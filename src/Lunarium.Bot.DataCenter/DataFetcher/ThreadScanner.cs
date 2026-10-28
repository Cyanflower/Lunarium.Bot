// .NET 核心库
using System.Collections.Concurrent;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Rest;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using Lunarium.Logging;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.OpenAPI;

// 项目内命名空间
using Lunarium.Bot.DataCenter.DataProcess;
using Lunarium.Bot.DataCenter.Database;
using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.DataCenter.Utils;
using Lunarium.Bot.DataCenter.AppInteractionService;
using Lunarium.Scheduler;

namespace Lunarium.Bot.DataCenter.DataFetcher;

/// <summary>
/// Discord 论坛频道扫描器, 负责扫描和收集论坛频道中的帖子数据
/// 该类完全不会依赖 DataProcess 处理层
/// 而是直接接入 Database 服务
/// 保持独立的实现
/// </summary>
public class ThreadScanner
{
    private readonly ILogger _logger;
    private readonly ILogger _loggerForScan;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly LunariumScheduler _scheduler;
    private readonly RateLimiter _apiRequestRateLimiter;
    private readonly DataProcessService _dataProcessService;
    private readonly DatabaseService _databaseService;
    private readonly ThreadActivityTracker _activityTracker;

    /// <summary>
    /// HTTP 请求重试策略, 用于处理网络异常和临时性错误
    /// </summary>
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    /// <summary>
    /// Discord API 请求选项配置
    /// </summary>
    private readonly RequestOptions _requestOptions;

    /// <summary>
    /// 缓存的论坛频道字典, 键为频道ID, 值为对应的 SocketForumChannel 对象
    /// </summary>
    private Dictionary<ulong, SocketForumChannel> _socketForumChannelsCacheForTags = new();

    /// <summary>
    /// 缓存的文字频道字典, 键为频道ID, 值为对应的 SocketTextChannel 对象
    /// </summary>
    private Dictionary<ulong, SocketTextChannel> _socketTextChannelsCache = new();

    /// <summary>
    /// 需要部署的服务器ID集合, 从配置文件中读取
    /// </summary>
    private HashSet<ulong> DeployGuildIds => _configLoader.Config.DeployConfig.ScanConfigs.Keys.ToHashSet();

    /// <summary>
    /// 本次列举失败的频道（任一频道列举失败就把它排除在孤儿清理范围之外，避免误删）。
    /// </summary>
    private readonly ConcurrentDictionary<ulong, byte> _listingFailedChannels = new();

    /// <summary>
    /// 初始化 Scanner 实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="configLoader">配置加载器</param>
    /// <param name="discordSocketClient">Discord WebSocket 客户端</param>
    /// <param name="rateLimiter">速率桶, 用于控制并发请求数量</param>
    /// <param name="initializationService">初始化服务</param>
    /// <param name="tokenizerService">分词服务, 用于处理文本搜索</param>
    public ThreadScanner(
        ILogger logger,
        ConfigLoader configLoader,
        DiscordSocketClient discordSocketClient,
        LunariumScheduler scheduler,
        DataProcessService dataProcessService,
        DatabaseService databaseService,
        ThreadActivityTracker activityTracker,
        RateLimiter rateLimiter,
        RequestOptions requestOptions,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy
    )
    {
        _logger = logger.ForContext("Scanner"); 
        _loggerForScan = logger.ForContext("Scan");
        _configLoader = configLoader;
        _discordSocketClient = discordSocketClient;
        _scheduler = scheduler;
        _apiRequestRateLimiter = rateLimiter;
        _dataProcessService = dataProcessService;
        _databaseService = databaseService;
        _activityTracker = activityTracker;
        _requestOptions = requestOptions;
        _httpRetryPolicy = httpRetryPolicy;
    }

    /// <summary>
    /// 执行同步扫描, 收集所有论坛频道、标签和帖子数据
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task<string> SyncScanAsync(ScanMode mode = ScanMode.Incremental, ScanProgressReporter? reporter = null)
    {
        _logger.Info("🚀 开始执行同步扫描任务...");
        _listingFailedChannels.Clear();

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

                _logger.Info("{Time} ✅ 获取到 {ForumCount} 个论坛频道, {TextCount} 个文字频道",
                    GetElapsedTime(),
                    allForumChannels.Values.Sum(list => list.Count),
                    allTextChannels.Values.Sum(list => list.Count));
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 获取到 {allForumChannels.Values.Sum(l => l.Count)} 个论坛频道, {allTextChannels.Values.Sum(l => l.Count)} 个文字频道");

            // === GetAllRestForumThreads
            var allRestForumThreads = await GetAllRestForumThreads(allForumChannels);

                _logger.Info("{Time} ✅ 获取到 {ThreadCount} 个论坛帖子",
                    GetElapsedTime(),
                    allRestForumThreads.Values.Sum(list => list.Count));
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 获取到 {allRestForumThreads.Values.Sum(l => l.Count)} 个论坛帖子");
                _loggerForScan.Info("API GET: 全部论坛帖子: {@allRestForumThreads}", allRestForumThreads);
                
            // === GetAllRestSocketTextThreads
            var allSocketTextThreads = await GetAllRestSocketTextThreads(allTextChannels);

                _logger.Info("{Time} ✅ 获取到 {ThreadCount} 个文字频道子区",
                    GetElapsedTime(),
                    allSocketTextThreads.Values.Sum(list => list.Count));
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} ✅ 获取到 {allSocketTextThreads.Values.Sum(l => l.Count)} 个文字频道子区");
                _loggerForScan.Info("API GET: 全部频道帖子: {@allSocketTextThreads}", allSocketTextThreads);
                
                var phase1Duration = DateTimeOffset.UtcNow - phase1Start;
                _logger.Info("{Time} ⏱️ 第 1 阶段完成, 耗时: {Duration:F2} 秒",
                    GetElapsedTime(), phase1Duration.TotalSeconds);
                if (reporter is not null) await  reporter.ReportProgressAsync($"> {GetElapsedTime()} 📥 ⏱️ 第 1 阶段完成, 耗时: {phase1Duration.TotalSeconds:F2} 秒");

            // ==================== 数据处理阶段 ====================
                _logger.Info("{Time} 🔄 第 2 阶段：处理数据...", GetElapsedTime());
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 🔄 第 2 阶段：处理数据...");
                var phase2Start = DateTimeOffset.UtcNow;

