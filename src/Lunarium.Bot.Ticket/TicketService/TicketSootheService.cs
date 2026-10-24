using Discord;
using Discord.WebSocket;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Component;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

using Polly.Retry;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>
/// 系统自动通过的工单在长时间无人回复时，向提交人发送固定文案的安抚消息，
/// 并在通知频道投递提示卡片与一条提及管理组身份组的文字消息。
/// <para>
/// 纯定时驱动：不监听消息、不做对话。"是否已有管理组回复"通过读取工单频道最近的
/// 历史消息判断（作者既不是提交人也不是 Bot 即视为已回复）。
/// </para>
/// <para>
/// 幂等由 <see cref="TicketData.SootheInfo"/> 保证：检查结果（发送 / 无需发送）一旦落盘，
/// 该工单就不会再被扫描；只有"安抚已发但通知未完成"会在重启后补发。
/// </para>
/// </summary>
public sealed class TicketSootheService
{
    /// <summary>判断是否已有管理组回复时，最多回看的历史消息数。</summary>
    private const int HistoryScanLimit = 300;

    /// <summary>
    /// 发给提交人的固定安抚文案。刻意不使用任何提及，避免额外的红点打扰。
    /// </summary>
    private const string SootheContent = """
        💬 管理组暂时还没有回复这条工单。

        你的诉求已经收到并记录在案，请放心，看到后我们会尽快跟进。如有补充信息，直接发在本频道即可。
        """;

    /// <summary>
    /// 通知消息只允许提及配置中的管理组身份组，不放开用户/everyone 提及。
    /// </summary>
    private static AllowedMentions BuildAdminRoleMentions(GuildConfig config)
        => new() { RoleIds = config.AllowManageTicketAdminRoleIds.ToList() };

    private readonly ILogger _logger;

    /// <summary>审计日志：安抚与通知属于系统自动行为，只进 Audit.log。</summary>
    private readonly ILogger _audit;

    private readonly TicketManager _ticketManager;
    private readonly AsyncRetryPolicy _retryPolicy;

    public TicketSootheService(ILogger logger, TicketManager ticketManager)
    {
        _logger = logger.ForContext("TicketService.TicketSootheService");
        _audit = logger.ForContext("Audit.TicketSoothe");
        _ticketManager = ticketManager;
        _retryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(_logger);
    }

    /// <summary>
    /// 本工单当前是否需要处理（纯本地判断，不访问 Discord）。
    /// </summary>
    public static bool ShouldProcess(TicketData ticket, GuildConfig config, DateTimeOffset now)
    {
        if (!ticket.IsAutoApprove || ticket.Status != TicketStatus.Open)
        {
            return false;
        }

        if (ticket.SootheInfo is null)
        {
            if (!config.EnableAutoSoothe || ticket.ApproveInfo is not { } approve)
            {
                return false;
            }

            return now >= approve.ApprovedAt.AddMinutes(config.SootheDelayMinutes);
        }

        // 安抚已发但通知未完成：重启或部分失败后补发
        return ticket.SootheInfo.MessageId > 0 && ticket.SootheInfo.NotifiedAt is null;
    }

