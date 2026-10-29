using System.Collections.Concurrent;
using System.Net;

using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Archive.Notifier;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.RateLimiting;
using Lunarium.Common.Caching;
using Lunarium.Logging;

using Polly.Retry;

namespace Lunarium.Bot.Archive.Archiver;

/// <summary>
/// 置顶监控：每轮归档扫描时对比"上一轮跟踪的置顶帖"与"本轮实际的置顶帖"，掉置顶 / 换置顶时发告警卡片。
/// <para>
/// 人工点击按钮优先；12 小时内无人处理则到期自动处理：
/// <see cref="PinnedAlertKind.Lost"/> 自动恢复置顶，<see cref="PinnedAlertKind.Updated"/> 自动保持新帖。
/// </para>
/// <para>
/// 只靠扫描（不订阅 <c>ThreadUpdated</c>，也不额外起定时器）；跟踪状态与待处理告警都落盘，重启后继续生效。
/// </para>
/// </summary>
public sealed class PinnedMonitoring : IAsyncDisposable
{
    /// <summary>告警等待人工处理的时长。</summary>
    private static readonly TimeSpan AlertLifetime = TimeSpan.FromHours(12);

    /// <summary>恢复置顶连续失败多少次后放弃重试。</summary>
    private const int MaxRestoreAttempts = 3;

    /// <summary>恢复失败后下一次重试的最短间隔（复用到期时间作为重试计时）。</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);

    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly NotifierManager _notifierManager;
    private readonly DiscordSocketClient _client;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly JsonDocumentStore<PinnedMonitoringDocument> _store;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<GuildKey, Dictionary<PinnedChannel, PinnedThread>> _track = new();
    private readonly ConcurrentDictionary<PinnedAlertKey, PinnedAlert> _alerts = new();

    public PinnedMonitoring(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        NotifierManager notifierManager,
        DiscordSocketClient client,
        RateLimitedExecutor rateLimitedExecutor,
        JsonDocumentStore<PinnedMonitoringDocument> store)
    {
        _logger = logger.ForContext("PinnedMonitoring");
        _configLoader = configLoader;
        _notifierManager = notifierManager;
        _client = client;
        _rateLimitedExecutor = rateLimitedExecutor;
        _store = store;
        _httpRetryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(logger);

        var document = _store.Read();

        foreach (var guild in document.Guilds)
        {
            var channels = new Dictionary<PinnedChannel, PinnedThread>();
            foreach (var channel in guild.Channels)
            {
                channels[new PinnedChannel(channel.ChannelId)] = new PinnedThread(channel.ThreadId);
            }

            _track[new GuildKey(guild.GuildId)] = channels;
        }

        foreach (var alert in document.Alerts)
        {
            _alerts[alert.Key] = alert;
        }

        _logger.Info("置顶监控已加载: 跟踪 {GuildCount} 个服务器, 待处理告警 {AlertCount} 条",
            _track.Count, _alerts.Values.Count(x => x.IsPending));
    }

    #region 扫描