            // === GetForumAndTagData
            var (allForumsData, allTagsData) = GetForumAndTagData(allForumChannels);

                _logger.Info("{Time} ✅ 处理得到 {ChannelCount} 个频道, {TagCount} 个标签",
                    GetElapsedTime(),
                    allForumsData.Count, allTagsData.Count);
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 处理得到 {allForumsData.Count} 个频道, {allTagsData.Count} 个标签");
                    
            // === 增量判定：第 1 阶段已经拿到全部线程对象，第 2 阶段只深挖脏集合与新增帖子
            List<RestThreadChannel> forumThreads = allRestForumThreads.SelectMany(kv => kv.Value).ToList();
            List<RestThreadChannel> textThreads = allSocketTextThreads.SelectMany(kv => kv.Value).ToList();
            List<ulong> allListedThreadIds = forumThreads.Select(t => t.Id)
                .Concat(textThreads.Select(t => t.Id))
                .ToList();
            List<ulong> allListedChannelIds = forumThreads.Select(t => t.CategoryId ?? 0)
                .Concat(textThreads.Select(t => t.CategoryId ?? 0))
                .Where(id => id != 0 && !_listingFailedChannels.ContainsKey(id))
                .Distinct()
                .ToList();

            HashSet<ulong> dirtyThreadIds = _activityTracker.Snapshot().ToHashSet();
            Dictionary<ulong, ThreadScanBaseline> baselines = await _databaseService.GetThreadScanBaselinesAsync();

            List<RestThreadChannel> forumThreadsToDeepScan = mode == ScanMode.Full
                ? forumThreads
                : forumThreads
                    .Where(t =>
                        dirtyThreadIds.Contains(t.Id)
                        || !baselines.TryGetValue(t.Id, out var baseline)
                        || baseline.IsDeleted)
                    .ToList();

            _logger.Info("{Time} 🎯 扫描模式: {Mode}, 全量列举 {Total} 条, 本次深挖 {Deep} 条, 脏集合 {Dirty} 条",
                GetElapsedTime(), mode, forumThreads.Count, forumThreadsToDeepScan.Count, dirtyThreadIds.Count);
            if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 🎯 扫描模式: {mode}, 全量列举 {forumThreads.Count} 条, 本次深挖 {forumThreadsToDeepScan.Count} 条");

            // === GetAllRestForumThreadsData & GetAllSocketTextThreadsData
            var allForumThreadsData = await GetAllRestForumThreadsData(forumThreadsToDeepScan);
            var allTextThreadsData = GetAllRestTextThreadsData(allSocketTextThreads);

                // 统计处理结果
                var forumSuccess = allForumThreadsData.Count(r => r.Status == ProcessingStatus.Success && r.Data is not null);
                var forumSkipped = allForumThreadsData.Count(r => r.Status == ProcessingStatus.Invalid && r.Data is not null);
                var forumFailed = allForumThreadsData.Count(r => r.Status == ProcessingStatus.Failed || r.Data is null);
                
                var textSuccess = allTextThreadsData.Count(r => r.Status == ProcessingStatus.Success && r.Data is not null);
                var textSkipped = allTextThreadsData.Count(r => r.Status == ProcessingStatus.Invalid && r.Data is not null);
                var textFailed = allTextThreadsData.Count(r => r.Status == ProcessingStatus.Failed || r.Data is null);

