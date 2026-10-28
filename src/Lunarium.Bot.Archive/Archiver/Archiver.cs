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
/// 一轮归档的编排：抓取活跃线程 → 按活跃度归档超出上限的部分 → 状态报告。
/// </summary>
public sealed class Archiver
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly NotifierManager _notifier;
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    public Archiver(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        DiscordSocketClient client,
        RateLimitedExecutor rateLimitedExecutor,
        NotifierManager notifier)
    {
        _logger = logger.ForContext("Archiver");
        _configLoader = configLoader;
        _client = client;
        _rateLimitedExecutor = rateLimitedExecutor;
        _notifier = notifier;
        _httpRetryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(logger);
    }

    public async Task Archive(CancellationToken cancellationToken)
    {
        _logger.Info("====== 定时归档任务开始 ======");

        try
        {
            await ProcessAllGuildsArchiveTask(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _logger.Info("====== 定时归档任务结束 ======");
        }
    }

    private async Task ProcessAllGuildsArchiveTask(CancellationToken cancellationToken)
    {
        await _notifier.InitializeStatusChannelIfNeededAsync(cancellationToken).ConfigureAwait(false);

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

            var context = new GuildContext(guild, guildConfig);

            _logger.Info("开始处理服务器 {GuildName}({GuildId}) (活跃线程上限: {Limit})",
                guild.Name, guildId, guildConfig.ArchiveLimit);

            try
            {
                await ProcessSingleGuildArchive(context, taskStartTime, cancellationToken).ConfigureAwait(false);
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
        var active = await GetEligibleChannelsFromGuild(context, cancellationToken).ConfigureAwait(false);
        _logger.Info("[{GuildName}({GuildId})] 步骤1/4: 获取到 {ThreadCount} 个活跃线程",
            context.Guild.Name, context.Guild.Id, active.Items.Count);

        ArchiveOutcome outcome;

        if (active.Items.Count > context.ActiveLimit)
        {
            // 2. 仅在超限时逐帖取最后活跃时间（未超限的周期不产生任何额外请求）
            _logger.Info("[{GuildName}({GuildId})] 步骤2/4: 活跃线程 {ActiveCount} 超过上限 {Limit}，开始获取最后活跃时间",
                context.Guild.Name, context.Guild.Id, active.Items.Count, context.ActiveLimit);

            var timed = await GetThreadsWithLastActivityTime(context, active.Items, cancellationToken)
                .ConfigureAwait(false);

            var unreadable = timed.Count(thread => thread.Unreadable);
            if (unreadable > 0)
            {
                _logger.Warning("[{GuildName}({GuildId})] 步骤2/4: {Count} 个线程拿不到最后活跃时间，本轮按最活跃处理（不会被归档）",
                    context.Guild.Name, context.Guild.Id, unreadable);
            }

            // 3. 归档最不活跃的部分
            _logger.Info("[{GuildName}({GuildId})] 步骤3/4: 开始归档超限线程（计划归档 {Planned} 个）",
                context.Guild.Name, context.Guild.Id, active.Items.Count - context.ActiveLimit);

            outcome = await ArchiveExcessiveThreads(context, timed, active.Items.Count, unreadable, cancellationToken)
                .ConfigureAwait(false);

            _logger.Info("[{GuildName}({GuildId})] 步骤3/4: 归档执行完成，共处理 {Count} 个线程",
                context.Guild.Name, context.Guild.Id, outcome.Items.Count);
        }
        else
        {
            outcome = new ArchiveOutcome(
                TotalActiveThreads: active.Items.Count,
                Items: Array.Empty<ArchiveItemResult>(),
                HasExcessThreads: false);

            _logger.Info("[{GuildName}({GuildId})] 步骤2/4: 活跃线程 {ActiveCount} 未超过上限 {Limit}，跳过最后活跃时间获取",
                context.Guild.Name, context.Guild.Id, active.Items.Count, context.ActiveLimit);

            _logger.Info("[{GuildName}({GuildId})] 步骤3/4: 无超限线程，跳过归档",
                context.Guild.Name, context.Guild.Id);
        }

        // 4. 状态报告
        _logger.Info("[{GuildName}({GuildId})] 步骤4/4: 更新归档状态报告",
            context.Guild.Name, context.Guild.Id);

        await _notifier.UpdateArchiveStatusReportAsync(
            context,
            outcome,
            DateTimeOffset.UtcNow - taskStartTime,
            cancellationToken).ConfigureAwait(false);
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
                        var all = await _client.Rest.GetAllActiveThreadsAsync(context.Guild).ConfigureAwait(false);
                        return [.. all];
                    },
                    token),
                cancellationToken).ConfigureAwait(false);

            return new ActiveThreads(threads);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[{GuildName}({GuildId})] 步骤1/4: 获取服务器活跃线程失败",
                context.Guild.Name, context.Guild.Id);
            throw;
        }
    }

    private async Task<IReadOnlyList<TimedThread>> GetThreadsWithLastActivityTime(
        GuildContext context,
        IReadOnlyList<RestThreadChannel> threads,
        CancellationToken cancellationToken)
    {
        var count = 0;
        var tasks = threads.Select(async thread =>
        {
            var current = Interlocked.Increment(ref count);
            try
            {
                _logger.Debug("[{GuildName}({GuildId})] 步骤2/4: 获取第 {Current}/{Total} 个线程的最后活跃时间",
                    context.Guild.Name, context.Guild.Id, current, threads.Count);

                var lastMessageTime = await _httpRetryPolicy.ExecuteAsync(
                    token => _rateLimitedExecutor.WriteAndWaitAsync<DateTimeOffset?>(
                        async _ =>
                        {
                            var recentMessages = await thread.GetMessagesAsync(1).FlattenAsync().ConfigureAwait(false);
                            var lastMessage = recentMessages.FirstOrDefault();
                            // 读不到最后消息时返回 null：由调用侧按“最活跃”处理，本轮不参与归档
                            return lastMessage?.CreatedAt;
                        },
                        token),
                    cancellationToken).ConfigureAwait(false);

                return lastMessageTime is { } lastActivity
                    ? new TimedThread(thread, lastActivity)
                    : new TimedThread(thread, DateTimeOffset.MaxValue, Unreadable: true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{GuildName}({GuildId})] 步骤2/4: 获取线程 {ThreadId} 最后消息时间失败",
                    context.Guild.Name, context.Guild.Id, thread.Id);
                return new TimedThread(thread, DateTimeOffset.MaxValue, Unreadable: true);
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ArchiveOutcome> ArchiveExcessiveThreads(
        GuildContext context,
        IReadOnlyList<TimedThread> threads,
        int totalActives,
        int unreadableCount,
        CancellationToken cancellationToken)
    {
        var plannedCount = Math.Max(0, totalActives - context.ActiveLimit);

        var candidates = threads
            .OrderByDescending(thread => thread.LastMessageAt)
            .ThenByDescending(thread => thread.Thread.Id)
            .ToList();

        var lastThreads = candidates
            .TakeLast(Math.Min(plannedCount, candidates.Count))
            .ToList();

        if (lastThreads.Count == 0)
        {
            _logger.Warning("[{GuildName}({GuildId})] 步骤3/4: 本轮没有可归档候选（计划 {Planned}）",
                context.Guild.Name, context.Guild.Id, plannedCount);

            return new ArchiveOutcome(
                TotalActiveThreads: totalActives,
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
            _logger.Error("[{GuildName}({GuildId})] 步骤3/4: 本轮计划归档 {Count} 个线程，超过安全围栏 {Safety}，已跳过",
                context.Guild.Name, context.Guild.Id, lastThreads.Count, safetyLimit);

            return new ArchiveOutcome(
                TotalActiveThreads: totalActives,
                Items: Array.Empty<ArchiveItemResult>(),
                HasExcessThreads: true,
                PlannedCount: plannedCount,
                UnreadableCount: unreadableCount);
        }

        _logger.Info("[{GuildName}({GuildId})] 步骤3/4: 计划归档 {Planned} 个，实际候选 {Actual} 个",
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
                            }).ConfigureAwait(false);
                        },
                        token),
                    cancellationToken).ConfigureAwait(false);

                _logger.Info("[{GuildName}({GuildId})] 步骤3/4: -> 成功归档线程 {ThreadId} (最后活跃于: {LastActiveAt})",
                    context.Guild.Name, context.Guild.Id, thread.Thread.Id, thread.LastMessageAt);

                return new ArchiveItemResult(thread.Thread, thread.LastMessageAt, true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{GuildName}({GuildId})] 步骤3/4: 归档线程 {ThreadId} 时发生异常",
                    context.Guild.Name, context.Guild.Id, thread.Thread.Id);
                return new ArchiveItemResult(thread.Thread, thread.LastMessageAt, false, ex);
            }
        });

        var results = await Task.WhenAll(archiveTasks).ConfigureAwait(false);

        return new ArchiveOutcome(
            TotalActiveThreads: totalActives,
            Items: results,
            HasExcessThreads: true,
            PlannedCount: plannedCount,
            UnreadableCount: unreadableCount);
    }
}