    /// <summary>
    /// 每轮归档调用：先处理到期告警，再与上一轮跟踪状态比对并落盘。
    /// </summary>
    /// <param name="guildId">服务器 Id。</param>
    /// <param name="monitoredChannelIds">需要监控的父频道白名单；空集合表示沿用上一轮跟踪范围。</param>
    /// <param name="pinnedThreads">本轮抓到的置顶帖（调用方已按白名单粗筛）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PinnedReconcileResult> ReconcileAsync(
        ulong guildId,
        IReadOnlySet<ulong> monitoredChannelIds,
        IReadOnlyList<RestThreadChannel> pinnedThreads,
        CancellationToken cancellationToken)
    {
        try
        {
            // 到期检查与扫描同源：粒度 = 归档间隔，不额外起定时器
            await ProcessExpiredAsync(cancellationToken).ConfigureAwait(false);

            var guildBucket = new GuildKey(guildId);

            // 一个父频道只能有一个置顶帖：出现重复时以最后一条为准
            var newTrack = new Dictionary<PinnedChannel, PinnedThread>();
            foreach (var thread in pinnedThreads)
            {
                if (monitoredChannelIds.Count > 0 && !monitoredChannelIds.Contains(thread.ParentChannelId))
                {
                    continue;
                }

                newTrack[new PinnedChannel(thread.ParentChannelId)] = new PinnedThread(thread.Id);
            }

            _track.TryGetValue(guildBucket, out var oldTrack);

            var results = new List<PinnedChange>();

            foreach (var (channel, thread) in newTrack)
            {
                if (oldTrack is null || !oldTrack.TryGetValue(channel, out var previous))
                {
                    results.Add(new PinnedChange(thread.Id, channel.Id, PinnedChangeKind.New));
                }
                else if (previous.Id != thread.Id)
                {
                    results.Add(new PinnedChange(thread.Id, channel.Id, PinnedChangeKind.Updated, previous.Id));
                }
                else
                {
                    results.Add(new PinnedChange(thread.Id, channel.Id, PinnedChangeKind.Unchanged));
                }
            }

            if (oldTrack is not null)
            {
                foreach (var (channel, previous) in oldTrack)
                {
                    if (newTrack.ContainsKey(channel))
                    {
                        continue;
                    }

                    // 频道已移出监控范围：不算丢失，也不再跟踪
                    if (monitoredChannelIds.Count > 0 && !monitoredChannelIds.Contains(channel.Id))
                    {
                        continue;
                    }

                    results.Add(new PinnedChange(previous.Id, channel.Id, PinnedChangeKind.Lost));
                }
            }

            _track[guildBucket] = newTrack;
            Save();

            return new PinnedReconcileResult(results);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "置顶扫描失败: GuildId={GuildId}", guildId);

            try
            {
                await _notifierManager
                    .ToNotifierChannel(NotifierComponent.PinnedScanFailed(ex: ex), guildId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception notifierEx)
            {
                _logger.Error(notifierEx, "上报置顶扫描失败时再次失败: GuildId={GuildId}", guildId);
            }

            // 置顶监控是次级失败：不 throw，不能打断归档
            return new PinnedReconcileResult([], Failed: true);
        }
    }

    /// <summary>按本轮扫描结果发通知：新置顶合并成一条；掉置顶 / 换置顶各发一条带按钮的告警卡片。</summary>
    public async Task NotifyChangesAsync(ulong guildId, PinnedReconcileResult result, CancellationToken cancellationToken)
    {
        if (result.Failed || result.Items.Count == 0 || !result.HasChange)
        {
            return;
        }

        var newPinneds = new List<PinnedChange>();

        foreach (var item in result.Items)
        {
            // 频道重新有了置顶帖：之前那条掉置顶告警已经过时，直接收尾
            if (item.Kind is PinnedChangeKind.New or PinnedChangeKind.Updated)
            {
                await ClosePendingAlertAsync(guildId, item.ChannelId, "检测到该频道已重新置顶，自动收尾", cancellationToken)
                    .ConfigureAwait(false);
            }

            switch (item.Kind)
            {
                case PinnedChangeKind.New:
                    newPinneds.Add(item);
                    break;
                case PinnedChangeKind.Lost:
                    await RaiseAlertAsync(guildId, PinnedAlertKind.Lost, item, cancellationToken).ConfigureAwait(false);
                    break;
                case PinnedChangeKind.Updated:
                    await RaiseAlertAsync(guildId, PinnedAlertKind.Updated, item, cancellationToken).ConfigureAwait(false);
                    break;
                case PinnedChangeKind.Unchanged:
                    break;
            }
        }

        if (newPinneds.Count > 0)
        {
            // 全是新增 = 首次扫描（没有上一轮可比），单独给个说法
            var reason = newPinneds.Count == result.Items.Count && result.Items.Count > 0
                ? "本轮置顶帖为首次扫描"
                : "此轮扫描新增跟踪置顶帖";

            await _notifierManager
                .ToNotifierChannel(NotifierComponent.PinnedAdded(newPinneds, reason), guildId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    #endregion

    #region 告警处理

    /// <summary>处理已到期且仍待处理的告警，返回处理条数。</summary>
    public async Task<int> ProcessExpiredAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var processed = 0;

        foreach (var alert in _alerts.Values.ToList())
        {
            lock (_gate)
            {
                if (!alert.IsPending || alert.ExpireAt > now)
                {
                    continue;
                }

                alert.Decision = PinnedDecision.TimedOut;
                alert.ActorId = 0;
            }

            _logger.Info("置顶告警到期自动处理: GuildId={GuildId}, ChannelId={ChannelId}, Kind={Kind}",
                alert.GuildId, alert.ChannelId, alert.Kind);

            await ExecuteDecisionAsync(alert, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    /// <summary>处理告警卡片上的按钮。</summary>
    public async Task<PinnedDecisionResult> ApplyDecisionAsync(
        ulong guildId,
        ulong channelId,
        PinnedDecision decision,
        ulong actorId,
        CancellationToken cancellationToken)
    {
        if (!_alerts.TryGetValue(new PinnedAlertKey(guildId, channelId), out var alert))
        {
            return PinnedDecisionResult.Fail("该提醒已失效或已处理");
        }

        lock (_gate)
        {
            if (!alert.IsPending)
            {
                return PinnedDecisionResult.Fail("该提醒已被处理");
            }

            alert.Decision = decision;
            alert.ActorId = actorId;
        }

        return await ExecuteDecisionAsync(alert, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PinnedDecisionResult> ExecuteDecisionAsync(PinnedAlert alert, CancellationToken cancellationToken)
    {
        // 恢复置顶：人工点「恢复」，或到期自动处理且类型是 Lost
        var restore = alert.Decision == PinnedDecision.Restored
                      || (alert.Decision == PinnedDecision.TimedOut && alert.Kind == PinnedAlertKind.Lost);

        string detail;

        if (!restore)
        {
            detail = alert.Decision == PinnedDecision.TimedOut
                ? "到期未处理，已保持当前状态"
                : "已忽略该提醒";

            // 放弃掉置顶：从跟踪里移除，下一轮不再报 Lost（该帖会作为普通线程参与归档）
            if (alert.Kind == PinnedAlertKind.Lost)
            {
                UntrackChannel(alert.GuildId, alert.ChannelId);
            }
        }
        else
        {
            var restoreResult = await RestorePinnedAsync(alert, cancellationToken).ConfigureAwait(false);
            if (!restoreResult.Success)
            {
                alert.Attempts++;

                if (alert.Attempts < MaxRestoreAttempts)
                {
                    alert.Decision = PinnedDecision.Pending;   // 回滚，下一轮重试
                    alert.ExpireAt = DateTimeOffset.UtcNow + RetryDelay;   // 别在同一轮的多个服务器里连着重试
                    Save();
                    _logger.Warning("恢复置顶失败，保留告警重试: GuildId={GuildId}, ChannelId={ChannelId}, Attempts={Attempts}",
                        alert.GuildId, alert.ChannelId, alert.Attempts);
                    return PinnedDecisionResult.Fail($"{restoreResult.Message}，将在下一轮重试");
                }

                detail = $"{restoreResult.Message}（已连续失败 {alert.Attempts} 次，停止重试）";

                // 放弃恢复：从跟踪里移除，否则下一轮会重新报一次 Lost
                if (alert.Kind == PinnedAlertKind.Lost)
                {
                    UntrackChannel(alert.GuildId, alert.ChannelId);
                }
            }
            else
            {
                detail = restoreResult.Message;
                TrackChannel(alert.GuildId, alert.ChannelId, alert.RestoreTargetId);
            }
        }

        await FinalizeAlertAsync(alert, detail, cancellationToken).ConfigureAwait(false);
        return PinnedDecisionResult.Ok(detail);
    }

    private async Task<PinnedDecisionResult> RestorePinnedAsync(PinnedAlert alert, CancellationToken cancellationToken)
    {
        var targetId = alert.RestoreTargetId;

        try
        {
            if (await GetThreadChannelAsync(targetId, cancellationToken).ConfigureAwait(false) is not IThreadChannel thread)
            {
                return PinnedDecisionResult.Fail("目标帖子已不存在，无法恢复");
            }

            await _httpRetryPolicy.ExecuteAsync(
                token => _rateLimitedExecutor.WriteAndWaitAsync(
                    async _ =>
                    {
                        await thread.ModifyAsync(properties =>
                        {
                            properties.Archived = false;
                            properties.Flags = thread.Flags | ChannelFlags.Pinned;
                        }).ConfigureAwait(false);
                    },
                    token),
                cancellationToken).ConfigureAwait(false);

            // 换置顶场景：旧帖已恢复，把新帖的置顶撤掉
            if (alert.Kind == PinnedAlertKind.Updated
                && alert.ThreadId != 0
                && alert.ThreadId != targetId)
            {
                await TryUnpinAsync(alert.ThreadId, cancellationToken).ConfigureAwait(false);
            }

            _logger.Info("已恢复置顶: GuildId={GuildId}, ChannelId={ChannelId}, ThreadId={ThreadId}",
                alert.GuildId, alert.ChannelId, targetId);

            return PinnedDecisionResult.Ok(alert.Kind == PinnedAlertKind.Updated ? "已恢复旧置顶帖" : "已恢复置顶");
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            _logger.Error(ex, "恢复置顶被拒绝(403)，Bot 可能缺少「管理线程 / 管理帖子」权限: GuildId={GuildId}, ChannelId={ChannelId}, ThreadId={ThreadId}",
                alert.GuildId, alert.ChannelId, targetId);
            return PinnedDecisionResult.Fail("恢复置顶被 Discord 拒绝：Bot 缺少「管理线程」权限");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "恢复置顶失败: GuildId={GuildId}, ChannelId={ChannelId}, ThreadId={ThreadId}",
                alert.GuildId, alert.ChannelId, targetId);
            return PinnedDecisionResult.Fail("恢复置顶失败");
        }
    }

    private async Task TryUnpinAsync(ulong threadId, CancellationToken cancellationToken)
    {
        try
        {
            if (await GetThreadChannelAsync(threadId, cancellationToken).ConfigureAwait(false) is not IThreadChannel thread)
            {
                return;
            }

            await _httpRetryPolicy.ExecuteAsync(
                token => _rateLimitedExecutor.WriteAndWaitAsync(
                    async _ =>
                    {
                        await thread.ModifyAsync(properties => properties.Flags = thread.Flags & ~ChannelFlags.Pinned)
                            .ConfigureAwait(false);
                    },
                    token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "撤销新置顶帖失败: ThreadId={ThreadId}", threadId);
        }
    }

    /// <summary>取线程频道（缓存优先 → REST 回退），与归档链路使用同一套限流与 HTTP 重试。</summary>
    private async Task<IThreadChannel?> GetThreadChannelAsync(ulong threadId, CancellationToken cancellationToken)
    {
        var channel = await _httpRetryPolicy.ExecuteAsync(
            token => _rateLimitedExecutor.WriteAndWaitAsync<IChannel?>(
                async _ => await _client.GetChannelAsync(threadId),
                token),
            cancellationToken).ConfigureAwait(false);

        return channel as IThreadChannel;
    }

    private async Task RaiseAlertAsync(
        ulong guildId,
        PinnedAlertKind kind,
        PinnedChange status,
        CancellationToken cancellationToken)
    {
        var key = new PinnedAlertKey(guildId, status.ChannelId);
        if (_alerts.TryGetValue(key, out var existing) && existing.IsPending)
        {
            _logger.Info("已存在待处理的置顶告警，跳过重复创建: GuildId={GuildId}, ChannelId={ChannelId}",
                guildId, status.ChannelId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var alert = new PinnedAlert
        {
            GuildId = guildId,
            ChannelId = status.ChannelId,
            ThreadId = status.ThreadId,
            PreviousThreadId = status.PreviousThreadId,
            Kind = kind,
            NotifierChannelId = GetNotifierChannelId(guildId),
            DetectedAt = now,
            ExpireAt = now + AlertLifetime,
            Decision = PinnedDecision.Pending
        };

        _alerts[key] = alert;
        Save();

        var card = kind == PinnedAlertKind.Updated
            ? NotifierComponent.PinnedUpdated(guildId, alert.ChannelId, alert.PreviousThreadId, alert.ThreadId, alert.ExpireAt)
            : NotifierComponent.PinnedLost(guildId, alert.ChannelId, alert.ThreadId, alert.ExpireAt);

        var message = await _notifierManager.ToNotifierChannel(card, guildId, cancellationToken).ConfigureAwait(false);
        if (message is not null)
        {
            alert.MessageId = message.Id;
            Save();
        }

        _logger.Warning("置顶告警: GuildId={GuildId}, ChannelId={ChannelId}, ThreadId={ThreadId}, Previous={Previous}, Kind={Kind}, ExpireAt={ExpireAt}",
            guildId, alert.ChannelId, alert.ThreadId, alert.PreviousThreadId, kind, alert.ExpireAt);
    }

    private async Task ClosePendingAlertAsync(
        ulong guildId,
        ulong channelId,
        string detail,
        CancellationToken cancellationToken)
    {
        if (!_alerts.TryGetValue(new PinnedAlertKey(guildId, channelId), out var alert))
        {
            return;
        }

        lock (_gate)
        {
            if (!alert.IsPending)
            {
                return;
            }

            alert.Decision = PinnedDecision.Restored;
            alert.ActorId = 0;
        }

        _logger.Info("置顶告警已自动收尾: GuildId={GuildId}, ChannelId={ChannelId}", guildId, channelId);
        await FinalizeAlertAsync(alert, detail, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeAlertAsync(PinnedAlert alert, string detail, CancellationToken cancellationToken)
    {
        if (alert.MessageId != 0 && alert.NotifierChannelId != 0)
        {
            var card = NotifierComponent.PinnedAlertClosed(alert, detail);
            await _notifierManager
                .TryEditAsync(alert.NotifierChannelId, alert.MessageId, card, cancellationToken)
                .ConfigureAwait(false);
        }

        // 终态即删除记录：留着会让卡片永远停在可点击状态
        _alerts.TryRemove(alert.Key, out _);
        Save();
    }

    #endregion

    #region 跟踪与持久化

    private ulong GetNotifierChannelId(ulong guildId)
        => _configLoader.Value.DeployGuildConfig.TryGetValue(guildId, out var config)
            ? config.NotifierChannelId
            : 0;

    private void UntrackChannel(ulong guildId, ulong channelId)
    {
        if (!_track.TryGetValue(new GuildKey(guildId), out var channels))
        {
            return;
        }

        lock (_gate)
        {
            channels.Remove(new PinnedChannel(channelId));
        }
    }

    private void TrackChannel(ulong guildId, ulong channelId, ulong threadId)
    {
        var channels = _track.GetOrAdd(new GuildKey(guildId), _ => new Dictionary<PinnedChannel, PinnedThread>());

        lock (_gate)
        {
            channels[new PinnedChannel(channelId)] = new PinnedThread(threadId);
        }
    }

    private void Save()
    {
        try
        {
            PinnedMonitoringDocument document;

            lock (_gate)
            {
                document = new PinnedMonitoringDocument
                {
                    Guilds = _track.Select(pair => new PinnedGuildEntry
                    {
                        GuildId = pair.Key.Id,
                        Channels = pair.Value.Select(channel => new PinnedChannelEntry
                        {
                            ChannelId = channel.Key.Id,
                            ThreadId = channel.Value.Id
                        }).ToList()
                    }).ToList(),
                    Alerts = _alerts.Values.ToList()
                };
            }

            _store.Set(document);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存置顶监控缓存失败");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Save();
        await _store.FlushAsync().ConfigureAwait(false);
    }

    #endregion
}