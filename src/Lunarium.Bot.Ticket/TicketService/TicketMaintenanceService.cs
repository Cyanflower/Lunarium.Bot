using System.Net;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

using Microsoft.Extensions.Hosting;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>
/// 工单后台维护：
/// <list type="number">
///   <item>启动对账：已通过/已关闭但缺频道坐标的工单，按频道名认领；待审核但缺审核卡片的工单重发卡片。</item>
///   <item>定时扫描：把到点仍待审核的工单自动通过（复用 <see cref="TicketWorkflowService.ApproveAsync"/>）。</item>
///   <item>定时扫描：自动通过的工单长时间无管理回复时发送安抚（复用 <see cref="TicketSootheService"/>）。</item>
///   <item>定时扫描：执行到期的归档频道删除，删除前校验工单与频道身份。</item>
/// </list>
/// <para>
/// 本服务不在宿主 DI 里，由 <c>TicketBotService</c> 在首次就绪后手动 <c>StartAsync</c>，
/// 否则拿不到私有容器里的服务。
/// </para>
/// </summary>
public sealed class TicketMaintenanceService : BackgroundService
{
    /// <summary>扫描周期。删除队列需要秒级精度，自动通过对精度不敏感。</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly TicketManager _ticketManager;
    private readonly TicketWorkflowService _workflow;
    private readonly TicketCardService _cardService;
    private readonly TicketDeletionQueue _deletions;
    private readonly TicketSootheService _soothe;

    public TicketMaintenanceService(
        ILogger logger,
        DiscordSocketClient client,
        ConfigLoader<TicketConfig> configLoader,
        TicketManager ticketManager,
        TicketWorkflowService workflow,
        TicketCardService cardService,
        TicketDeletionQueue deletions,
        TicketSootheService soothe)
    {
        _logger = logger.ForContext("TicketService.TicketMaintenanceService");
        _client = client;
        _configLoader = configLoader;
        _ticketManager = ticketManager;
        _workflow = workflow;
        _cardService = cardService;
        _deletions = deletions;
        _soothe = soothe;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 对账失败不能拦住自动通过
            _logger.Error(ex, "启动对账失败");
        }

        using var timer = new PeriodicTimer(ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ApproveDueTicketsAsync(stoppingToken);
                    await SootheSilentTicketsAsync(stoppingToken);
                    await ProcessDeletionsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "自动通过扫描失败");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    /// <summary>
    /// 启动对账。只修复"能从磁盘状态推断出正确做法"的情况，其余留给人工。
    /// </summary>
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        _deletions.Load();

