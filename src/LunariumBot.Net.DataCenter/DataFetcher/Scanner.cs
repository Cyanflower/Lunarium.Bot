// .NET 核心库
using System.Collections.Concurrent;
using System.Text;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Rest;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;
using LunariumBot.Net.Extensions;

// 项目内命名空间
using LunariumBot.Net.DataCenter.DataProcess;
using LunariumBot.Net.DataCenter.Model;
using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.DataCenter.AppInteractionService;
using LunariumBot.Net.Scheduler;

namespace LunariumBot.Net.DataCenter.DataFetcher;

/// <summary>
/// Discord 论坛频道扫描器，负责扫描和收集论坛频道中的帖子数据
/// </summary>
public class Scanner
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly LunariumScheduler _scheduler;
    private readonly SemaphoreSlim _semaphore;
    private readonly DataProcessService _dataProcessService;
    private readonly ITokenizerService _tokenizerService;

    /// <summary>
    /// HTTP 请求重试策略，用于处理网络异常和临时性错误
    /// </summary>
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    /// <summary>
    /// Discord API 请求选项配置
    /// </summary>
    private readonly RequestOptions _requestOptions;

    /// <summary>
    /// 缓存的论坛频道字典，键为频道ID，值为对应的 SocketForumChannel 对象
    /// </summary>
    private Dictionary<ulong, SocketForumChannel> _socketForumChannelsCacheForTags = new();

    /// <summary>
    /// 需要部署的服务器ID集合，从配置文件中读取
    /// </summary>
    private HashSet<ulong> DeployGuildIds => _configLoader.Config.DeployConfig.DeployGuildIds.ToHashSet();

    /// <summary>
    /// 初始化 Scanner 实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="configLoader">配置加载器</param>
    /// <param name="discordSocketClient">Discord WebSocket 客户端</param>
    /// <param name="semaphore">信号量，用于控制并发请求数量</param>
    /// <param name="initializationService">初始化服务</param>
    /// <param name="tokenizerService">分词服务，用于处理文本搜索</param>
    public Scanner(
        ILogger logger,
        ConfigLoader configLoader,
        DiscordSocketClient discordSocketClient,
        LunariumScheduler scheduler,
        SemaphoreSlim semaphore,
        DataProcessService dataProcessService,
        ITokenizerService tokenizerService)
    {
        _logger = logger.ForContext("Scanner");
        _configLoader = configLoader;
        _discordSocketClient = discordSocketClient;
        _scheduler = scheduler;
        _semaphore = semaphore;
        _dataProcessService = dataProcessService;
        _tokenizerService = tokenizerService;
        _httpRetryPolicy = HttpRetryPolicyConfig();
        _requestOptions = RequestOptionsConfig();
    }

    /// <summary>
    /// 执行同步扫描，收集所有论坛频道、标签和帖子数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task<string> SyncScan(ScanProgressReporter? reporter = null)
    {
        _logger.Info("🚀 开始执行同步扫描任务...");

        try
        {
            var startTime = DateTimeOffset.UtcNow;
            
            // 辅助方法：计算经过的时间并格式化为 [T+HH:MM:SSS]
            string GetElapsedTime()
            {
                var elapsed = DateTimeOffset.UtcNow - startTime;
                return $"[T+{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}]";
            }
            if (reporter is not null) reporter.AppendLine($"T: <t:{startTime.ToUnixTimeSeconds()}:f> | <t:{startTime.ToUnixTimeSeconds()}:R>");

            // ==================== 数据收集阶段 ====================
                _logger.Info("{Time} 📥 第 1 阶段：收集频道和帖子数据...", GetElapsedTime());
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 📥 第 1 阶段：收集频道和帖子数据...");
                var phase1Start = DateTimeOffset.UtcNow;

            // === GetAllForumChannels
            var (allForumChannels, allTextChannels) = GetAllForumChannels();

                _logger.Info("{Time} ✅ 获取到 {ForumCount} 个论坛频道，{TextCount} 个文字频道",
                    GetElapsedTime(),
                    allForumChannels.Values.Sum(list => list.Count),
                    allTextChannels.Values.Sum(list => list.Count));
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 获取到 {allForumChannels.Values.Sum(l => l.Count)} 个论坛频道，{allTextChannels.Values.Sum(l => l.Count)} 个文字频道");

            // === GetAllRestForumThreads
            var allRestForumThreads = await GetAllRestForumThreads(allForumChannels);

                _logger.Info("{Time} ✅ 获取到 {ThreadCount} 个论坛帖子",
                    GetElapsedTime(),
                    allRestForumThreads.Values.Sum(list => list.Count));
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 获取到 {allRestForumThreads.Values.Sum(l => l.Count)} 个论坛帖子");

            // === GetAllRestSocketTextThreads
            var allSocketTextThreads = GetAllRestSocketTextThreads(allTextChannels);

                _logger.Info("{Time} ✅ 获取到 {ThreadCount} 个文字频道子区",
                    GetElapsedTime(),
                    allSocketTextThreads.Values.Sum(list => list.Count));
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} ✅ 获取到 {allSocketTextThreads.Values.Sum(l => l.Count)} 个文字频道子区");
                
                var phase1Duration = DateTimeOffset.UtcNow - phase1Start;
                _logger.Info("{Time} ⏱️ 第 1 阶段完成，耗时: {Duration:F2} 秒",
                    GetElapsedTime(), phase1Duration.TotalSeconds);
                if (reporter is not null) await  reporter.ReportProgressAsync($"> {GetElapsedTime()} 📥 ⏱️ 第 1 阶段完成，耗时: {phase1Duration.TotalSeconds:F2} 秒");

            // ==================== 数据处理阶段 ====================
                _logger.Info("{Time} 🔄 第 2 阶段：处理数据...", GetElapsedTime());
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 🔄 第 2 阶段：处理数据...");
                var phase2Start = DateTimeOffset.UtcNow;

            // === GetForumAndTagData
            var (allForumsData, allTagsData) = GetForumAndTagData(allForumChannels);

                _logger.Info("{Time} ✅ 处理得到 {ChannelCount} 个频道，{TagCount} 个标签",
                    GetElapsedTime(),
                    allForumsData.Count, allTagsData.Count);
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 处理得到 {allForumsData.Count} 个频道，{allTagsData.Count} 个标签");
                    
            // === GetAllRestForumThreadsData & GetAllSocketTextThreadsData
            var allForumThreadsData = await GetAllRestForumThreadsData(allRestForumThreads);
            var allTextThreadsData = GetAllSocketTextThreadsData(allSocketTextThreads);

                // 统计处理结果
                var forumSuccess = allForumThreadsData.Count(r => r.Status == ProcessingStatus.Success);
                var forumSkipped = allForumThreadsData.Count(r => r.Status == ProcessingStatus.Invalid);
                var forumFailed = allForumThreadsData.Count(r => r.Status == ProcessingStatus.Failed);
                
                var textSuccess = allTextThreadsData.Count(r => r.Status == ProcessingStatus.Success);
                var textSkipped = allTextThreadsData.Count(r => r.Status == ProcessingStatus.Invalid);
                var textFailed = allTextThreadsData.Count(r => r.Status == ProcessingStatus.Failed);

                _logger.Info("{Time} 📊 论坛帖子处理结果: 成功 {Success}, 跳过 {Skipped}, 失败 {Failed}",
                    GetElapsedTime(), forumSuccess, forumSkipped, forumFailed);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} 📊 论坛帖子处理结果: 成功 {forumSuccess}, 跳过 {forumSkipped}, 失败 {forumFailed}");
                _logger.Info("{Time} 📊 文字子区处理结果: 成功 {Success}, 跳过 {Skipped}, 失败 {Failed}",
                    GetElapsedTime(), textSuccess, textSkipped, textFailed);
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 📊 文字子区处理结果: 成功 {textSuccess}, 跳过 {textSkipped}, 失败 {textFailed}");

            // 合并所有帖子数据（排除失败的，保留跳过的）
            List<ThreadData> allThreadsData =
            [
                .. allForumThreadsData
                    .Where(r => r.Status != ProcessingStatus.Failed && r.Data is not null)
                    .Select(r => r.Data!),
                .. allTextThreadsData
                    .Where(r => r.Status != ProcessingStatus.Failed && r.Data is not null)
                    .Select(r => r.Data!),
            ];

                _logger.Info("{Time} 📦 合并后待同步数据: {ThreadCount} 个帖子（包含跳过的）",
                    GetElapsedTime(), allThreadsData.Count);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} 📦 合并后待同步数据: {allThreadsData.Count} 个帖子（包含跳过的）");
                var phase2Duration = DateTimeOffset.UtcNow - phase2Start;
                _logger.Info("{Time} ⏱️ 第 2 阶段完成，耗时: {Duration:F2} 秒",
                    GetElapsedTime(), phase2Duration.TotalSeconds);
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ⏱️ 第 2 阶段完成，耗时: {phase2Duration.TotalSeconds:F2} 秒");
                
            // ==================== 数据库写入阶段 ====================
                _logger.Info("{Time} 💾 第 3 阶段：写入数据库...", GetElapsedTime());
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 💾 第 3 阶段：写入数据库...");
                var phase3Start = DateTimeOffset.UtcNow;

            // === SyncProcessorAsync
            await _dataProcessService.SyncProcessorAsync(allForumsData, allTagsData, allThreadsData);

                var phase3Duration = DateTimeOffset.UtcNow - phase3Start;
                _logger.Info("{Time} ⏱️ 第 3 阶段完成，耗时: {Duration:F2} 秒",
                    GetElapsedTime(), phase3Duration.TotalSeconds);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} ⏱️ 第 3 阶段完成，耗时: {phase3Duration.TotalSeconds:F2} ");

                // ==================== 总结 ====================
                var totalDuration = DateTimeOffset.UtcNow - startTime;
                _logger.Info("{Time} ✅ 同步扫描任务完成！总耗时: {TotalDuration:F2} 秒",
                    GetElapsedTime(), totalDuration.TotalSeconds);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} ✅ 同步扫描任务完成！总耗时: {totalDuration.TotalSeconds:F2} 秒");
            var endTime = DateTimeOffset.UtcNow;
                if (reporter is not null) reporter.AppendLine($"T END: <t:{endTime.ToUnixTimeSeconds()}:f> | <t:{endTime.ToUnixTimeSeconds()}:R>");
                
                _logger.Info("📊 性能统计:");
                if (reporter is not null) reporter.AppendLine($"📊 性能统计:");
                _logger.Info("> - 第 1 阶段（数据收集）: {Phase1:F2} 秒 ({Phase1Percent:F1}%)",
                    phase1Duration.TotalSeconds, 
                    phase1Duration.TotalSeconds / totalDuration.TotalSeconds * 100);
                    
                if (reporter is not null) reporter.AppendLine($"> - 第 1 阶段（数据收集）: {phase1Duration.TotalSeconds:F2} 秒 ({phase1Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                _logger.Info("> - 第 2 阶段（数据处理）: {Phase2:F2} 秒 ({Phase2Percent:F1}%)",
                    phase2Duration.TotalSeconds, 
                    phase2Duration.TotalSeconds / totalDuration.TotalSeconds * 100);
                if (reporter is not null) reporter.AppendLine($"> - 第 2 阶段（数据处理）: {phase2Duration.TotalSeconds:F2} 秒 ({phase2Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                _logger.Info("> - 第 3 阶段（数据库写入）: {Phase3:F2} 秒 ({Phase3Percent:F1}%)",
                    phase3Duration.TotalSeconds, 
                    phase3Duration.TotalSeconds / totalDuration.TotalSeconds * 100);
                if (reporter is not null) await reporter.ReportProgressAsync($"> - 第 3 阶段（数据库写入）: {phase3Duration.TotalSeconds:F2} 秒 ({phase3Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");

                // 构建并返回统计字符串
                var statsBuilder = new StringBuilder();
                statsBuilder.AppendLine("📊 同步扫描任务统计报告");
                statsBuilder.AppendLine();
                if (reporter is null) statsBuilder.AppendLine("调度: 手动 (同步命令)");
                else statsBuilder.AppendLine("调度: 自动 (调度器)");
                statsBuilder.AppendLine($"▶️ 任务开始时间: <t:{startTime.ToUnixTimeSeconds()}:f>");
                statsBuilder.AppendLine($"⏹ 任务完成时间: <t:{endTime.ToUnixTimeSeconds()}:f>");
                statsBuilder.AppendLine($"⏱️ 总耗时: {totalDuration.TotalSeconds:F2} 秒");
                statsBuilder.AppendLine();
                statsBuilder.AppendLine("📥 数据收集结果:");
                statsBuilder.AppendLine($"> - 论坛频道: {allForumChannels.Values.Sum(list => list.Count)} 个");
                statsBuilder.AppendLine($"> - 文字频道: {allTextChannels.Values.Sum(list => list.Count)} 个");
                statsBuilder.AppendLine($"> - 论坛帖子: {allRestForumThreads.Values.Sum(list => list.Count)} 个");
                statsBuilder.AppendLine($"> - 文字子区: {allSocketTextThreads.Values.Sum(list => list.Count)} 个");
                statsBuilder.AppendLine();
                statsBuilder.AppendLine("🔄 数据处理结果:");
                statsBuilder.AppendLine($"> - 频道数: {allForumsData.Count} 个");
                statsBuilder.AppendLine($"> - 标签数: {allTagsData.Count} 个");
                statsBuilder.AppendLine($"> - 论坛帖子: 成功 {forumSuccess}, 跳过 {forumSkipped}, 失败 {forumFailed}");
                statsBuilder.AppendLine($"> - 文字子区: 成功 {textSuccess}, 跳过 {textSkipped}, 失败 {textFailed}");
                statsBuilder.AppendLine($"> - 合并后待同步: {allThreadsData.Count} 个帖子");
                statsBuilder.AppendLine();
                statsBuilder.AppendLine("⚡ 性能统计:");
                statsBuilder.AppendLine($"> - 第 1 阶段（数据收集）: {phase1Duration.TotalSeconds:F2} 秒 ({phase1Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                statsBuilder.AppendLine($"> - 第 2 阶段（数据处理）: {phase2Duration.TotalSeconds:F2} 秒 ({phase2Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                statsBuilder.AppendLine($"> - 第 3 阶段（数据库写入）: {phase3Duration.TotalSeconds:F2} 秒 ({phase3Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                statsBuilder.AppendLine();
                statsBuilder.AppendLine("⏰ 定时任务调度计划:");

                string schedulerInfo = _scheduler.GetAllJobsInfo();
                string formattedSchedulerInfo = schedulerInfo.Replace("\n", "\n> ");
                statsBuilder.AppendLine($"> {formattedSchedulerInfo}");

            return statsBuilder.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 同步扫描任务失败");
            
            var errorStats = $"❌ 同步扫描任务失败\n时间: <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:f>\n原因: {ex.Message}";
            
            try
            {
                var logChannel = (IMessageChannel)_discordSocketClient.GetChannel(_configLoader.Config.AuthConfig.LogChannel);
                var embed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ 同步扫描任务失败")
                    .WithDescription($"时间:<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:f><t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>\n原因:\n```\n{ex.Message.Slice(3000, "...")}\n```")
                    .Build();

                await logChannel.SendMessageAsync(embed: embed);
            }
            catch (Exception notifyEx)
            {
                // 放弃通知, 只记录
                _logger.Critical(notifyEx, "❌ 发送错误信息通知失败");
            }

            return errorStats;
        }
    }

    /// <summary>
    /// 获取所有配置的论坛频道
    /// </summary>
    /// <returns>字典，键为服务器（Guild），值为该服务器中的论坛频道列表</returns>
    private (Dictionary<SocketGuild, List<SocketForumChannel>>, Dictionary<SocketGuild, List<SocketTextChannel>>) GetAllForumChannels()
    {
        try
        {
            Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels = new();
            Dictionary<SocketGuild, List<SocketTextChannel>> allTextChannels = new();
            _socketForumChannelsCacheForTags = new();

            // 遍历配置的所有服务器
            foreach (var guildId in DeployGuildIds)
            {
                if (_configLoader.Config.DeployConfig.ExcludeGuildIds.Contains(guildId)) continue;
                List<SocketForumChannel> socketForumChannels = new();
                List<SocketTextChannel> socketTextChannels = new();
                var guild = _discordSocketClient.GetGuild(guildId);

                if (guild is not null)
                {
                    // 遍历服务器中的所有频道
                    foreach (var channel in guild.Channels)
                    {
                        if (_configLoader.Config.DeployConfig.ExcludeChannelIds.Contains(channel.Id)) continue;
                        if (channel is SocketForumChannel forumChannel)
                        {
                            if (!forumChannel.CategoryId.HasValue) continue;
                            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(forumChannel.CategoryId.Value)) continue;
                            socketForumChannels.Add(forumChannel);
                            // 同时缓存到字典中，方便后续快速查找
                            _socketForumChannelsCacheForTags[forumChannel.Id] = forumChannel;
                        }
                        if (channel.GetType() == typeof(SocketTextChannel))
                        {
                            var textChannel = (SocketTextChannel)channel;
                            if (!textChannel.CategoryId.HasValue) continue;
                            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(textChannel.CategoryId.Value)) continue;
                            socketTextChannels.Add(textChannel);
                        }
                    }
                    allForumChannels[guild] = socketForumChannels;
                    allTextChannels[guild] = socketTextChannels;
                }
            }
            return (allForumChannels, allTextChannels);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取论坛频道列表时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取所有论坛频道中的帖子（包括活跃帖子和归档帖子）
    /// </summary>
    /// <param name="allForumChannels">所有论坛频道的字典</param>
    /// <returns>字典，键为服务器（Guild），值为该服务器中所有论坛的帖子列表</returns>
    private async Task<Dictionary<SocketGuild, List<RestThreadChannel>>> GetAllRestForumThreads(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
    {
        try
        {
            Dictionary<SocketGuild, List<RestThreadChannel>> allRestForumThreads = new();
            foreach (var (guild, forumChannels) in allForumChannels)
            {
                List<RestThreadChannel> currentGuildForumThreads = new();
                List<Task<List<RestThreadChannel>>> tasks = new();

                // 为每个论坛频道创建一个异步任务
                foreach (var channel in forumChannels)
                {
                    Task<List<RestThreadChannel>> task = Task.Run(async () =>
                    {
                        try
                        {
                            // 使用信号量控制并发数量，避免超过 Discord API 速率限制
                            await _semaphore.WaitAsync();
                            List<RestThreadChannel> restThreads = new();

                            // 1. 获取活跃的帖子
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                _logger.Info("📥 正在获取活跃帖子: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                                    guild.Name, channel.Name, channel.Id);

                                var activeThreads = await channel.GetActiveThreadsAsync();

                                _logger.Info("✅ 成功获取活跃帖子: [{GuildName}.{ChannelName}] 数量 {Count}",
                                    guild.Name, channel.Name, channel.Id, activeThreads.Count);

                                restThreads.AddRange(activeThreads);
                            });

                            // 2. 获取公开归档的帖子（分页获取）
                            _logger.Info("📥 正在获取公开归档帖子: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                                guild.Name, channel.Name, channel.Id);

                            var publicArchivedThreads = new List<RestThreadChannel>();
                            DateTimeOffset? beforeTimestamp = null;
                            int page = 1;

                            // 分页循环获取所有归档帖子
                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    _logger.Debug("📄 正在获取第 {Page} 批公开归档帖子: [{GuildName}.{ChannelName}]",
                                        page, guild.Name, channel.Name, channel.Id);
                                    // 每次获取最多 100 个帖子
                                    fetchedThreads = await channel.GetPublicArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    publicArchivedThreads.AddRange(fetchedThreads);
                                    // 使用最早的归档时间戳作为下一次请求的基准
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    page++;
                                }
                                else
                                {
                                    // 没有更多帖子了，退出循环
                                    _logger.Info("✅ 所有公开归档帖子已获取完毕: [{GuildName}.{ChannelName}]",
                                        guild.Name, channel.Name, channel.Id);
                                    break;
                                }
                            }
                            restThreads.AddRange(publicArchivedThreads);
                            _logger.Info("✅ 总共获取 {Count} 个公开归档帖子: [{GuildName}.{ChannelName}]",
                                publicArchivedThreads.Count, guild.Name, channel.Name, channel.Id);

                            // 去重（活跃帖子可能与归档帖子有重复）
                            var uniqueRestThreads = restThreads.DistinctBy(t => t.Id).ToList();
                            _logger.Info("📊 总计获取 {Count} 个去重帖子: [{GuildName}.{ChannelName}]",
                                uniqueRestThreads.Count, guild.Name, channel.Name, channel.Id);

                            return uniqueRestThreads;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "❌ 获取论坛频道帖子时发生错误: [{GuildName}.{ChannelName}]",
                                guild.Name, channel.Name);
                            return new();
                        }
                        finally
                        {
                            // 释放信号量，允许其他任务继续
                            _semaphore.Release();
                        }
                    });
                    tasks.Add(task);
                }

                // 等待所有任务完成并合并结果
                currentGuildForumThreads = (await Task.WhenAll(tasks)).SelectMany(thread => thread).ToList();
                allRestForumThreads[guild] = currentGuildForumThreads;
            }
            return allRestForumThreads;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有帖子时发生错误");
            return new();
        }
    }

    private Dictionary<SocketGuild, List<SocketThreadChannel>> GetAllRestSocketTextThreads(Dictionary<SocketGuild, List<SocketTextChannel>> allTextChannels)
    {
        try
        {
            Dictionary<SocketGuild, List<SocketThreadChannel>> allRestTextThreads = new();
            foreach (var (guild, textChannels) in allTextChannels)
            {
                List<SocketThreadChannel> currentGuildTextThreads = new();
                foreach (var channel in textChannels)
                {
                    try
                    {
                        currentGuildTextThreads.AddRange(channel.Threads);

                        _logger.Info("✅ 总共获取 {Count} 个子区: [{GuildName}.{ChannelName}]",
                            channel.Threads.Count, guild.Name, channel.Name, channel.Id);

                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "❌ 获取论坛频道子区时发生错误: [{GuildName}.{ChannelName}]",
                            guild.Name, channel.Name);
                    }
                }
                allRestTextThreads[guild] = currentGuildTextThreads;
            }
            return allRestTextThreads;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有子区时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取所有帖子的详细数据
    /// </summary>
    /// <param name="allRestForumThreads">所有帖子的字典</param>
    /// <returns>包含所有帖子处理结果的列表</returns>
    private async Task<List<ProcessingResult>> GetAllRestForumThreadsData(Dictionary<SocketGuild, List<RestThreadChannel>> allRestForumThreads)
    {
        try
        {
            // 为每个帖子创建一个异步任务来获取其详细数据
            var tasks = allRestForumThreads.SelectMany(threads => threads.Value).Select(async thread =>
            {
                try
                {
                    // 使用信号量控制并发数量
                    await _semaphore.WaitAsync();
                    var result = await GetForumThreadData(thread);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取帖子数据时发生错误");
                    return new()
                    {
                        Status = ProcessingStatus.Failed
                    };
                }
                finally
                {
                    _semaphore.Release();
                }

            });

            // 等待所有任务完成
            var allForumThreadsData = await Task.WhenAll(tasks);
            _logger.Info("✅ 成功获取所有帖子数据，总计 {Count} 个", allForumThreadsData.Length);
            return allForumThreadsData.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有帖子数据时发生错误");
            return new();
        }
    }

    private List<ProcessingResult> GetAllSocketTextThreadsData(Dictionary<SocketGuild, List<SocketThreadChannel>> allSocketTextThreads)
    {
        try
        {
            var result = allSocketTextThreads.SelectMany(threads => threads.Value).Select( thread =>
            {
                try
                {
                    var result = GetTextThreadData(thread);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取子区数据时发生错误");
                    return new()
                    {
                        Status = ProcessingStatus.Failed
                    };
                }
            });

            var allForumThreadsData = result.ToList();
            _logger.Info("✅ 成功获取所有子区数据，总计 {Count} 个", allForumThreadsData.Count);
            return allForumThreadsData.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有子区数据时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取论坛频道和标签的数据
    /// </summary>
    /// <param name="allForumChannels">所有论坛频道的字典</param>
    /// <returns>元组，包含论坛频道数据列表和标签信息列表</returns>
    private (List<ChannelData> ForumsData, List<TagInfo> TagsData) GetForumAndTagData(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
    {
        try
        {
            // 提取所有论坛频道的基本信息
            List<ChannelData> forumData = allForumChannels
                .SelectMany(kvpair => kvpair.Value)
                .Select(forum => new ChannelData()
                {
                    ChannelId = forum.Id,
                    ChannelName = forum.Name,
                    GuildId = forum.Guild.Id,
                    GuildName = forum.Guild.Name
                })
                .ToList();

            // 提取所有论坛频道的标签信息
            // 使用嵌套的 SelectMany 将论坛频道展平为标签
            List<TagInfo> tagsData = allForumChannels
                .SelectMany(
                    kvpair => kvpair.Value,
                    (kvpair, forum) => new { Guild = kvpair.Key, Forum = forum }
                )
                .SelectMany(
                    ctx => ctx.Forum.Tags,
                    (ctx, tag) => new TagInfo()
                    {
                        TagId = tag.Id,
                        TagName = tag.Name,
                        ChannelId = ctx.Forum.Id,
                        ChannelName = ctx.Forum.Name,
                        GuildName = ctx.Guild.Name
                    }
                )
                .ToList();

            return (forumData, tagsData);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取论坛频道和标签数据时发生错误");
            return (new(), new());
        }
    }

    /// <summary>
    /// 获取单个帖子的详细数据
    /// </summary>
    /// <param name="restThreadChannel">帖子频道</param>
    /// <returns>包含帖子数据的处理结果</returns>
    private async Task<ProcessingResult> GetForumThreadData(RestThreadChannel restThreadChannel)
    {
        try
        {
            var isValid = true;
            var isLocked = false;
            ProcessingResult result = new();

            // 规则检查: 跳过锁定的帖子
            if (restThreadChannel.IsLocked)
            {
                _logger.Debug("🔒 帖子已锁定，视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                isLocked = true;
                result.Status = ProcessingStatus.Invalid;
            }

            // 规则检查: 跳过置顶的帖子
            if (restThreadChannel.Flags.HasFlag(ChannelFlags.Pinned))
            {
                _logger.Debug("📌 帖子已置顶，视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                result.Status = ProcessingStatus.Invalid;
            }

            RestMessage? firstMessage = null;
            RestMessage? lastMessage = null;
            Attachment? imageAttachment = null;
            int highestReactionCount = 0;

            // 获取帖子的首楼消息和最新消息
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    // 获取首楼消息（帖子ID等于首楼消息ID）
                    firstMessage = await restThreadChannel.GetMessageAsync(restThreadChannel.Id, _requestOptions);

                    // 获取最新消息
                    // SelectManyAsync 会把 IAsyncEnumerable<批次> 转换成 IAsyncEnumerable<单个消息>
                    lastMessage = await restThreadChannel.GetMessagesAsync(1, _requestOptions)
                                               .SelectMany(batch => batch.ToAsyncEnumerable()) // 压平操作
                                               .FirstOrDefaultAsync(); // 获取第一个元素
                                               
                    if (firstMessage is null || string.IsNullOrWhiteSpace(firstMessage.Content))
                    {
                        _logger.Debug("⚠️ 帖子没有有效首楼消息: {ThreadName} (ID: {ThreadId}, 尝试备用方法)",
                    restThreadChannel.Name, restThreadChannel.Id);
                        var iChannel = await _discordSocketClient.GetChannelAsync(restThreadChannel.Id);
                        firstMessage = await restThreadChannel.GetMessageAsync(restThreadChannel.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取帖子的首楼消息和最新消息失败");
                    throw;
                }
            });

            // 验证首楼消息是否有效
            if (firstMessage is null)
            {
                _logger.Warning("⚠️ 帖子没有有效首楼消息: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                _logger.Debug($"https://discord.com/channels/{restThreadChannel.GuildId}/{restThreadChannel.Id}/0");
                isValid = false;
                result.Status = ProcessingStatus.Invalid;
            }
            else
            {
                // 提取首楼消息中的图片附件
                imageAttachment = firstMessage.Attachments.FirstOrDefault(a => IsImageAttachment(a));
                // 获取首楼消息的最高反应数
                highestReactionCount = firstMessage.Reactions.Values
                    .Select(r => r.ReactionCount)
                    .DefaultIfEmpty(0)
                    .Max();
            }

            // 获取帖子的标签信息
            string? tags = null;
            List<ulong> validTagIds = new();
            if (!_socketForumChannelsCacheForTags.TryGetValue(restThreadChannel.ParentChannelId, out var parentChannel))
            {
                // 如果因为某些极其罕见的原因找不到父频道
                _logger.Error("❌ 数据不一致错误！无法找到父频道缓存: 帖子 {ThreadName} (ID: {ThreadId}), 父频道ID {ParentChannelId}",
                    restThreadChannel.Name, restThreadChannel.Id, restThreadChannel.ParentChannelId);
                isValid = false;
                result.Status = ProcessingStatus.Failed;
            }
            else
            {
                var validTags = parentChannel.Tags
                    .Where(tag => restThreadChannel.AppliedTags.Contains(tag.Id))
                    .ToList();
                
                validTagIds = validTags.Select(tag => tag.Id).ToList();
                var appliedTagNames = validTags.Select(tag => tag.Name).ToList();
                tags = string.Join(", ", appliedTagNames);

                // 如果帖子有应用标签，但没有有效标签，记录警告
                if (restThreadChannel.AppliedTags.Any() && !validTagIds.Any())
                {
                    _logger.Warning("⚠️ 帖子 {ThreadName} (ID: {ThreadId}) 应用的所有标签都已不存在于论坛中",
                        restThreadChannel.Name, restThreadChannel.Id);
                }
            }

            if (!_configLoader.ChannelSet.Contains(restThreadChannel.CategoryId ?? 0))
            {
                _logger.Debug("⚠️ 帖子已不在作品区内，视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                _logger.Debug("_configLoader.ChannelSet: {ChannelSet}", _configLoader.ChannelSet);
                isValid = false;
                result.Status = ProcessingStatus.Invalid;
            }

            // 构建帖子数据对象
            result.Data = new ThreadData()
            {
                ThreadId = restThreadChannel.Id,
                FirstMessageId = restThreadChannel.Id,
                AuthorId = restThreadChannel.OwnerId,
                ChannelId = restThreadChannel.CategoryId ?? 0,
                ChannelName = parentChannel is not null ? parentChannel.Name : $"UnknownChannel:{restThreadChannel.CategoryId ?? 0}",
                GuildId = restThreadChannel.GuildId,
                GuildName = parentChannel is not null ? parentChannel.Guild.Name : $"UnknownGuild:{restThreadChannel.GuildId}",
                CreatedAt = restThreadChannel.CreatedAt,
                ReactionCount = highestReactionCount,
                ReplyCount = restThreadChannel.MessageCount,
                LastActiveAt = lastMessage is not null ? lastMessage.CreatedAt : restThreadChannel.CreatedAt,
                JumpUrl = $"https://discord.com/channels/{restThreadChannel.GuildId}/{restThreadChannel.Id}/0",
                AttachmentUrl = imageAttachment?.Url,

                Title = restThreadChannel.Name,
                FirstMessageContent = firstMessage?.Content,

                TitleTokenized = _tokenizerService.CutForSearch(restThreadChannel.Name),
                ContentTokenized = firstMessage is not null ? _tokenizerService.CutForSearch(firstMessage.Content) : null,

                // 更新相关
                UpdatedAt = null,
                UpdatedJumpUrl = null,
                UpdateCount = 0,

                Tags = tags,
                TagsId = validTagIds,

                IsValid = isValid,
                IsLocked = isLocked,
                IsDeleted = false,
                InForum = true,
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取帖子数据时发生错误: {ThreadName} (ID: {ThreadId})",
                restThreadChannel.Name, restThreadChannel.Id);
            return new()
            {
                Status = ProcessingStatus.Failed,
            };
        }
    }

    private ProcessingResult GetTextThreadData(SocketThreadChannel socketThreadChannel)
    {
        try
        {
            var isValid = false;
            var isLocked = false;
            ProcessingResult result = new();

            if (socketThreadChannel.IsLocked)
            {
                _logger.Debug("🔒 子区已锁定，视为无效: {ThreadName} (ID: {ThreadId})",
                    socketThreadChannel.Name, socketThreadChannel.Id);
                isValid = false;
                isLocked = true;
                result.Status = ProcessingStatus.Invalid;
            }

            if (socketThreadChannel.Flags.HasFlag(ChannelFlags.Pinned))
            {
                _logger.Debug("📌 子区已置顶，视为无效: {ThreadName} (ID: {ThreadId})",
                    socketThreadChannel.Name, socketThreadChannel.Id);
                isValid = false;
                result.Status = ProcessingStatus.Invalid;
            }

            var threadInterface  = (IThreadChannel)socketThreadChannel;

            RestMessage? firstMessage = null;
            RestMessage? lastMessage = null;
            Attachment? imageAttachment = null;

            // 构建帖子数据对象
            result.Data = new ThreadData()
            {
                ThreadId = socketThreadChannel.Id,
                FirstMessageId = socketThreadChannel.Id,
                AuthorId = threadInterface.OwnerId,
                ChannelId = socketThreadChannel.CategoryId ?? 0,
                ChannelName = socketThreadChannel.ParentChannel is not null ? socketThreadChannel.ParentChannel.Name : $"UnknownChannel:{socketThreadChannel.CategoryId ?? 0}",
                GuildId = socketThreadChannel.Guild is not null ? socketThreadChannel.Guild.Id : 0,
                GuildName = socketThreadChannel.ParentChannel is not null ? socketThreadChannel.ParentChannel.Guild.Name : "UnknownGuild",
                CreatedAt = socketThreadChannel.CreatedAt,
                ReactionCount = 0,
                ReplyCount = socketThreadChannel.MessageCount,
                LastActiveAt = lastMessage is not null ? lastMessage.CreatedAt : socketThreadChannel.CreatedAt,
                JumpUrl = $"https://discord.com/channels/{(socketThreadChannel.Guild is not null ? socketThreadChannel.Guild.Id : 0)}/{socketThreadChannel.Id}/0",
                AttachmentUrl = imageAttachment?.Url,

                Title = socketThreadChannel.Name,
                FirstMessageContent = firstMessage?.Content,

                TitleTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),
                ContentTokenized = firstMessage is not null ? _tokenizerService.CutForSearch(firstMessage.Content) : null,

                // 更新相关
                UpdatedAt = null,
                UpdatedJumpUrl = null,
                UpdateCount = 0,

                Tags = null,
                TagsId = [],

                IsValid = isValid,
                IsLocked = isLocked,
                IsDeleted = false,
                InForum = false,
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取子区数据时发生错误: {ThreadName} (ID: {ThreadId})",
                socketThreadChannel.Name, socketThreadChannel.Id);
            return new()
            {
                Status = ProcessingStatus.Failed,
            };
        }
    }

    /// <summary>
    /// 判断附件是否为图片类型
    /// </summary>
    /// <param name="attachment">Discord 消息附件</param>
    /// <returns>如果是图片类型返回 true，否则返回 false</returns>
    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }

    /// <summary>
    /// 配置 HTTP 请求重试策略
    /// </summary>
    /// <returns>Polly 异步重试策略</returns>
    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        // --- Polly 策略配置 ---
        // 配置一个重试策略, 专门用于处理Http的网络重试逻辑
        return Policy
            // 定义需要处理的异常:
            // - 处理特定类型的 Discord.HttpException (例如 500, 502, 503, 429 等)
            //   不重试的情况: 404(资源不存在), 403(权限不足), 400(请求格式错误)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 || (int)ex.HttpCode != 403 || (int)ex.HttpCode != 400)
            // - 处理网络层级的异常，比如 SSL 握手失败、连接被重置等
            .Or<HttpRequestException>()
            // - 对 HttpClient 超时的处理
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)

            // 定义重试逻辑
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试3次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 指数退避: 2s, 4s, 8s
                onRetry: (exception, timespan, attempt, context) => // 每次重试时执行的回调
                {
                    // 在日志中可以更清晰地看到是哪种异常触发了重试
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Warning("⚠️ HTTP 请求失败 ({ErrorType})，将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }

    /// <summary>
    /// 配置 Discord API 请求选项
    /// </summary>
    /// <returns>Discord.Net 请求选项</returns>
    public static RequestOptions RequestOptionsConfig()
    {
        // RequestOptions: 维持 Discord.Net 的默认重试机制
        // Timeout 设置一个比 HttpClient 超时更长的时间, 确保 Polly 有足够时间完成重试
        return new RequestOptions
        {
            Timeout = 20 * 60 * 1000, // 设置总超时为20分钟（以毫秒为单位）
            RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试机制
        };
    }
}