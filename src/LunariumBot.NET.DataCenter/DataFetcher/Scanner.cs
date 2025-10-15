// .NET 核心库
using System.Collections.Concurrent;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Rest;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;
using LunariumBot.Net.DataCenter.Utils;

namespace LunariumBot.Net.DataCenter.DataFetcher;

/// <summary>
/// Discord 论坛频道扫描器，负责扫描和收集论坛频道中的帖子数据
/// </summary>
public class Scanner
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly SemaphoreSlim _semaphore;
    private readonly InitializationService _initializationService;
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
    private Dictionary<ulong, SocketForumChannel> _socketForumChannels = new();

    /// <summary>
    /// 需要扫描的频道ID集合，从配置文件中读取
    /// </summary>
    private HashSet<ulong> ScanChannelIDs => _configLoader.Config.GuildsConfig.ScanChannelIDs.Values.SelectMany(list => list).ToHashSet();

    /// <summary>
    /// 需要部署的服务器ID集合，从配置文件中读取
    /// </summary>
    private HashSet<ulong> DeployGuildIds => _configLoader.Config.GuildsConfig.DeployGuildIds.ToHashSet();

    /// <summary>
    /// 初始化 Scanner 实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="configLoader">配置加载器</param>
    /// <param name="discordSocketClient">Discord WebSocket 客户端</param>
    /// <param name="semaphore">信号量，用于控制并发请求数量</param>
    /// <param name="initializationService">初始化服务</param>
    /// <param name="tokenizerService">分词服务，用于处理文本搜索</param>
    public Scanner(ILogger logger, ConfigLoader configLoader, DiscordSocketClient discordSocketClient, SemaphoreSlim semaphore, InitializationService initializationService, ITokenizerService tokenizerService)
    {
        _logger = logger.ForContext("Scanner");
        _configLoader = configLoader;
        _discordSocketClient = discordSocketClient;
        _semaphore = semaphore;
        _initializationService = initializationService;
        _tokenizerService = tokenizerService;
        _httpRetryPolicy = HttpRetryPolicyConfig();
        _requestOptions = RequestOptionsConfig();
    }

    /// <summary>
    /// 执行同步扫描，收集所有论坛频道、标签和帖子数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task SyncScan()
    {
        _logger.Info("🚀 开始执行同步扫描任务...");

        // 数据收集处理
        var allForumChannels = GetAllForumChannels();
        var allRestThreads = await GetAllRestThreads(allForumChannels);

        // 获取全部论坛频道和标签数据
        var allForumChannelsData = GetForumAndTagData(allForumChannels);

        // 获取全部帖子数据
        var allThreadsData = await GetAllThreadsData(allRestThreads);

        // 写入数据库
        // TODO: 实现数据库写入 allForumChannelsData 和 allThreadsData 逻辑


        _logger.Info("✅ 同步扫描任务完成。");
    }

    /// <summary>
    /// 获取所有配置的论坛频道
    /// </summary>
    /// <returns>字典，键为服务器（Guild），值为该服务器中的论坛频道列表</returns>
    private Dictionary<SocketGuild, List<SocketForumChannel>> GetAllForumChannels()
    {
        try
        {
            Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels = new();
            _socketForumChannels = new();

            // 遍历配置的所有服务器
            foreach (var guildId in DeployGuildIds)
            {
                List<SocketForumChannel> socketForumChannels = new();
                var guild = _discordSocketClient.GetGuild(guildId);
                if (guild != null)
                {
                    // 遍历服务器中的所有频道
                    foreach (var channel in guild.Channels)
                    {
                        // 只收集配置中指定的论坛频道
                        if (channel is SocketForumChannel forumChannel && channel.GetType() == typeof(SocketForumChannel) && ScanChannelIDs.Contains(channel.Id))
                        {
                            socketForumChannels.Add(forumChannel);
                            // 同时缓存到字典中，方便后续快速查找
                            _socketForumChannels[forumChannel.Id] = forumChannel;
                        }
                    }
                    allForumChannels[guild] = socketForumChannels;
                }
            }
            return allForumChannels;
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
    private async Task<Dictionary<SocketGuild, List<RestThreadChannel>>> GetAllRestThreads(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
    {
        try
        {
            Dictionary<SocketGuild, List<RestThreadChannel>> allRestThreads = new();
            foreach (var (guild, forumChannels) in allForumChannels)
            {
                List<RestThreadChannel> currentGuildThreads = new();
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
                currentGuildThreads = (await Task.WhenAll(tasks)).SelectMany(thread => thread).ToList();
                allRestThreads[guild] = currentGuildThreads;
            }
            return allRestThreads;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有帖子时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取所有帖子的详细数据
    /// </summary>
    /// <param name="allRestThreads">所有帖子的字典</param>
    /// <returns>包含所有帖子处理结果的列表</returns>
    private async Task<List<ProcessingResult>> GetAllThreadsData(Dictionary<SocketGuild, List<RestThreadChannel>> allRestThreads)
    {
        try
        {
            // 为每个帖子创建一个异步任务来获取其详细数据
            var tasks = allRestThreads.SelectMany(threads => threads.Value).Select(async thread =>
            {
                try
                {
                    // 使用信号量控制并发数量
                    await _semaphore.WaitAsync();
                    var result = await GetThreadData(thread);
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
            var allThreadsData = await Task.WhenAll(tasks);
            _logger.Info("✅ 成功获取所有帖子数据，总计 {Count} 个", allThreadsData.Length);
            return allThreadsData.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有帖子数据时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取论坛频道和标签的数据
    /// </summary>
    /// <param name="allForumChannels">所有论坛频道的字典</param>
    /// <returns>元组，包含论坛频道数据列表和标签信息列表</returns>
    private async Task<(List<ChannelData> ForumsData, List<TagInfo> TagsData)> GetForumAndTagData(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
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
    private async Task<ProcessingResult> GetThreadData(RestThreadChannel restThreadChannel)
    {
        try
        {
            var isValid = true;
            var isLocked = false;
            ProcessingResult result = new();

            // 规则检查: 跳过锁定的帖子
            if (restThreadChannel.IsLocked)
            {
                _logger.Debug("🔒 帖子已锁定，跳过处理: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                isLocked = true;
                result.Status = ProcessingStatus.Skipped;
            }

            // 规则检查: 跳过置顶的帖子
            if (restThreadChannel.Flags.HasFlag(ChannelFlags.Pinned))
            {
                _logger.Debug("📌 帖子已置顶，跳过处理: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                result.Status = ProcessingStatus.Skipped;
            }

            RestMessage? firstMessage = null;
            RestMessage? lastMessage = null;
            Attachment? imageAttachment = null;
            int highestReactionCount = 0;

            // 获取帖子的首楼消息和最新消息
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息（帖子ID等于首楼消息ID）
                firstMessage = await restThreadChannel.GetMessageAsync(restThreadChannel.Id, _requestOptions);

                // 获取最新消息
                // SelectManyAsync 会把 IAsyncEnumerable<批次> 转换成 IAsyncEnumerable<单个消息>
                lastMessage = await restThreadChannel.GetMessagesAsync(1, _requestOptions)
                                           .SelectMany(batch => batch.ToAsyncEnumerable()) // 压平操作
                                           .FirstOrDefaultAsync(); // 获取第一个元素
            });

            // 验证首楼消息是否有效
            if (firstMessage == null || string.IsNullOrWhiteSpace(firstMessage.Content))
            {
                _logger.Warning("⚠️ 帖子没有有效首楼消息: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                result.Status = ProcessingStatus.Skipped;
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
            if (!_socketForumChannels.TryGetValue(restThreadChannel.ParentChannelId, out var parentChannel))
            {
                // 如果因为某些极其罕见的原因找不到父频道
                _logger.Error("❌ 数据不一致错误！无法找到父频道缓存: 帖子 {ThreadName} (ID: {ThreadId}), 父频道ID {ParentChannelId}",
                    restThreadChannel.Name, restThreadChannel.Id, restThreadChannel.ParentChannelId);
                isValid = false;
                result.Status = ProcessingStatus.Failed;
            }
            else
            {
                // 从父频道中获取帖子应用的标签名称
                var appliedTagNames = parentChannel.Tags
                    .Where(tag => restThreadChannel.AppliedTags.Contains(tag.Id))
                    .Select(tag => tag.Name)
                    .ToList();
                tags = string.Join(", ", appliedTagNames);
            }

            // 备注: 成员计数相关代码（目前已注释）
            // var memberCount = restThreadChannel.MemberCount;
            // if (_initializationService.NeedInitialization)
            // {
            //     int userMember = 0;
            //     await foreach (var userBatch in restThreadChannel.GetThreadUsersAsync(100, _requestOptions))
            //     {
            //         userMember += userBatch.Count;
            //     }
            // }

            // 构建帖子数据对象
            result.Data = new ThreadData()
            {
                ThreadId = restThreadChannel.Id,
                FirstMessageId = restThreadChannel.Id,
                AuthorId = restThreadChannel.OwnerId,
                ChannelId = restThreadChannel.CategoryId ?? 0,
                ChannelName = parentChannel != null ? parentChannel.Name : $"UnknownChannel:{restThreadChannel.CategoryId ?? 0}",
                GuildId = restThreadChannel.GuildId,
                GuildName = parentChannel != null ? parentChannel.Guild.Name : $"UnknownGuild:{restThreadChannel.GuildId}",
                CreatedAt = restThreadChannel.CreatedAt,
                ReactionCount = highestReactionCount,
                ReplyCount = restThreadChannel.MessageCount,
                LastActiveAt = lastMessage != null ? lastMessage.CreatedAt : restThreadChannel.CreatedAt,
                JumpUrl = $"https://discord.com/channels/{restThreadChannel.GuildId}/{restThreadChannel.Id}/0",
                AttachmentUrl = imageAttachment?.Url,

                Tags = tags,
                TagsId = restThreadChannel.AppliedTags.ToList(),

                Title = restThreadChannel.Name,
                FirstMessageContent = firstMessage?.Content,

                // 使用分词服务对标题和内容进行分词，用于后续搜索
                TitleTokenized = _tokenizerService.CutForSearch(restThreadChannel.Name),
                ContentTokenized = firstMessage != null ? _tokenizerService.CutForSearch(firstMessage.Content) : null,

                IsValid = isValid,
                IsLocked = isLocked
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