        foreach (var ticket in _ticketManager.GetActiveTickets())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(ticket.GuildId, out var config))
            {
                _logger.Warning("对账跳过：工单所属服务器没有配置: {Ticket} GuildId={GuildId}", ticket, ticket.GuildId);
                continue;
            }

            var guild = _client.GetGuild(ticket.GuildId);
            if (guild is null)
            {
                _logger.Warning("对账跳过：Bot 不在该服务器内: {Ticket} GuildId={GuildId}", ticket, ticket.GuildId);
                continue;
            }

            // 已通过/已关闭却缺频道：按频道名认领上次建到一半的频道
            if (ticket.ChannelId == 0 && ticket.Status is TicketStatus.Open or TicketStatus.Closed)
            {
                var name = TicketWorkflowService.GetChannelName(ticket.Id);
                var channel = guild.TextChannels
                    .FirstOrDefault(c => c.CategoryId == config.TicketCategory && c.Name == name);

                if (channel is null)
                {
                    _logger.Warning("对账：工单已通过但找不到频道，需人工处理: {Ticket} ChannelName={ChannelName}",
                        ticket, name);
                    continue;
                }

                ticket.ChannelId = channel.Id;
                await _ticketManager.SaveAsync(ticket);
                _logger.Warning("对账：工单 {Ticket} 已认领频道 {ChannelId}", ticket, channel.Id);
                continue;
            }

            // 待审核却没有审核卡片：上次发卡片后崩溃，重发一张
            if (ticket.Status == TicketStatus.Pending && ticket.AuditMessageId == 0)
            {
                var ok = await _cardService.RepublishAuditCardAsync(guild, config, ticket);
                _logger.Warning("对账：待审核工单缺审核卡片，已重发={Ok}: {Ticket}", ok, ticket);
            }
        }
    }

    /// <summary>
    /// 执行到期的归档频道删除。删除前校验频道类型（普通文字频道）、工单一致性与频道身份；
    /// 校验失败直接丢弃队列项，Discord API 失败则按 5 分钟退避、最多重试 3 次。
    /// </summary>
    private async Task ProcessDeletionsAsync(CancellationToken cancellationToken)
    {
        var due = _deletions.GetDue(DateTimeOffset.UtcNow);
        if (due.Count == 0)
        {
            return;
        }

        var changed = false;

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var guild = _client.GetGuild(entry.GuildId);
            if (guild is null)
            {
                Drop(entry, "Bot 不在服务器内");
                continue;
            }

            if (guild.GetChannel(entry.ChannelId) is not ITextChannel channel)
            {
                Drop(entry, "频道已不存在", expected: true);
                continue;
            }

            // 线程（SocketThreadChannel）与公告（SocketNewsChannel）都实现 ITextChannel，必须显式排除，
            // 否则队列被篡改时可能删到线程或公告频道
            if (channel is IThreadChannel or INewsChannel)
            {
                Drop(entry, $"频道类型不符（只允许普通文字频道，实际 {channel.GetType().Name}）");
                continue;
            }

            // 队列文件只是调度用的磁盘数据，可信源永远是工单本身
            var ticket = _ticketManager.LoadAny(entry.TicketId);
            if (ticket is null)
            {
                Drop(entry, "找不到对应工单，无法确认删除目标");
                continue;
            }

            if (ticket.GuildId != entry.GuildId || ticket.ChannelId != entry.ChannelId)
            {
                Drop(entry, $"队列条目与工单不一致（工单 GuildId={ticket.GuildId} ChannelId={ticket.ChannelId}）");
                continue;
            }

            if (ticket.Status != TicketStatus.Archived)
            {
                Drop(entry, $"工单状态为 {ticket.Status}，不是已归档");
                continue;
            }

            var expectedName = TicketWorkflowService.GetChannelName(ticket.Id);
            if (!string.Equals(channel.Name, expectedName, StringComparison.Ordinal))
            {
                Drop(entry, $"频道名不符（期望 {expectedName}，实际 {channel.Name}）");
                continue;
            }

            // 分组校验依赖服务器配置；配置缺失时只保留工单 + 频道名校验
            if (_configLoader.Value.DeployGuildConfig.TryGetValue(entry.GuildId, out var config)
                && channel.CategoryId != config.TicketCategory)
            {
                Drop(entry, $"频道分组不符（期望 {config.TicketCategory}，实际 {channel.CategoryId?.ToString() ?? "无分组"}）");
                continue;
            }

            // 删除目标一律以工单为准（entry.ChannelId 已在上面校验过一致性）
            var channelId = ticket.ChannelId;

            try
            {
                await channel.DeleteAsync(new RequestOptions
                {
                    AuditLogReason = $"归档工单 {ticket} 到期删除",
                });

                _deletions.Remove(channelId);
                changed = true;
                _logger.Info("归档频道已删除: {Ticket} ChannelId={ChannelId}", ticket, channelId);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                // 已被管理员手动删除：按成功处理，避免无效的退避重试
                _deletions.Remove(channelId);
                changed = true;
                _logger.Info("归档频道已不存在（可能被手动删除），移除队列项: {Ticket} ChannelId={ChannelId}",
                    ticket, channelId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var abandoned = _deletions.RecordFailure(channelId, DateTimeOffset.UtcNow);
                changed = true;

                if (abandoned)
                {
                    _logger.Error(ex, "删除归档频道连续失败，已放弃: {Ticket} ChannelId={ChannelId}", ticket, channelId);
                }
                else
                {
                    _logger.Warning(ex, "删除归档频道失败，5 分钟后重试: {Ticket} ChannelId={ChannelId}", ticket, channelId);
                }
            }
        }

        if (changed)
        {
            await _deletions.SaveAsync(cancellationToken);
        }

        // 校验失败都是确定性失败，直接丢弃条目；expected 表示属于正常情况（频道本来就已经没了）
        void Drop(PendingDeletion item, string reason, bool expected = false)
        {
            if (expected)
            {
                _logger.Info("删除队列：{Reason}，移除条目: TicketId={TicketId} ChannelId={ChannelId} GuildId={GuildId}",
                    reason, item.TicketId, item.ChannelId, item.GuildId);
            }
            else
            {
                _logger.Warning("删除队列：{Reason}，跳过删除并移除条目: TicketId={TicketId} ChannelId={ChannelId} GuildId={GuildId}",
                    reason, item.TicketId, item.ChannelId, item.GuildId);
            }

            _deletions.Remove(item.ChannelId);
            changed = true;
        }
    }

    private async Task ApproveDueTicketsAsync(CancellationToken cancellationToken)
    {
        var due = _ticketManager.GetDueAutoApprove(DateTimeOffset.UtcNow);
        foreach (var ticket in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_ticketManager.TryBeginTransition(ticket.Id))
            {
                // 正在被管理员处理，下一轮再看
                continue;
            }

            try
            {
                if (!_configLoader.Value.DeployGuildConfig.TryGetValue(ticket.GuildId, out var config))
                {
                    _logger.Warning("自动通过跳过：服务器没有配置: {Ticket} GuildId={GuildId}", ticket, ticket.GuildId);
                    continue;
                }

                var guild = _client.GetGuild(ticket.GuildId);
                if (guild is null)
                {
                    _logger.Warning("自动通过跳过：Bot 不在该服务器内: {Ticket}", ticket);
                    continue;
                }

                var result = await _workflow.ApproveAsync(
                    guild, config, ticket, TicketActor.System, isAutoApprove: true);

                _logger.Info("自动通过: {Ticket} 结果={Success} {Message}", ticket, result.Success, result.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "自动通过失败: {Ticket}", ticket);
            }
            finally
            {
                _ticketManager.EndTransition(ticket.Id);
            }
        }
    }

    /// <summary>
    /// 自动通过的工单在约定时间内没有管理组发言时，向提交人发送安抚消息。
    /// 只做本地条件筛选，真正需要访问 Discord 的工单才占用处理权。
    /// </summary>
    private async Task SootheSilentTicketsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var ticket in _ticketManager.GetActiveTickets())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(ticket.GuildId, out var config))
            {
                continue;
            }

            if (!TicketSootheService.ShouldProcess(ticket, config, now))
            {
                continue;
            }

            var guild = _client.GetGuild(ticket.GuildId);
            if (guild is null)
            {
                _logger.Warning("自动安抚跳过：Bot 不在该服务器内: {Ticket} GuildId={GuildId}", ticket, ticket.GuildId);
                continue;
            }

            if (!_ticketManager.TryBeginTransition(ticket.Id))
            {
                // 正在被管理员处理，下一轮再看
                continue;
            }

            try
            {
                await _soothe.ProcessAsync(guild, config, ticket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "自动安抚失败，下一轮重试: {Ticket}", ticket);
            }
            finally
            {
                _ticketManager.EndTransition(ticket.Id);
            }
        }
    }
}