                _logger.Info("{Time} 📊 论坛帖子处理结果: 成功 {Success}, 无效 {Skipped}, 失败 {Failed}",
                    GetElapsedTime(), forumSuccess, forumSkipped, forumFailed);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} 📊 论坛帖子处理结果: 成功 {forumSuccess}, 无效 {forumSkipped}, 失败 {forumFailed}");
                _logger.Info("{Time} 📊 文字子区处理结果: 成功 {Success}, 无效 {Skipped}, 失败 {Failed}",
                    GetElapsedTime(), textSuccess, textSkipped, textFailed);
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 📊 文字子区处理结果: 成功 {textSuccess}, 无效 {textSkipped}, 失败 {textFailed}");

            // 合并所有帖子数据（排除失败的, 保留无效的）
            List<ThreadData> allThreadsData =
            [
                .. allForumThreadsData
                    .Where(r => r.Status != ProcessingStatus.Failed && r.Data is not null)
                    .Select(r => r.Data!),
                .. allTextThreadsData
                    .Where(r => r.Status != ProcessingStatus.Failed && r.Data is not null)
                    .Select(r => r.Data!),
            ];
            // === 增量：未被深挖的帖子只计算元数据（标题/标签/锁定/有效性）变化
            List<ThreadData> metadataUpdates = [];
            if (mode == ScanMode.Incremental)
            {
                HashSet<ulong> deepScannedIds = forumThreadsToDeepScan.Select(t => t.Id).ToHashSet();
                foreach (var thread in forumThreads)
                {
                    if (deepScannedIds.Contains(thread.Id)) continue;

                    var metadata = BuildThreadMetadata(thread);
                    if (baselines.TryGetValue(thread.Id, out var baseline)
                        && baseline.Title == metadata.Title
                        && baseline.Tags == metadata.Tags
                        && baseline.IsLocked == metadata.IsLocked
                        && baseline.IsValid == metadata.IsValid)
                    {
                        continue;
                    }

                    metadataUpdates.Add(metadata);
                }

                if (metadataUpdates.Count > 0)
                {
                    _logger.Info("{Time} 🔧 检测到 {Count} 条帖子元数据变化（无需深挖）",
                        GetElapsedTime(), metadataUpdates.Count);
                }
            }

            _loggerForScan.Info("合并所有帖子数据（排除失败的, 保留无效的）");
            _loggerForScan.Info("所有帖子数据: 成功结果列表: allThreadsData: {@allThreadsData}", allThreadsData);
            _loggerForScan.Info("所有帖子数据: 失败结果列表: allThreadsData: {@allThreadsDataFailed}", allForumThreadsData.Where(r => r.Status == ProcessingStatus.Failed || r.Data is null));
            _loggerForScan.Info("所有帖子数据: 无效结果列表: allThreadsData: {@allThreadsDataInvalid}", allForumThreadsData.Where(r => r.Status == ProcessingStatus.Invalid && r.Data is not null));

                _logger.Info("{Time} 📦 合并后待同步数据: {ThreadCount} 个帖子（包含无效的）",
                    GetElapsedTime(), allThreadsData.Count);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} 📦 合并后待同步数据: {allThreadsData.Count} 个帖子（包含无效的）");
                var phase2Duration = DateTimeOffset.UtcNow - phase2Start;
                _logger.Info("{Time} ⏱️ 第 2 阶段完成, 耗时: {Duration:F2} 秒",
                    GetElapsedTime(), phase2Duration.TotalSeconds);
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ⏱️ 第 2 阶段完成, 耗时: {phase2Duration.TotalSeconds:F2} 秒");
                
            // ==================== 数据库写入阶段 ====================
                _logger.Info("{Time} 💾 第 3 阶段：写入数据库...", GetElapsedTime());
                if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 💾 第 3 阶段：写入数据库...");
                var phase3Start = DateTimeOffset.UtcNow;

            // === SyncProcessorAsync（孤儿清理使用全量列举 ID，不能用增量数据）
            await _dataProcessService.SyncAllThreadAsync(
                allForumsData, allTagsData, allThreadsData,
                allListedThreadIds, allListedChannelIds);

            if (metadataUpdates.Count > 0)
            {
                await _dataProcessService.UpdateThreadsMetadataAsync(metadataUpdates);
            }

            // === 消费脏集合（只移除成功深挖的；全量扫描直接清空）
            if (mode == ScanMode.Full)
            {
                _activityTracker.Clear();
            }
            else
            {
                var succeededIds = allForumThreadsData
                    .Where(r => r.Status != ProcessingStatus.Failed && r.Data is not null)
                    .Select(r => r.Data!.ThreadId);
                _activityTracker.RemoveConsumed(succeededIds);

                var listedIds = allListedThreadIds.ToHashSet();
                _activityTracker.RemoveConsumed(dirtyThreadIds.Where(id => !listedIds.Contains(id)).ToArray());
            }
            await _activityTracker.FlushAsync();

                var phase3Duration = DateTimeOffset.UtcNow - phase3Start;
                _logger.Info("{Time} ⏱️ 第 3 阶段完成, 耗时: {Duration:F2} 秒",
                    GetElapsedTime(), phase3Duration.TotalSeconds);
                if (reporter is not null) reporter.AppendLine($"> {GetElapsedTime()} ⏱️ 第 3 阶段完成, 耗时: {phase3Duration.TotalSeconds:F2} ");

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
                statsBuilder.AppendLine($"> - 扫描模式: {mode}");
                statsBuilder.AppendLine($"> - 全量列举帖子: {forumThreads.Count + textThreads.Count} 个");
                statsBuilder.AppendLine($"> - 本次深挖: {forumThreadsToDeepScan.Count} 个");
                statsBuilder.AppendLine($"> - 元数据更新: {metadataUpdates.Count} 个");
                statsBuilder.AppendLine($"> - 频道数: {allForumsData.Count} 个");
                statsBuilder.AppendLine($"> - 标签数: {allTagsData.Count} 个");
                statsBuilder.AppendLine($"> - 论坛帖子: 成功 {forumSuccess}, 无效 {forumSkipped}, 失败 {forumFailed}");
                statsBuilder.AppendLine($"> - 文字子区: 成功 {textSuccess}, 无效 {textSkipped}, 失败 {textFailed}");
                statsBuilder.AppendLine($"> - 合并后待同步: {allThreadsData.Count} 个帖子");
                statsBuilder.AppendLine();
                statsBuilder.AppendLine("⚡ 性能统计:");
                statsBuilder.AppendLine($"> - 第 1 阶段（数据收集）: {phase1Duration.TotalSeconds:F2} 秒 ({phase1Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                statsBuilder.AppendLine($"> - 第 2 阶段（数据处理）: {phase2Duration.TotalSeconds:F2} 秒 ({phase2Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                statsBuilder.AppendLine($"> - 第 3 阶段（数据库写入）: {phase3Duration.TotalSeconds:F2} 秒 ({phase3Duration.TotalSeconds / totalDuration.TotalSeconds * 100:F1}%)");
                statsBuilder.AppendLine();
                statsBuilder.AppendLine("⏰ 定时任务调度计划:");

                string schedulerInfo = _scheduler.GetAllJobsInfo();
                // 先移除末尾换行符, 再替换
                string formattedSchedulerInfo = schedulerInfo.TrimEnd('\n', '\r').Replace("\n", "\n> ");
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

                using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
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
    /// <returns>字典, 键为服务器（Guild）, 值为该服务器中的论坛频道列表</returns>
    private (Dictionary<SocketGuild, List<SocketForumChannel>>, Dictionary<SocketGuild, List<SocketTextChannel>>) GetAllForumChannels()
    {
        try
        {
            Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels = new();
            Dictionary<SocketGuild, List<SocketTextChannel>> allTextChannels = new();
            _socketForumChannelsCacheForTags = new();
            _socketTextChannelsCache = new();

            _loggerForScan.Info("========== 开始扫描所有频道 ==========");
            _loggerForScan.Info("📊 配置的服务器数量: {Count}", DeployGuildIds.Count);
            _loggerForScan.Info("🚫 排除的服务器数量: {Count}", _configLoader.Config.DeployConfig.ExcludeGuildIds.Count);
            _loggerForScan.Info("🚫 排除的频道数量: {Count}", _configLoader.Config.DeployConfig.ExcludeChannelIds.Count);
            _loggerForScan.Info("🚫 排除的分类数量: {Count}", _configLoader.Config.DeployConfig.ExcludeCategoryIds.Count);

            int totalForumChannels = 0;
            int totalTextChannels = 0;
            int totalChannels = 0;
            int totalExcludedChannels = 0;

            // 遍历配置的所有服务器
            foreach (var guildId in DeployGuildIds)
            {
                if (_configLoader.Config.DeployConfig.ExcludeGuildIds.Contains(guildId))
                {
                    _loggerForScan.Info("⏭️  无效排除的服务器 ID: {GuildId}", guildId);
                    continue;
                }

                List<SocketForumChannel> socketForumChannels = new();
                List<SocketTextChannel> socketTextChannels = new();
                var guild = _discordSocketClient.GetGuild(guildId);

                if (guild is not null)
                {
                    _loggerForScan.Info("========================================");
                    _loggerForScan.Info("🏰 服务器: {GuildName} (ID: {GuildId})", guild.Name, guild.Id);
                    _loggerForScan.Info("📂 服务器频道总数: {Count}", guild.Channels.Count);

                    int guildForumCount = 0;
                    int guildTextCount = 0;
                    int guildExcludedCount = 0;
                    int guildOtherCount = 0;

                    // 遍历服务器中的所有频道
                    for (int i = 0; i < guild.Channels.Count; i++)
                    {
                        var channel = guild.Channels.ElementAt(i);
                        totalChannels++;

                        string channelTypeName = channel.GetType().Name;
                        _loggerForScan.Info("  [{Index}/{Total}] 频道: {ChannelName} (ID: {ChannelId}) | 类型: {ChannelType}",
                            i + 1, guild.Channels.Count, channel.Name, channel.Id, channelTypeName);

                        // 检查是否在排除列表
                        if (_configLoader.Config.DeployConfig.ExcludeChannelIds.Contains(channel.Id))
                        {
                            _loggerForScan.Info("    ⏭️  频道已被排除");
                            guildExcludedCount++;
                            totalExcludedChannels++;
                            continue;
                        }

                        // 处理论坛频道
                        if (channel is SocketForumChannel forumChannel)
                        {
                            _loggerForScan.Info("    ✅ 识别为论坛频道 (SocketForumChannel)");
                            _loggerForScan.Info("    📁 分类 ID: {CategoryId}", forumChannel.CategoryId?.ToString() ?? "无");

                            if (!forumChannel.CategoryId.HasValue)
                            {
                                _loggerForScan.Info("    ⚠️  论坛频道无分类, 无效");
                                continue;
                            }

                            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(forumChannel.CategoryId.Value))
                            {
                                _loggerForScan.Info("    ⏭️  论坛频道所属分类已被排除");
                                guildExcludedCount++;
                                totalExcludedChannels++;
                                continue;
                            }

                            socketForumChannels.Add(forumChannel);
                            _socketForumChannelsCacheForTags[forumChannel.Id] = forumChannel;
                            guildForumCount++;
                            totalForumChannels++;
                            _loggerForScan.Info("    ✅ 论坛频道已添加");
                        }
                        // 处理文字频道
                        else if (channel.GetType() == typeof(SocketTextChannel))
                        {
                            _loggerForScan.Info("    ✅ 识别为文字频道 (SocketTextChannel)");
                            var textChannel = (SocketTextChannel)channel;
                            _loggerForScan.Info("    📁 分类 ID: {CategoryId}", textChannel.CategoryId?.ToString() ?? "无");
                            _loggerForScan.Info("    🧵 子区数量: {ThreadCount}", textChannel.Threads.Count);

                            if (!textChannel.CategoryId.HasValue)
                            {
                                _loggerForScan.Info("    ⚠️  文字频道无分类, 无效");
                                continue;
                            }

                            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(textChannel.CategoryId.Value))
                            {
                                _loggerForScan.Info("    ⏭️  文字频道所属分类已被排除");
                                guildExcludedCount++;
                                totalExcludedChannels++;
                                continue;
                            }

                            socketTextChannels.Add(textChannel);
                            _socketTextChannelsCache[channel.Id] = textChannel;
                            guildTextCount++;
                            totalTextChannels++;
                            _loggerForScan.Info("    ✅ 文字频道已添加");
                        }
                        else
                        {
                            _loggerForScan.Info("    ℹ️  其他类型频道(channelType), 无效", channel.GetType().Name);
                            guildOtherCount++;
                        }
                    }

                    allForumChannels[guild] = socketForumChannels;
                    allTextChannels[guild] = socketTextChannels;

                    _loggerForScan.Info("----------------------------------------");
                    _loggerForScan.Info("📊 服务器 [{GuildName}] 统计:", guild.Name);
                    _loggerForScan.Info("  - 论坛频道: {Count} 个", guildForumCount);
                    _loggerForScan.Info("  - 文字频道: {Count} 个", guildTextCount);
                    _loggerForScan.Info("  - 已排除: {Count} 个", guildExcludedCount);
                    _loggerForScan.Info("  - 其他类型: {Count} 个", guildOtherCount);
                }
                else
                {
                    _loggerForScan.Warning("⚠️  无法获取服务器 ID: {GuildId}", guildId);
                }
            }

            _loggerForScan.Info("========================================");
            _loggerForScan.Info("📊 扫描完成 - 总体统计:");
            _loggerForScan.Info("  - 总频道数: {Count}", totalChannels);
            _loggerForScan.Info("  - 论坛频道: {Count}", totalForumChannels);
            _loggerForScan.Info("  - 文字频道: {Count}", totalTextChannels);
            _loggerForScan.Info("  - 已排除: {Count}", totalExcludedChannels);
            _loggerForScan.Info("========== 频道扫描完成 ==========\n");

            return (allForumChannels, allTextChannels);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取论坛频道列表时发生错误");
            _loggerForScan.Error(ex, "❌ 获取论坛频道列表时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取所有论坛频道中的帖子（包括活跃帖子和归档帖子）
    /// </summary>
    /// <param name="allForumChannels">所有论坛频道的字典</param>
    /// <returns>字典, 键为服务器（Guild）, 值为该服务器中所有论坛的帖子列表</returns>
    private async Task<Dictionary<SocketGuild, List<RestThreadChannel>>> GetAllRestForumThreads(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
    {
        try
        {
            _loggerForScan.Info("========== 开始获取论坛帖子 ==========");
            int totalForumChannels = allForumChannels.Sum(x => x.Value.Count);
            _loggerForScan.Info("📊 待扫描的论坛频道总数: {Count}", totalForumChannels);

            Dictionary<SocketGuild, List<RestThreadChannel>> allRestForumThreads = new();
            int processedChannelCount = 0;

            foreach (var (guild, forumChannels) in allForumChannels)
            {
                _loggerForScan.Info("========================================");
                _loggerForScan.Info("🏰 服务器: {GuildName} (ID: {GuildId})", guild.Name, guild.Id);
                _loggerForScan.Info("📂 论坛频道数量: {Count}", forumChannels.Count);

                List<RestThreadChannel> currentGuildForumThreads = new();
                List<Task<List<RestThreadChannel>>> tasks = new();

                // 为每个论坛频道创建一个异步任务
                foreach (var channel in forumChannels)
                {
                    int channelIndex = forumChannels.IndexOf(channel) + 1;
                    Task<List<RestThreadChannel>> task = Task.Run(async () =>
                    {
                        try
                        {
                            _loggerForScan.Info("----------------------------------------");
                            _loggerForScan.Info("📋 [{Index}/{Total}] 论坛: {ChannelName} (ID: {ChannelId})",
                                channelIndex, forumChannels.Count, channel.Name, channel.Id);

                            List<RestThreadChannel> restThreads = new();

                            // 1. 获取活跃的帖子
                            int activeCount = 0;
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                // 控制速率
                                using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);

                                _loggerForScan.Info("  📥 正在获取活跃帖子...");
                                var activeThreads = await channel.GetActiveThreadsAsync();

                                activeCount = activeThreads.Count;
                                _loggerForScan.Info("  ✅ 获取到 {Count} 个活跃帖子", activeCount);

                                restThreads.AddRange(activeThreads);
                            });

                            // 2. 获取公开归档的帖子（分页获取）
                            _loggerForScan.Info("  📥 开始获取公开归档帖子...");
                            var publicArchivedThreads = new List<RestThreadChannel>();
                            DateTimeOffset? beforeTimestamp = null;
                            int publicPage = 1;

                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    // 控制速率
                                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                                    _loggerForScan.Info("    📄 正在获取第 {Page} 批公开归档帖子...", publicPage);
                                    fetchedThreads = await channel.GetPublicArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    publicArchivedThreads.AddRange(fetchedThreads);
                                    _loggerForScan.Info("    ✅ 第 {Page} 批: 获取到 {Count} 个帖子", publicPage, fetchedThreads.Count);
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    publicPage++;
                                }
                                else
                                {
                                    _loggerForScan.Info("    ✅ 公开归档帖子获取完毕, 共 {TotalPages} 批", publicPage - 1);
                                    break;
                                }
                            }

                            restThreads.AddRange(publicArchivedThreads);
                            _loggerForScan.Info("  ✅ 总共获取 {Count} 个公开归档帖子", publicArchivedThreads.Count);

                            // 3. 获取私密归档的帖子（分页获取）
                            _loggerForScan.Info("  📥 开始获取私密归档帖子...");
                            var privateArchivedThreads = new List<RestThreadChannel>();
                            beforeTimestamp = null;
                            int privatePage = 1;

                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    // 控制速率
                                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                                    _loggerForScan.Info("    📄 正在获取第 {Page} 批私密归档帖子...", privatePage);
                                    fetchedThreads = await channel.GetPrivateArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    privateArchivedThreads.AddRange(fetchedThreads);
                                    _loggerForScan.Info("    ✅ 第 {Page} 批: 获取到 {Count} 个帖子", privatePage, fetchedThreads.Count);
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    privatePage++;
                                }
                                else
                                {
                                    _loggerForScan.Info("    ✅ 私密归档帖子获取完毕, 共 {TotalPages} 批", privatePage - 1);
                                    break;
                                }
                            }

                            restThreads.AddRange(privateArchivedThreads);
                            _loggerForScan.Info("  ✅ 总共获取 {Count} 个私密归档帖子", privateArchivedThreads.Count);

                            // 去重（活跃帖子可能与归档帖子有重复）
                            var uniqueRestThreads = restThreads.DistinctBy(t => t.Id).ToList();
                            int duplicateCount = restThreads.Count - uniqueRestThreads.Count;

                            _loggerForScan.Info("  📊 统计:");
                            _loggerForScan.Info("    - 活跃帖子: {Count}", activeCount);
                            _loggerForScan.Info("    - 公开归档: {Count}", publicArchivedThreads.Count);
                            _loggerForScan.Info("    - 私密归档: {Count}", privateArchivedThreads.Count);
                            _loggerForScan.Info("    - 重复移除: {Count}", duplicateCount);
                            _loggerForScan.Info("    - 去重后总计: {Count}", uniqueRestThreads.Count);

                            // 记录每个帖子的详细信息
                            _loggerForScan.Info("  📋 帖子详细列表:");
                            for (int i = 0; i < uniqueRestThreads.Count; i++)
                            {
                                var thread = uniqueRestThreads[i];
                                _loggerForScan.Info("    [{Index}] 帖子: {ThreadName} | ID: {ThreadId} | 作者ID: {OwnerId} | 类型: {ThreadType} | 归档: {IsArchived}",
                                    i + 1, thread.Name, thread.Id, thread.OwnerId, thread.Type, thread.IsArchived);
                            }

                            Interlocked.Increment(ref processedChannelCount);
                            _loggerForScan.Info("  ✅ 论坛扫描完成 [{Processed}/{Total}]", processedChannelCount, totalForumChannels);

                            return uniqueRestThreads;
                        }
                        catch (Exception ex)
                        {
                            _listingFailedChannels[channel.Id] = 0;
                            _logger.Error(ex, "❌ 获取论坛频道帖子时发生错误: [{GuildName}.{ChannelName}]",
                                guild.Name, channel.Name);
                            _loggerForScan.Error(ex, "❌ 获取论坛频道帖子时发生错误: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                                guild.Name, channel.Name, channel.Id);
                            return new();
                        }
                    });
                    tasks.Add(task);
                }

                // 等待所有任务完成并合并结果
                var results = await Task.WhenAll(tasks);
                currentGuildForumThreads = results.SelectMany(thread => thread).ToList();
                allRestForumThreads[guild] = currentGuildForumThreads;

                _loggerForScan.Info("📊 服务器 [{GuildName}] 论坛帖子总计: {Count}", guild.Name, currentGuildForumThreads.Count);
            }

            int totalThreads = allRestForumThreads.Sum(x => x.Value.Count);
            _loggerForScan.Info("========================================");
            _loggerForScan.Info("📊 论坛帖子获取完成:");
            _loggerForScan.Info("  - 扫描的论坛频道: {Count}", totalForumChannels);
            _loggerForScan.Info("  - 获取的帖子总数: {Count}", totalThreads);
            _loggerForScan.Info("========== 论坛帖子获取完成 ==========\n");
            _loggerForScan.Info("结果列表: allRestForumThreads: {@allRestForumThreads}", allRestForumThreads);

            return allRestForumThreads;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有论坛帖子时发生错误");
            _loggerForScan.Error(ex, "❌ 获取所有论坛帖子时发生错误");
            return new();
        }
    }

    /// <summary>
    /// 获取所有文字频道中的子区（包括活跃子区和归档子区）
    /// </summary>
    /// <param name="allTextChannels">所有文字频道的字典</param>
    /// <returns>字典, 键为服务器（Guild）, 值为该服务器中所有文字频道的子区列表</returns>
    private async Task<Dictionary<SocketGuild, List<RestThreadChannel>>> GetAllRestSocketTextThreads(Dictionary<SocketGuild, List<SocketTextChannel>> allTextChannels)
    {
        try
        {
            _loggerForScan.Info("========== 开始获取文字频道子区 ==========");
            int totalTextChannels = allTextChannels.Sum(x => x.Value.Count);
            _loggerForScan.Info("📊 待扫描的文字频道总数: {Count}", totalTextChannels);

            Dictionary<SocketGuild, List<RestThreadChannel>> allRestTextThreads = new();
            int processedChannelCount = 0;

            foreach (var (guild, textChannels) in allTextChannels)
            {
                _loggerForScan.Info("========================================");
                _loggerForScan.Info("🏰 服务器: {GuildName} (ID: {GuildId})", guild.Name, guild.Id);
                _loggerForScan.Info("📂 文字频道数量: {Count}", textChannels.Count);

                List<RestThreadChannel> currentGuildTextThreads = new();
                List<Task<List<RestThreadChannel>>> tasks = new();

                // 为每个文字频道创建一个异步任务
                foreach (var channel in textChannels)
                {
                    int channelIndex = textChannels.IndexOf(channel) + 1;
                    Task<List<RestThreadChannel>> task = Task.Run(async () =>
                    {
                        try
                        {
                            _loggerForScan.Info("----------------------------------------");
                            _loggerForScan.Info("📋 [{Index}/{Total}] 频道: {ChannelName} (ID: {ChannelId})",
                                channelIndex, textChannels.Count, channel.Name, channel.Id);

                            List<RestThreadChannel> restThreads = new();

                            // 1. 获取活跃的子区
                            int activeCount = 0;
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                // 控制速率
                                using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                                _loggerForScan.Info("  📥 正在获取活跃子区...");
                                var activeThreads = await channel.GetActiveThreadsAsync();
                                activeCount = activeThreads.Count;
                                _loggerForScan.Info("  ✅ 获取到 {Count} 个活跃子区", activeCount);
                                restThreads.AddRange(activeThreads);
                            });

                            // 2. 获取公开归档的子区（分页获取）
                            _loggerForScan.Info("  📥 开始获取公开归档子区...");
                            var publicArchivedThreads = new List<RestThreadChannel>();
                            DateTimeOffset? beforeTimestamp = null;
                            int publicPage = 1;

                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    // 控制速率
                                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                                    _loggerForScan.Info("    📄 正在获取第 {Page} 批公开归档子区...", publicPage);
                                    fetchedThreads = await channel.GetPublicArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    publicArchivedThreads.AddRange(fetchedThreads);
                                    _loggerForScan.Info("    ✅ 第 {Page} 批: 获取到 {Count} 个子区", publicPage, fetchedThreads.Count);
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    publicPage++;
                                }
                                else
                                {
                                    _loggerForScan.Info("    ✅ 公开归档子区获取完毕, 共 {TotalPages} 批", publicPage - 1);
                                    break;
                                }
                            }

                            restThreads.AddRange(publicArchivedThreads);
                            _loggerForScan.Info("  ✅ 总共获取 {Count} 个公开归档子区", publicArchivedThreads.Count);

                            // 3. 获取私密归档的子区（分页获取）
                            _loggerForScan.Info("  📥 开始获取私密归档子区...");
                            var privateArchivedThreads = new List<RestThreadChannel>();
                            beforeTimestamp = null;
                            int privatePage = 1;

                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    // 控制速率
                                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                                    _loggerForScan.Info("    📄 正在获取第 {Page} 批私密归档子区...", privatePage);
                                    fetchedThreads = await channel.GetPrivateArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    privateArchivedThreads.AddRange(fetchedThreads);
                                    _loggerForScan.Info("    ✅ 第 {Page} 批: 获取到 {Count} 个子区", privatePage, fetchedThreads.Count);
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    privatePage++;
                                }
                                else
                                {
                                    _loggerForScan.Info("    ✅ 私密归档子区获取完毕, 共 {TotalPages} 批", privatePage - 1);
                                    break;
                                }
                            }

                            restThreads.AddRange(privateArchivedThreads);
                            _loggerForScan.Info("  ✅ 总共获取 {Count} 个私密归档子区", privateArchivedThreads.Count);

                            // 获取已加入的私密归档子区（分页获取）
                            _loggerForScan.Info("  📥 开始获取已加入的私密归档子区...");
                            var joinedPrivateArchivedThreads = new List<RestThreadChannel>();
                            beforeTimestamp = null;
                            int joinedPage = 1;

                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    // 控制速率
                                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                                    _loggerForScan.Info("    📄 正在获取第 {Page} 批已加入的私密归档子区...", joinedPage);
                                    fetchedThreads = await channel.GetJoinedPrivateArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    joinedPrivateArchivedThreads.AddRange(fetchedThreads);
                                    _loggerForScan.Info("    ✅ 第 {Page} 批: 获取到 {Count} 个子区", joinedPage, fetchedThreads.Count);
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    joinedPage++;
                                }
                                else
                                {
                                    _loggerForScan.Info("    ✅ 已加入的私密归档子区获取完毕, 共 {TotalPages} 批", joinedPage - 1);
                                    break;
                                }
                            }

                            restThreads.AddRange(joinedPrivateArchivedThreads);
                            _loggerForScan.Info("  ✅ 总共获取 {Count} 个已加入的私密归档子区", joinedPrivateArchivedThreads.Count);

                            // 去重
                            var uniqueRestThreads = restThreads.DistinctBy(t => t.Id).ToList();
                            int duplicateCount = restThreads.Count - uniqueRestThreads.Count;

                            _loggerForScan.Info("  📊 统计:");
                            _loggerForScan.Info("    - 活跃子区: {Count}", activeCount);
                            _loggerForScan.Info("    - 公开归档: {Count}", publicArchivedThreads.Count);
                            _loggerForScan.Info("    - 私密归档: {Count}", privateArchivedThreads.Count);
                            _loggerForScan.Info("    - 已加入私密归档: {Count}", joinedPrivateArchivedThreads.Count);
                            _loggerForScan.Info("    - 重复移除: {Count}", duplicateCount);
                            _loggerForScan.Info("    - 去重后总计: {Count}", uniqueRestThreads.Count);

                            // 记录每个子区的详细信息
                            _loggerForScan.Info("  📋 子区详细列表:");
                            for (int i = 0; i < uniqueRestThreads.Count; i++)
                            {
                                var thread = uniqueRestThreads[i];
                                _loggerForScan.Info("    [{Index}] 子区: {ThreadName} | ID: {ThreadId} | 作者ID: {OwnerId} | 类型: {ThreadType} | 归档: {IsArchived} | 父频道: {ParentName} (ID: {ParentId})",
                                    i + 1, thread.Name, thread.Id, thread.OwnerId, thread.Type, thread.IsArchived, channel.Name, channel.Id);
                            }

                            Interlocked.Increment(ref processedChannelCount);
                            _loggerForScan.Info("  ✅ 文字频道扫描完成 [{Processed}/{Total}]", processedChannelCount, totalTextChannels);

                            return uniqueRestThreads;
                        }
                        catch (Exception ex)
                        {
                            _listingFailedChannels[channel.Id] = 0;
                            _logger.Error(ex, "❌ 获取文字频道子区时发生错误: [{GuildName}.{ChannelName}]",
                                guild.Name, channel.Name);
                            _loggerForScan.Error(ex, "❌ 获取文字频道子区时发生错误: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                                guild.Name, channel.Name, channel.Id);
                            return new();
                        }
                    });
                    tasks.Add(task);
                }

                // 等待所有任务完成并合并结果
                var results = await Task.WhenAll(tasks);
                currentGuildTextThreads = results.SelectMany(thread => thread).ToList();
                allRestTextThreads[guild] = currentGuildTextThreads;

                _loggerForScan.Info("📊 服务器 [{GuildName}] 文字频道子区总计: {Count}", guild.Name, currentGuildTextThreads.Count);
            }

            int totalThreads = allRestTextThreads.Sum(x => x.Value.Count);
            _loggerForScan.Info("========================================");
            _loggerForScan.Info("📊 文字频道子区获取完成:");
            _loggerForScan.Info("  - 扫描的文字频道: {Count}", totalTextChannels);
            _loggerForScan.Info("  - 获取的子区总数: {Count}", totalThreads);
            _loggerForScan.Info("========== 文字频道子区获取完成 ==========\n");
            _loggerForScan.Info("结果列表: allRestTextThreads: {@allRestTextThreads}", allRestTextThreads);

            return allRestTextThreads;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有文字频道子区时发生错误");
            _loggerForScan.Error(ex, "❌ 获取所有文字频道子区时发生错误");
            return new();
        }
    }


    /// <summary>
    /// 获取所有帖子的详细数据
    /// </summary>
    /// <param name="allRestForumThreads">所有帖子的字典</param>
    /// <returns>包含所有帖子处理结果的列表</returns>
    private async Task<List<ProcessingResult>> GetAllRestForumThreadsData(IEnumerable<RestThreadChannel> threads)
    {
        try
        {
            // 为每个帖子创建一个异步任务来获取其详细数据
            var tasks = threads.Select(async thread =>
            {
                try
                {
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

            });

            // 等待所有任务完成
            var allForumThreadsData = await Task.WhenAll(tasks);
            _logger.Info("✅ 成功获取所有帖子数据, 总计 {Count} 个", allForumThreadsData.Length);
            return allForumThreadsData.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取所有帖子数据时发生错误");
            return new();
        }
    }

    private List<ProcessingResult> GetAllRestTextThreadsData(Dictionary<SocketGuild, List<RestThreadChannel>> allRestTextThreads)
    {
        try
        {
            var result = allRestTextThreads.SelectMany(threads => threads.Value).Select( thread =>
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
            _logger.Info("✅ 成功获取所有子区数据, 总计 {Count} 个", allForumThreadsData.Count);
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
    /// <returns>元组, 包含论坛频道数据列表和标签信息列表</returns>
    private (List<ChannelData> ForumsData, List<TagData> TagsData) GetForumAndTagData(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
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
            List<TagData> tagsData = allForumChannels
                .SelectMany(
                    kvpair => kvpair.Value,
                    (kvpair, forum) => new { Guild = kvpair.Key, Forum = forum }
                )
                .SelectMany(
                    ctx => ctx.Forum.Tags,
                    (ctx, tag) => new TagData()
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
    /// <summary>
    /// 构建只含元数据（标题/标签/锁定/有效性）的 ThreadData，用于增量扫描中无需深挖的帖子。
    /// </summary>
    private ThreadData BuildThreadMetadata(RestThreadChannel thread)
    {
        var isValid = true;
        var isLocked = false;

        if (thread.IsLocked)
        {
            isValid = false;
            isLocked = true;
        }

        if (thread.Flags.HasFlag(ChannelFlags.Pinned))
        {
            isValid = false;
        }

        if (!_configLoader.ValidChannelSet.Contains(thread.CategoryId ?? 0))
        {
            isValid = false;
        }

        string? tags = null;
        List<ulong> validTagIds = [];
        if (_socketForumChannelsCacheForTags.TryGetValue(thread.ParentChannelId, out var parentChannel))
        {
            var validTags = parentChannel.Tags
                .Where(tag => thread.AppliedTags.Contains(tag.Id))
                .ToList();
            validTagIds = validTags.Select(tag => tag.Id).ToList();
            tags = string.Join(", ", validTags.Select(tag => tag.Name));
        }

        return new ThreadData
        {
            ThreadId = thread.Id,
            ChannelId = thread.CategoryId ?? 0,
            Title = thread.Name,
            TitleTokenized = JiebaTokenizerService.CutForSearch(thread.Name),
            Tags = tags,
            TagsId = validTagIds,
            IsValid = isValid,
            IsLocked = isLocked,
            InForum = true,
        };
    }

    private async Task<ProcessingResult> GetForumThreadData(RestThreadChannel restThreadChannel)
    {
        try
        {
            var isValid = true;
            var isLocked = false;
            ProcessingResult result = new();

            // 规则检查: 无效锁定的帖子
            if (restThreadChannel.IsLocked)
            {
                _loggerForScan.Info("🔒 帖子已锁定, 视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                isLocked = true;
                result.Status = ProcessingStatus.Invalid;
            }

            // 规则检查: 无效置顶的帖子
            if (restThreadChannel.Flags.HasFlag(ChannelFlags.Pinned))
            {
                _loggerForScan.Info("📌 帖子已置顶, 视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                result.Status = ProcessingStatus.Invalid;
            }

            RestMessage? firstMessage = null;
            RestMessage? lastMessage = null;
            List<string>? imageAttachment = null;
            int reactionCount = 0;

            // 获取帖子的首楼消息和最新消息
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    // 控制速率
                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);

                    // 获取首楼消息（帖子ID等于首楼消息ID）
                    firstMessage = await restThreadChannel.GetMessageAsync(restThreadChannel.Id, _requestOptions);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取帖子的首楼消息和最新消息失败");
                    throw;
                }
            });
            
            // 获取帖子的首楼消息和最新消息
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    // 控制速率
                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);

                    // 获取最新消息
                    // SelectManyAsync 会把 IAsyncEnumerable<批次> 转换成 IAsyncEnumerable<单个消息>
                    lastMessage = await restThreadChannel.GetMessagesAsync(1, _requestOptions)
                                               .SelectMany(batch => batch.ToAsyncEnumerable()) // 压平操作
                                               .FirstOrDefaultAsync(); // 获取第一个元素
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取帖子的首楼消息和最新消息失败");
                    throw;
                }
            });

            if (firstMessage is null || string.IsNullOrWhiteSpace(firstMessage.Content))
            {
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    try
                    {
                        _loggerForScan.Warning("⚠️ 帖子没有有效首楼消息: {ThreadName} (ID: {ThreadId}), 尝试备用方法",
                        restThreadChannel.Name, restThreadChannel.Id);
                        {
                            // 控制速率
                            using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                            var restChannel = await _discordSocketClient.Rest.GetChannelAsync(restThreadChannel.Id);
                            var restTextChannel = (RestTextChannel)restChannel;
                            // 控制速率
                            using var tokenb = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                            firstMessage = await restTextChannel.GetMessageAsync(restThreadChannel.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "❌ 获取帖子的首楼消息和最新消息失败");
                        throw;
                    }
                });
            }

            // 验证首楼消息是否有效
            if (firstMessage is not null)
            {
                // 提取首楼消息中的图片附件
                imageAttachment = firstMessage.Attachments
                    .Where(a => IsImageAttachment(a))
                    .Select(a => a.Url)
                    .ToList();
                // 外链图片以embed形式存在
                imageAttachment.AddRange(firstMessage.Embeds
                    .Where(embed => embed.Thumbnail is not null && embed.Type == EmbedType.Image || embed.Type == EmbedType.Gifv)
                    .Select(embed => embed.Thumbnail!.Value.Url)
                    .ToList()
                );
                // 获取首楼消息的反应总数
                reactionCount = firstMessage.Reactions.Values
                    .Select(r => r.ReactionCount)
                    .Sum();
            }
            
            // 逻辑更改: 没有首楼消息仍然视为有效, 但注释掉以暂留代码, 或许需求还会改回去
            // else
            // {
            //     _logger.Warning("⚠️ 帖子没有有效首楼消息: {ThreadName} (ID: {ThreadId})",
            //         restThreadChannel.Name, restThreadChannel.Id);
            //     _logger.Debug($"https://discord.com/channels/{restThreadChannel.GuildId}/{restThreadChannel.Id}/0");
            //     isValid = false;
            //     result.Status = ProcessingStatus.Invalid;
            // }

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

                // 如果帖子有应用标签, 但没有有效标签, 记录警告
                if (restThreadChannel.AppliedTags.Any() && !validTagIds.Any())
                {
                    _logger.Warning("⚠️ 帖子 {ThreadName} (ID: {ThreadId}) 应用的所有标签都已不存在于论坛中",
                        restThreadChannel.Name, restThreadChannel.Id);
                }
            }

            if (!_configLoader.ValidChannelSet.Contains(restThreadChannel.CategoryId ?? 0))
            {
                _logger.Debug("⚠️ 帖子不在作品区内, 视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                _logger.Debug("_configLoader.ChannelSet: {ChannelSet}", _configLoader.ValidChannelSet);
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
                ReactionCount = reactionCount,
                ReplyCount = restThreadChannel.MessageCount,
                LastActiveAt = lastMessage is not null ? lastMessage.CreatedAt : restThreadChannel.CreatedAt,
                JumpUrl = $"https://discord.com/channels/{restThreadChannel.GuildId}/{restThreadChannel.Id}/0",
                AttachmentUrls = imageAttachment,

                Title = restThreadChannel.Name,
                FirstMessageContent = firstMessage?.Content,

                TitleTokenized = JiebaTokenizerService.CutForSearch(restThreadChannel.Name),
                ContentTokenized = firstMessage is not null ? JiebaTokenizerService.CutForSearch(firstMessage.Content) : null,

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

    private ProcessingResult GetTextThreadData(RestThreadChannel restThreadChannel)
    {
        try
        {
            var isValid = false;
            var isLocked = false;
            ProcessingResult result = new();

            if (restThreadChannel.IsLocked)
            {
                _logger.Debug("🔒 子区已锁定, 视为无效: {ThreadName} (ID: {ThreadId})",
                    restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                isLocked = true;
                result.Status = ProcessingStatus.Invalid;
            }

            // 子区直接设首条消息为null, 子区: 创建子区的消息 = Title = 首楼
            RestMessage? firstMessage = null;
            RestMessage? lastMessage = null;
            List<string>? imageAttachment = null;
            
            if (!_socketTextChannelsCache.TryGetValue(restThreadChannel.ParentChannelId, out var parentChannel))
            {
                // 如果因为某些极其罕见的原因找不到父频道
                _logger.Error("❌ 数据不一致错误！无法找到父频道缓存: 帖子 {ThreadName} (ID: {ThreadId}), 父频道ID {ParentChannelId}",
                    restThreadChannel.Name, restThreadChannel.Id, restThreadChannel.ParentChannelId);
                isValid = false;
                result.Status = ProcessingStatus.Failed;
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
                ReactionCount = 0,
                ReplyCount = restThreadChannel.MessageCount,
                LastActiveAt = lastMessage is not null ? lastMessage.CreatedAt : restThreadChannel.CreatedAt,
                JumpUrl = $"https://discord.com/channels/{restThreadChannel.GuildId}/{restThreadChannel.Id}/0",
                AttachmentUrls = imageAttachment,

                Title = restThreadChannel.Name,
                FirstMessageContent = firstMessage?.Content,

                TitleTokenized = JiebaTokenizerService.CutForSearch(restThreadChannel.Name),
                ContentTokenized = firstMessage is not null ? JiebaTokenizerService.CutForSearch(firstMessage.Content) : null,

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
    /// <returns>如果是图片类型返回 true, 否则返回 false</returns>
    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".gifv", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
}