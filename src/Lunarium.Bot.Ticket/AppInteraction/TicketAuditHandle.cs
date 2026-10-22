using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Ticket.Component;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Ticket.TicketService;
using Lunarium.Common.UID;
using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.AppInteraction;

/// <summary>
/// 审核卡片（办公厅）上的通过 / 拒绝交互。
/// <para>
/// 审核通过后才会创建工单频道；拒绝不建频道，只私信提交人理由。
/// 并发保护由 <see cref="TicketManager.TryBeginTransition"/> 保证：两个管理员同时点按钮时只有一个人能执行。
/// </para>
/// </summary>
public sealed class TicketAuditHandle : InteractionModuleBase<SocketInteractionContext>
{
    private enum AuditAction
    {
        Approve,
        Reject,
        FastReject
    }

    private readonly ILogger _logger;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly TicketManager _ticketManager;
    private readonly TicketWorkflowService _workflow;

    public TicketAuditHandle(
        ILogger logger,
        ConfigLoader<TicketConfig> configLoader,
        TicketManager ticketManager,
        TicketWorkflowService workflow)
    {
        _logger = logger.ForContext("TicketAuditHandle");
        _configLoader = configLoader;
        _ticketManager = ticketManager;
        _workflow = workflow;
    }

    [ComponentInteraction("audit:approve:*")]
    public Task ApproveAsync(string ticketId) => HandleComponentAsync(AuditAction.Approve, ticketId);

    [ComponentInteraction("audit:reject:*")]
    public Task RejectAsync(string ticketId) => HandleComponentAsync(AuditAction.Reject, ticketId);

    [ComponentInteraction("audit:fastReject:*")]
    public Task FastRejectAsync(string ticketId) => HandleComponentAsync(AuditAction.FastReject, ticketId);

