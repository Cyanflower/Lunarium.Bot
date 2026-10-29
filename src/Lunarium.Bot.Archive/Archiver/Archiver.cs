using Discord;
using Discord.Rest;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Archive.Notifier;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.OpenAPI;
using Lunarium.Bot.Framework.RateLimiting;
using Lunarium.Logging;

using Polly.Retry;

namespace Lunarium.Bot.Archive.Archiver;

/// <summary>
/// 一轮归档的编排：抓取活跃线程 → 白名单分类 → 置顶扫描 → 按活跃度归档 → 状态报告。
/// </summary>
public sealed class Archiver
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly NotifierManager _notifier;
    private readonly PinnedMonitoring _pinnedMonitoring;
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    public Archiver(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        DiscordSocketClient client,
        RateLimitedExecutor rateLimitedExecutor,
        NotifierManager notifier,
        PinnedMonitoring pinnedMonitoring)
    {
        _logger = logger.ForContext("Archiver");
        _configLoader = configLoader;
        _client = client;
        _rateLimitedExecutor = rateLimitedExecutor;
        _notifier = notifier;
        _pinnedMonitoring = pinnedMonitoring;
        _httpRetryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(logger);
    }

    public async Task Archive(CancellationToken cancellationToken)
    {
        _logger.Info("====== 定时归档任务开始 ======");

        try
        {
            await ProcessAllGuildsArchiveTask(cancellationToken);
        }
        finally
        {
            _logger.Info("====== 定时归档任务结束 ======");
        }
    }

    private async Task ProcessAllGuildsArchiveTask(CancellationToken cancellationToken)
    {
        await _notifier.InitializeStatusChannelIfNeededAsync(cancellationToken);

        foreach (var (guildId, guildConfig) in _configLoader.Value.DeployGuildConfig)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var taskStartTime = DateTimeOffset.UtcNow;
            var guild = _client.GetGuild(guildId);
            if (guild is null)
            {
                _logger.Warning("处理服务器 {GuildId} 时未能获取到 SocketGuild 对象，该服务器跳过处理", guildId);
                continue;
            }

            var context = new GuildContext(
                guild,
                guildConfig,
                guildConfig.PinnedMonitoringChannelIds.ToHashSet());

            _logger.Info("开始处理服务器 {GuildName}({GuildId}) (活跃线程上限: {Limit})",
                guild.Name, guildId, guildConfig.ArchiveLimit);

            try
            {
                await ProcessSingleGuildArchive(context, taskStartTime, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理服务器 {GuildName}({GuildId}) 的归档任务时发生异常", guild.Name, guildId);
            }

            _logger.Info("服务器 {GuildName}({GuildId}) 处理完成，总耗时: {Duration:F2} 秒",
                guild.Name, guildId, (DateTimeOffset.UtcNow - taskStartTime).TotalSeconds);
        }
    }

    private async Task ProcessSingleGuildArchive(
        GuildContext context,
        DateTimeOffset taskStartTime,
        CancellationToken cancellationToken)
    {
        // 1. 获取服务器所有活跃线程
        var active = await GetEligibleChannelsFromGuild(context, cancellationToken);
        _logger.Info("[{GuildName}({GuildId})] 步骤1/6: 获取到 {ThreadCount} 个活跃线程",
            context.Guild.Name, context.Guild.Id, active.Items.Count);

        // 2. 按白名单分类置顶线程和普通线程
        var classified = ClassifyThreadsByPinnedStatus(context, active);
        _logger.Info("[{GuildName}({GuildId})] 步骤2/6: 分类完成 -> {PinnedCount} 个监控置顶帖, {RegularCount} 个普通线程",
            context.Guild.Name, context.Guild.Id, classified.Pinned.Count, classified.Regular.Count);

        // 3. 每轮都做置顶扫描；即使本轮不需要归档也要执行
        var reconcileResult = await _pinnedMonitoring.ReconcileAsync(
            context.Guild.Id,
            context.PinnedChannels,
            classified.Pinned,
            cancellationToken);

        if (reconcileResult.Failed)
        {
            _logger.Warning("[{GuildName}({GuildId})] 步骤3/6: 置顶扫描失败，本轮跳过置顶变更通知",
                context.Guild.Name, context.Guild.Id);
        }
        else
        {
            _logger.Info("[{GuildName}({GuildId})] 步骤3/6: 置顶扫描完成 -> 新增 {NewCount} 个, 丢失 {LostCount} 个, 变更 {UpdatedCount} 个, 未变动 {UnchangedCount} 个",
                context.Guild.Name, context.Guild.Id,
                reconcileResult.NewCount, reconcileResult.LostCount, reconcileResult.UpdatedCount, reconcileResult.UnchangedCount);

            await _pinnedMonitoring.NotifyChangesAsync(context.Guild.Id, reconcileResult, cancellationToken);
        }

        // 4. 置顶帖与归档彻底分流：置顶帖不进归档候选，只用来扣减普通线程的可用额度。
        //    可用额度 = 上限 - 置顶帖数（置顶帖本就占着 limit 里的名额），
        //    余量 = 1000 - limit 由 Discord 的活跃线程上限兜底。
        var regularBudget = Math.Max(0, context.ActiveLimit - classified.Pinned.Count);
        var plannedCount = Math.Max(0, classified.Regular.Count - regularBudget);

        ArchiveOutcome outcome;

        if (plannedCount > 0)
        {
            // 4. 仅在需要归档时逐帖取最后活跃时间（不需要归档的周期不产生任何额外请求）
            _logger.Info("[{GuildName}({GuildId})] 步骤4/6: 普通线程 {RegularCount} 超过可用额度 {Budget}（上限 {Limit} - 置顶 {Pinned}），开始获取最后活跃时间",
                context.Guild.Name, context.Guild.Id,
                classified.Regular.Count, regularBudget, context.ActiveLimit, classified.Pinned.Count);

            var timed = await GetThreadsWithLastActivityTime(context, classified, cancellationToken);

            var unreadable = timed.Count(thread => thread.Unreadable);
            if (unreadable > 0)
            {
                _logger.Warning("[{GuildName}({GuildId})] 步骤4/6: {Count} 个线程拿不到最后活跃时间，本轮按最活跃处理（不会被归档）",
                    context.Guild.Name, context.Guild.Id, unreadable);
            }

            // 5. 归档最不活跃的部分；候选只有普通线程，置顶帖完全不参与
            _logger.Info("[{GuildName}({GuildId})] 步骤5/6: 开始归档 {Planned} 个最不活跃的普通线程",
                context.Guild.Name, context.Guild.Id, plannedCount);

            outcome = await ArchiveExcessiveThreads(
                    context,
                    timed,
                    plannedCount,
                    active.Items.Count,
                    classified.Pinned.Count,
                    unreadable,
                    cancellationToken);

            _logger.Info("[{GuildName}({GuildId})] 步骤5/6: 归档执行完成，共处理 {Count} 个线程",
                context.Guild.Name, context.Guild.Id, outcome.Items.Count);
        }
        else
        {
            outcome = new ArchiveOutcome(
                TotalActiveThreads: active.Items.Count,
                PinnedCount: classified.Pinned.Count,
                Items: Array.Empty<ArchiveItemResult>(),
                HasExcessThreads: false);

            _logger.Info("[{GuildName}({GuildId})] 步骤4/6: 普通线程 {RegularCount} 未超过可用额度 {Budget}（上限 {Limit} - 置顶 {Pinned}），跳过最后活跃时间获取",
                context.Guild.Name, context.Guild.Id,
                classified.Regular.Count, regularBudget, context.ActiveLimit, classified.Pinned.Count);

            _logger.Info("[{GuildName}({GuildId})] 步骤5/6: 无超限线程，跳过归档",
                context.Guild.Name, context.Guild.Id);
        }

        // 6. 状态报告
        _logger.Info("[{GuildName}({GuildId})] 步骤6/6: 更新归档状态报告",
            context.Guild.Name, context.Guild.Id);

        await _notifier.UpdateArchiveStatusReportAsync(
            context,
            outcome,
            DateTimeOffset.UtcNow - taskStartTime,
            cancellationToken);
    }

    private async Task<ActiveThreads> GetEligibleChannelsFromGuild(
        GuildContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var threads = await _httpRetryPolicy.ExecuteAsync(
                token => _rateLimitedExecutor.WriteAndWaitAsync<List<RestThreadChannel>>(
                    async _ =>
                    {
                        var all = await _client.Rest.GetAllActiveThreadsAsync(context.Guild);
                        return [.. all];
                    },
                    token),
                cancellationToken);

            return new ActiveThreads(threads);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[{GuildName}({GuildId})] 步骤1/6: 获取服务器活跃线程失败",
                context.Guild.Name, context.Guild.Id);
            throw;
        }
    }

    private ClassifiedThreads ClassifyThreadsByPinnedStatus(
        GuildContext context,
        ActiveThreads activeThreads)
    {
        try
        {
            var pinnedThreads = new List<RestThreadChannel>();
            var regularThreads = new List<RestThreadChannel>();

            foreach (var thread in activeThreads.Items)
            {
                // 空列表 = 监控全部父频道（与 GuildConfig.PinnedMonitoringChannelIds 的语义一致）
                var isMonitored = context.PinnedChannels.Count is 0
                                  || context.PinnedChannels.Contains(thread.ParentChannelId);
                if (isMonitored && thread.Flags.HasFlag(ChannelFlags.Pinned))
                {
                    pinnedThreads.Add(thread);
                }
                else
                {
                    regularThreads.Add(thread);
                }
            }

            return new ClassifiedThreads(pinnedThreads, regularThreads);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[{GuildName}({GuildId})] 步骤2/6: 对服务器活跃线程分类失败",
                context.Guild.Name, context.Guild.Id);
            throw;
        }
    }

    private async Task<IReadOnlyList<TimedThread>> GetThreadsWithLastActivityTime(
        GuildContext context,
        ClassifiedThreads threads,
        CancellationToken cancellationToken)
    {
        var count = 0;
        var tasks = threads.Regular.Select(async thread =>
        {
            var current = Interlocked.Increment(ref count);
            try
            {
                _logger.Debug("[{GuildName}({GuildId})] 步骤4/6: 获取第 {Current}/{Total} 个普通线程的最后活跃时间",
                    context.Guild.Name, context.Guild.Id, current, threads.Regular.Count);

                var lastMessageTime = await _httpRetryPolicy.ExecuteAsync(
                    token => _rateLimitedExecutor.WriteAndWaitAsync<DateTimeOffset?>(
                        async _ =>
                        {
                            var recentMessages = await thread.GetMessagesAsync(1).FlattenAsync();
                            var lastMessage = recentMessages.FirstOrDefault();
                            // 读不到最后消息时返回 null：由调用侧按“最活跃”处理，本轮不参与归档
                            return lastMessage?.CreatedAt;
                        },
                        token),
                    cancellationToken);

                return lastMessageTime is { } lastActivity
                    ? new TimedThread(thread, lastActivity)
                    : new TimedThread(thread, DateTimeOffset.MaxValue, Unreadable: true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{GuildName}({GuildId})] 步骤4/6: 获取线程 {ThreadId} 最后消息时间失败",
                    context.Guild.Name, context.Guild.Id, thread.Id);
                return new TimedThread(thread, DateTimeOffset.MaxValue, Unreadable: true);
            }
        });

        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 归档最不活跃的普通线程。
    /// <para>
    /// <paramref name="threads"/> 只含普通线程（置顶帖在上游已被分流），
    /// <paramref name="plannedCount"/> 已扣掉置顶帖占用的额度，这里不再重复计算。
    /// </para>
    /// </summary>
    private async Task<ArchiveOutcome> ArchiveExcessiveThreads(
        GuildContext context,
        IReadOnlyList<TimedThread> threads,
        int plannedCount,
        int totalActiveThreads,
        int pinnedCount,
        int unreadableCount,
        CancellationToken cancellationToken)
    {
        var candidates = threads
            .OrderByDescending(thread => thread.LastMessageAt)
            .ThenByDescending(thread => thread.Thread.Id)
            .ToList();

        var lastThreads = candidates
            .TakeLast(Math.Min(plannedCount, candidates.Count))
            .ToList();

        if (lastThreads.Count == 0)
        {
            _logger.Warning("[{GuildName}({GuildId})] 步骤5/6: 本轮没有可归档候选（计划 {Planned}）",
                context.Guild.Name, context.Guild.Id, plannedCount);

            return new ArchiveOutcome(
                TotalActiveThreads: totalActiveThreads,
                PinnedCount: pinnedCount,
                Items: Array.Empty<ArchiveItemResult>(),
                HasExcessThreads: true,
                PlannedCount: plannedCount,
                UnreadableCount: unreadableCount);
        }

        // 安全围栏：1000 是 Discord 活跃线程上限；如果一次要归档的数量超过缓冲区余量太多，
        // 说明前面的计算或抓取结果异常，宁可跳过本轮。
        var safetyLimit = (int)((ArchiveConfigLimits.LimitExclusiveMax - ArchiveConfigLimits.LimitInclusiveMin) * 1.2);
        if (lastThreads.Count > safetyLimit)
        {
            _logger.Error("[{GuildName}({GuildId})] 步骤5/6: 本轮计划归档 {Count} 个线程，超过安全围栏 {Safety}，已跳过",
                context.Guild.Name, context.Guild.Id, lastThreads.Count, safetyLimit);

            return new ArchiveOutcome(
                TotalActiveThreads: totalActiveThreads,
                PinnedCount: pinnedCount,
                Items: Array.Empty<ArchiveItemResult>(),
                HasExcessThreads: true,
                PlannedCount: plannedCount,
                UnreadableCount: unreadableCount);
        }

        _logger.Info("[{GuildName}({GuildId})] 步骤5/6: 计划归档 {Planned} 个，实际候选 {Actual} 个",
            context.Guild.Name, context.Guild.Id, plannedCount, lastThreads.Count);

        var archiveTasks = lastThreads.Select(async thread =>
        {
            try
            {
                await _httpRetryPolicy.ExecuteAsync(
                    token => _rateLimitedExecutor.WriteAndWaitAsync(
                        async _ =>
                        {
                            await thread.Thread.ModifyAsync(properties =>
                            {
                                properties.Archived = true;
                            });
                        },
                        token),
                    cancellationToken);

                _logger.Info("[{GuildName}({GuildId})] 步骤5/6: -> 成功归档线程 {ThreadId} (最后活跃于: {LastActiveAt})",
                    context.Guild.Name, context.Guild.Id, thread.Thread.Id, thread.LastMessageAt);

                return new ArchiveItemResult(thread.Thread, thread.LastMessageAt, true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{GuildName}({GuildId})] 步骤5/6: 归档线程 {ThreadId} 时发生异常",
                    context.Guild.Name, context.Guild.Id, thread.Thread.Id);
                return new ArchiveItemResult(thread.Thread, thread.LastMessageAt, false, ex);
            }
        });

        var results = await Task.WhenAll(archiveTasks);

        return new ArchiveOutcome(
            TotalActiveThreads: totalActiveThreads,
            PinnedCount: pinnedCount,
            Items: results,
            HasExcessThreads: true,
            PlannedCount: plannedCount,
            UnreadableCount: unreadableCount);
    }
}
