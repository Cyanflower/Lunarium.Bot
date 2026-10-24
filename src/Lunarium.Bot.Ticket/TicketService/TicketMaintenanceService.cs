using Discord;
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

    /// <summary>执行到期的归档频道删除；失败按 5 分钟退避，最多 3 次。</summary>
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
                _logger.Warning("删除队列：Bot 不在服务器内，丢弃: TicketId={TicketId} GuildId={GuildId}",
                    entry.TicketId, entry.GuildId);
                _deletions.Remove(entry.ChannelId);
                changed = true;
                continue;
            }

            if (guild.GetChannel(entry.ChannelId) is not ITextChannel channel)
            {
                _logger.Info("删除队列：频道已不存在，移除: TicketId={TicketId} ChannelId={ChannelId}",
                    entry.TicketId, entry.ChannelId);
                _deletions.Remove(entry.ChannelId);
                changed = true;
                continue;
            }

            try
            {
                await channel.DeleteAsync();
                _deletions.Remove(entry.ChannelId);
                changed = true;
                _logger.Info("归档频道已删除: TicketId={TicketId} ChannelId={ChannelId}", entry.TicketId, entry.ChannelId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var abandoned = _deletions.RecordFailure(entry.ChannelId, DateTimeOffset.UtcNow);
                changed = true;

                if (abandoned)
                {
                    _logger.Error(ex, "删除归档频道连续失败，已放弃: TicketId={TicketId} ChannelId={ChannelId}",
                        entry.TicketId, entry.ChannelId);
                }
                else
                {
                    _logger.Warning(ex, "删除归档频道失败，5 分钟后重试: TicketId={TicketId} ChannelId={ChannelId}",
                        entry.TicketId, entry.ChannelId);
                }
            }
        }

        if (changed)
        {
            await _deletions.SaveAsync(cancellationToken);
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
