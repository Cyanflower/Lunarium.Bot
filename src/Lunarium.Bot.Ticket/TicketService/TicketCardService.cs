using Discord;
using Discord.WebSocket;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Component;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

using Polly.Retry;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>建单后两张卡片的发送结果，用于给用户拼回执。</summary>
/// <param name="StatusCardSent">状态卡片（投诉记录频道）是否发送成功。</param>
/// <param name="AuditCardSent">审核卡片（办公厅）是否发送成功。</param>
public sealed record TicketPublishResult(bool StatusCardSent, bool AuditCardSent)
{
    /// <summary>两张卡片都送达。</summary>
    public bool AllSent => StatusCardSent && AuditCardSent;
}

/// <summary>
/// 工单卡片发布：把工单投递到"投诉记录"（状态卡片）与"办公厅"（审核卡片）。
/// <para>
/// 卡片发送失败不会让建单失败——工单此时已经落盘，管理员可在修复配置后重新发布；
/// 用户回执会提示"通知失败"。
/// </para>
/// </summary>
public sealed class TicketCardService
{
    /// <summary>
    /// 审核卡片发送时允许的提及类型。
    /// <para>
    /// Components V2 的 <c>TextDisplay</c> 里的提及会按 <c>allowed_mentions</c> 真正 ping，
    /// 模板里的 <c>@here</c> 需要 <see cref="AllowedMentionTypes.Everyone"/> 才生效。
    /// 这里刻意只开放 everyone/here：工单正文是用户原始输入，若一并放开
    /// <see cref="AllowedMentionTypes.Users"/> / <see cref="AllowedMentionTypes.Roles"/>，
    /// 用户就能借正文提及任意成员或身份组。
    /// </para>
    /// </summary>
    private static readonly AllowedMentions AuditCardMentions = new(AllowedMentionTypes.Everyone);

    private readonly ILogger _logger;
    private readonly TicketManager _ticketManager;
    private readonly TicketSuggestionService _suggestionService;
    private readonly AsyncRetryPolicy _retryPolicy;

    public TicketCardService(
        ILogger logger,
        TicketManager ticketManager,
        TicketSuggestionService suggestionService)
    {
        _logger = logger.ForContext("TicketCardService");
        _ticketManager = ticketManager;
        _suggestionService = suggestionService;
        _retryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(_logger);
    }

    /// <summary>
    /// 新工单入库后的完整投递：状态卡片 → AI 建议 → 审核卡片。
    /// 每一步都立即落盘，进程崩在中途时最多重发一张卡片，不会丢工单。
    /// </summary>
    public async Task<TicketPublishResult> PublishNewTicketAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket)
    {
        var statusSent = await PublishStatusCardAsync(guild, config.TicketLogChannel, ticket);

        // 不在这里用 IsAvailable 拦截：由 SuggestAsync 统一记录"为何没有建议"的日志
        var suggestion = await _suggestionService.SuggestAsync(ticket);
        if (suggestion is not null)
        {
            ticket.LLMSuggestion = suggestion;
            // 先落盘再发审核卡片：即使卡片发送失败，建议也不会重复计费
            await _ticketManager.SaveAsync(ticket);
        }

        var auditSent = await PublishAuditCardAsync(guild, config.NotifierChannel, ticket);

        return new TicketPublishResult(statusSent, auditSent);
    }

    /// <summary>重发审核卡片（启动对账用：上次发完卡片但没来得及落盘坐标）。</summary>
    public Task<bool> RepublishAuditCardAsync(SocketGuild guild, GuildConfig config, TicketData ticket)
        => PublishAuditCardAsync(guild, config.NotifierChannel, ticket);

    private async Task<bool> PublishStatusCardAsync(SocketGuild guild, ulong channelId, TicketData ticket)
    {
        var channel = ResolveTextChannel(guild, channelId);
        if (channel is null)
        {
            _logger.Warning("投诉记录频道无效或不是文字频道，跳过状态卡片: {Ticket} ChannelId={ChannelId}",
                ticket, channelId);
            return false;
        }

        try
        {
            var message = await _retryPolicy.ExecuteAsync(() =>
                channel.SendMessageAsync(embed: ComponentTemplate.TicketStatusMessage.ToEmbed(ticket)));

            ticket.StatusChannelId = channel.Id;
            ticket.StatusMessageId = message.Id;
            await _ticketManager.SaveAsync(ticket);

            _logger.Info("状态卡片已发送: {Ticket} ChannelId={ChannelId} MessageId={MessageId}",
                ticket, channel.Id, message.Id);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "发送状态卡片失败: {Ticket} ChannelId={ChannelId}", ticket, channel.Id);
            return false;
        }
    }

    private async Task<bool> PublishAuditCardAsync(SocketGuild guild, ulong channelId, TicketData ticket)
    {
        var channel = ResolveTextChannel(guild, channelId);
        if (channel is null)
        {
            _logger.Warning("审核频道无效或不是文字频道，跳过审核卡片: {Ticket} ChannelId={ChannelId}",
                ticket, channelId);
            return false;
        }

        try
        {
            var message = await _retryPolicy.ExecuteAsync(() =>
                channel.SendMessageAsync(
                    components: ComponentTemplate.PendingAuditMessage.ToComponent(
                        ticket.Id,
                        ticket.TicketCreator.UserId,
                        BuildAuditContent(ticket),
                        ticket.AutoApproveTime,
                        ticket.LLMSuggestion),
                    allowedMentions: AuditCardMentions));

            ticket.AuditChannelId = channel.Id;
            ticket.AuditMessageId = message.Id;
            await _ticketManager.SaveAsync(ticket);

            _logger.Info("审核卡片已发送: {Ticket} ChannelId={ChannelId} MessageId={MessageId}",
                ticket, channel.Id, message.Id);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "发送审核卡片失败: {Ticket} ChannelId={ChannelId}", ticket, channel.Id);
            return false;
        }
    }

    /// <summary>审核卡片正文：帮助类会带"相关链接"，其余只有正文。</summary>
    public static string BuildAuditContent(TicketData ticket)
    {
        return string.IsNullOrWhiteSpace(ticket.SecondaryContent)
            ? ticket.Content
            : $"{ticket.Content}\n\n> 📎 相关链接：{ticket.SecondaryContent}";
    }

    private static ITextChannel? ResolveTextChannel(SocketGuild guild, ulong channelId)
    {
        return channelId == 0 ? null : guild.GetChannel(channelId) as ITextChannel;
    }
}