    [ModalInteraction("modal:audit:reject:*")]
    public async Task RejectModalAsync(string ticketId, ComponentTemplate.AuditModal.InteractionModal modal)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (Context.Interaction is not SocketModal)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
                return;
            }

            var resolved = await ResolveAsync(ticketId);
            if (!resolved.Success)
            {
                await RespondAsync(components: resolved.Failure, ephemeral: true);
                return;
            }

            await ExecuteTransitionAsync(
                resolved.Guild!, resolved.Config!, resolved.Ticket!,
                AuditAction.Reject, TicketActor.From(Context.User), modal.Reason);
        }
        catch (Exception ex)
        {
            track.Error(ex, "处理拒绝模态窗时发生异常");
            await TryReportFailureAsync();
        }
    }

    private async Task HandleComponentAsync(AuditAction action, string ticketId)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (Context.Interaction is not SocketMessageComponent)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是消息组件交互"), ephemeral: true);
                return;
            }

            var resolved = await ResolveAsync(ticketId);
            if (!resolved.Success)
            {
                await RespondAsync(components: resolved.Failure, ephemeral: true);
                return;
            }

            var guild = resolved.Guild!;
            var ticket = resolved.Ticket!;

            if (ticket.Status != TicketStatus.Pending)
            {
                await RespondAsync(
                    components: MessagePrefab.AnyNotifier($"工单当前状态为 {ticket.Status}", "### 无需处理"),
                    ephemeral: true);
                return;
            }

            switch (action)
            {
                case AuditAction.Reject:
                    // 不 defer：模态窗本身就是这次交互的响应
                    await RespondWithModalAsync(ComponentTemplate.AuditModal.ToModal(ticket.Id));
                    return;

                case AuditAction.FastReject:
                    var reason = ticket.LLMSuggestion?.RejectReason;
                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        await RespondAsync(
                            components: MessagePrefab.AnyNotifier(
                                "该工单没有可用的 LLM 拒绝理由，请改用「拒绝」并填写原因",
                                "### 无法快速拒绝"),
                            ephemeral: true);
                        return;
                    }

                    await ExecuteTransitionAsync(
                        guild, resolved.Config!, ticket, action, TicketActor.From(Context.User), reason);
                    return;

                case AuditAction.Approve:
                    await ExecuteTransitionAsync(
                        guild, resolved.Config!, ticket, action, TicketActor.From(Context.User), null);
                    return;
            }
        }
        catch (Exception ex)
        {
            track.Error(ex, "处理审核交互时发生异常");
            await TryReportFailureAsync();
        }
    }

    /// <summary>占用工单 → 复核状态 → 执行迁移 → 释放占用。</summary>
    private async Task ExecuteTransitionAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket,
        AuditAction action,
        TicketActor actor,
        string? reason)
    {
        if (!_ticketManager.TryBeginTransition(ticket.Id))
        {
            await RespondAsync(
                components: MessagePrefab.AnyNotifier("该工单正在被其他管理员处理，请稍后重试", "### 请稍候"),
                ephemeral: true);
            return;
        }

        try
        {
            // 占用与状态复核必须在同一步：点击到执行之间状态可能已被别人改掉
            if (ticket.Status != TicketStatus.Pending)
            {
                await RespondAsync(
                    components: MessagePrefab.AnyNotifier($"工单当前状态为 {ticket.Status}", "### 无需处理"),
                    ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var result = action switch
            {
                AuditAction.Approve => await _workflow.ApproveAsync(guild, config, ticket, actor),
                AuditAction.FastReject => await _workflow.RejectAsync(guild, ticket, actor, reason!),
                AuditAction.Reject => await _workflow.RejectAsync(guild, ticket, actor, reason ?? string.Empty),
                _ => WorkflowResult.Fail("不支持的审核操作"),
            };

            _logger.Info("审核操作完成: 工单={Ticket} 操作={Action} 结果={Success} 操作人={ActorId}",
                ticket, action, result.Success, actor.Id);

            await FollowupAsync(
                components: result.Success
                    ? MessagePrefab.OperateSuccess(result.Message)
                    : MessagePrefab.FunctionFail(result.Message),
                ephemeral: true);
        }
        finally
        {
            _ticketManager.EndTransition(ticket.Id);
        }
    }

    /// <summary>
    /// 解析工单并做鉴权。失败时 <see cref="ResolvedTicket.Failure"/> 就是要回给用户的内容。
    /// </summary>
    private async Task<ResolvedTicket> ResolveAsync(string rawTicketId)
    {
        if (!int.TryParse(rawTicketId, out var ticketId))
        {
            return ResolvedTicket.Fail(MessagePrefab.FunctionFail("工单编号无效"));
        }

        if (Context.Guild is not SocketGuild guild)
        {
            return ResolvedTicket.Fail(MessagePrefab.FunctionFail("解析服务器信息失败"));
        }

        if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
        {
            return ResolvedTicket.Fail(MessagePrefab.ConfigFail("当前配置中并无本服务器配置"));
        }

        if (!_ticketManager.TryGet(ticketId, out var ticket) || ticket is null)
        {
            return ResolvedTicket.Fail(MessagePrefab.AnyFail($"找不到工单 {TicketData.FormatId(ticketId)}", "### 工单不存在"));
        }

        if (ticket.GuildId != guild.Id)
        {
            return ResolvedTicket.Fail(MessagePrefab.AuthFail("工单不属于本服务器"));
        }

        var validation = await RuleEngine.ValidateAsync(
            Context,
            new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
            new BaseRuleSet.RequireAdminRole(config.AllowManageTicketAdminRoleIds.ToHashSet()));

        return validation is not null
            ? ResolvedTicket.Fail(validation)
            : new ResolvedTicket(guild, config, ticket, null);
    }

    /// <summary>异常兜底：给用户一个明确的结束态，避免交互一直转圈。</summary>
    private async Task TryReportFailureAsync()
    {
        try
        {
            var component = MessagePrefab.FunctionFail("处理审核时发生内部错误，请联系维护者");
            if (Context.Interaction.HasResponded)
            {
                await FollowupAsync(components: component, ephemeral: true);
            }
            else
            {
                await RespondAsync(components: component, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "向管理员回报审核失败时再次失败");
        }
    }

    private sealed record ResolvedTicket(
        SocketGuild? Guild,
        GuildConfig? Config,
        TicketData? Ticket,
        MessageComponent? Failure)
    {
        public bool Success => Failure is null;

        public static ResolvedTicket Fail(MessageComponent failure) => new(null, null, null, failure);
    }
}