    /// <summary>执行单张工单的安抚与通知。失败会抛出，由调用方记录并等待下一轮重试。</summary>
    public async Task ProcessAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ticket.SootheInfo is null)
        {
            await TrySendSootheAsync(guild, ticket);
        }

        if (ticket.SootheInfo is { MessageId: > 0, NotifiedAt: null })
        {
            await NotifyAsync(guild, config, ticket);
        }
    }

    private async Task TrySendSootheAsync(SocketGuild guild, TicketData ticket)
    {
        // 通道无效时也落盘，避免每 10 秒重复告警；这种情况本身需要人工检查配置
        if (ticket.ChannelId == 0 || guild.GetChannel(ticket.ChannelId) is not ITextChannel channel)
        {
            _logger.Warning("自动安抚跳过：工单频道无效: {Ticket} ChannelId={ChannelId}", ticket, ticket.ChannelId);
            await RecordSkippedAsync(ticket);
            return;
        }

        if (await HasStaffReplyAsync(channel, ticket))
        {
            _logger.Info("自动安抚跳过：已有管理组回复: {Ticket} ChannelId={ChannelId}", ticket, channel.Id);
            await RecordSkippedAsync(ticket);
            return;
        }

        var message = await _retryPolicy.ExecuteAsync(() =>
            channel.SendMessageAsync(SootheContent, allowedMentions: AllowedMentions.None));

        ticket.SootheInfo = new SootheInfo
        {
            CheckedAt = DateTimeOffset.UtcNow,
            MessageId = message.Id,
        };
        await _ticketManager.SaveAsync(ticket);

        _audit.Info("已发送自动安抚: {Ticket} ChannelId={ChannelId} MessageId={MessageId}",
            ticket, channel.Id, message.Id);
    }

    /// <summary>记录"本次检查无需发送"，使该工单不再进入扫描。</summary>
    private async Task RecordSkippedAsync(TicketData ticket)
    {
        ticket.SootheInfo = new SootheInfo { CheckedAt = DateTimeOffset.UtcNow };
        await _ticketManager.SaveAsync(ticket);
    }

    /// <summary>
    /// 判断频道内是否已有管理组发言。
    /// <para>
    /// 工单频道只有提交人、管理组身份组与 Bot 可见，因此"作者不是提交人、也不是 Bot"
    /// 即可视为管理组已经介入；系统消息（如置顶提示）的作者同样是操作人，不影响判断。
    /// </para>
    /// </summary>
    private static async Task<bool> HasStaffReplyAsync(ITextChannel channel, TicketData ticket)
    {
        var messages = await channel.GetMessagesAsync(HistoryScanLimit).FlattenAsync();

        foreach (var message in messages)
        {
            if (message.Author.Id == ticket.TicketCreator.UserId || message.Author.IsBot)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 在通知频道投递"提示卡片 + 管理组身份组提及"。
    /// <para>
    /// 卡片与提及消息分两步发送并分别落盘：卡片发送成功后即使进程崩溃，
    /// 重启也只补发提及消息，不会重复发卡片。
    /// </para>
    /// </summary>
    private async Task NotifyAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket)
    {
        var soothe = ticket.SootheInfo!;

        if (guild.GetChannel(config.NotifierChannel) is not ITextChannel channel)
        {
            _logger.Warning("自动安抚通知跳过：通知频道无效: {Ticket} ChannelId={ChannelId}",
                ticket, config.NotifierChannel);
            soothe.NotifiedAt = DateTimeOffset.UtcNow;
            await _ticketManager.SaveAsync(ticket);
            return;
        }

        if (soothe.NotifyMessageId == 0)
        {
            var card = await _retryPolicy.ExecuteAsync(() =>
                channel.SendMessageAsync(embed: ComponentTemplate.SootheNoticeMessage.ToEmbed(ticket)));

            soothe.NotifyMessageId = card.Id;
            await _ticketManager.SaveAsync(ticket);
        }

        // 未配置管理组时没有可提及的对象，退化为一条无提及的提示，避免整条通知丢失
        string content;
        if (config.AllowManageTicketAdminRoleIds.Count == 0)
        {
            _logger.Warning("自动安抚通知：服务器未配置管理组身份组，通知消息不会提及任何人: {Ticket}", ticket);
            content = "🔔 有工单需要处理。";
        }
        else
        {
            content = config.AllowManageTicketAdminRoleIds.JoinRoleMentions(" ");
        }

        await _retryPolicy.ExecuteAsync(() =>
            channel.SendMessageAsync(content, allowedMentions: BuildAdminRoleMentions(config)));

        soothe.NotifiedAt = DateTimeOffset.UtcNow;
        await _ticketManager.SaveAsync(ticket);

        _audit.Info("已发送自动安抚通知: {Ticket} ChannelId={ChannelId} CardMessageId={CardMessageId}",
            ticket, channel.Id, soothe.NotifyMessageId);
    }
}
