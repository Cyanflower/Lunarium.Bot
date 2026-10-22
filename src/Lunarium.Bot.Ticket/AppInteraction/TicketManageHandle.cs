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
/// 工单频道内的管理命令：<c>/工单 关闭</c>、<c>/工单 归档</c>、<c>/工单 信息</c>。
/// <para>
/// 命令一律按"当前频道反查工单"定位，不接受工单号参数，避免误操作到别服/别的工单。
/// </para>
/// </summary>
[Group("工单", "工单管理命令")]
public sealed class TicketManageHandle : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly TicketManager _ticketManager;
    private readonly TicketWorkflowService _workflow;

    public TicketManageHandle(
        ILogger logger,
        ConfigLoader<TicketConfig> configLoader,
        TicketManager ticketManager,
        TicketWorkflowService workflow)
    {
        _logger = logger.ForContext("TicketManageHandle");
        _configLoader = configLoader;
        _ticketManager = ticketManager;
        _workflow = workflow;
    }

    [SlashCommand("信息", "查看当前工单状态")]
    public async Task InfoAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            var resolved = ResolveFromChannel();
            if (!resolved.Success)
            {
                await RespondAsync(components: resolved.Failure, ephemeral: true);
                return;
            }

            await RespondAsync(
                embeds: [ComponentTemplate.TicketStatusMessage.ToEmbed(resolved.Ticket!)],
                ephemeral: true);
        }
        catch (Exception ex)
        {
            track.Error(ex, "查看工单信息失败");
            await TryReportFailureAsync("查看工单信息时发生内部错误");
        }
    }

    [SlashCommand("关闭", "关闭当前工单（提交人或管理组可用）")]
    public async Task CloseAsync([Summary("原因", "可选，会记录在频道内")] string? reason = null)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            var resolved = ResolveFromChannel();
            if (!resolved.Success)
            {
                await RespondAsync(components: resolved.Failure, ephemeral: true);
                return;
            }

            var guild = resolved.Guild!;
            var config = resolved.Config!;
            var ticket = resolved.Ticket!;

            // 提交人可以关闭自己的工单；其他人必须是管理组
            if (Context.User.Id != ticket.TicketCreator.UserId)
            {
                var failure = await ValidateAdminAsync(config);
                if (failure is not null)
                {
                    await RespondAsync(components: failure, ephemeral: true);
                    return;
                }
            }

            if (!_ticketManager.TryBeginTransition(ticket.Id))
            {
                await RespondAsync(
                    components: MessagePrefab.AnyNotifier("该工单正在被处理，请稍后重试", "### 请稍候"),
                    ephemeral: true);
                return;
            }

            try
            {
                if (ticket.Status != TicketStatus.Open)
                {
                    await RespondAsync(
                        components: MessagePrefab.AnyNotifier($"工单当前状态为 {ticket.Status}，无法关闭", "### 状态不符"),
                        ephemeral: true);
                    return;
                }

                await DeferAsync(ephemeral: true);

                var result = await _workflow.CloseAsync(guild, ticket, TicketActor.From(Context.User), reason);
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
        catch (Exception ex)
        {
            track.Error(ex, "关闭工单失败");
            await TryReportFailureAsync("关闭工单时发生内部错误");
        }
    }

    [SlashCommand("归档", "归档当前工单（需先关闭）")]
    public async Task ArchiveAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            var resolved = ResolveFromChannel();
            if (!resolved.Success)
            {
                await RespondAsync(components: resolved.Failure, ephemeral: true);
                return;
            }

            var guild = resolved.Guild!;
            var ticket = resolved.Ticket!;

            var failure = await ValidateAdminAsync(resolved.Config!);
            if (failure is not null)
            {
                await RespondAsync(components: failure, ephemeral: true);
                return;
            }

            if (!_ticketManager.TryBeginTransition(ticket.Id))
            {
                await RespondAsync(
                    components: MessagePrefab.AnyNotifier("该工单正在被处理，请稍后重试", "### 请稍候"),
                    ephemeral: true);
                return;
            }

            try
            {
                if (ticket.Status != TicketStatus.Closed)
                {
                    await RespondAsync(
                        components: MessagePrefab.AnyNotifier($"工单当前状态为 {ticket.Status}，需先关闭才能归档", "### 状态不符"),
                        ephemeral: true);
                    return;
                }

                await DeferAsync(ephemeral: true);

                var result = await _workflow.ArchiveAsync(guild, resolved.Config!, ticket, TicketActor.From(Context.User));
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
        catch (Exception ex)
        {
            track.Error(ex, "归档工单失败");
            await TryReportFailureAsync("归档工单时发生内部错误");
        }
    }

    /// <summary>按当前频道反查工单；命令只能在工单频道内使用。</summary>
    private ResolvedTicket ResolveFromChannel()
    {
        if (Context.Guild is not SocketGuild guild)
        {
            return ResolvedTicket.Fail(MessagePrefab.FunctionFail("解析服务器信息失败"));
        }

        if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
        {
            return ResolvedTicket.Fail(MessagePrefab.ConfigFail("当前配置中并无本服务器配置"));
        }

        if (!_ticketManager.TryGetByChannel(Context.Channel.Id, out var ticket) || ticket is null)
        {
            return ResolvedTicket.Fail(MessagePrefab.AnyFail("此命令只能在工单频道中使用", "### 频道不匹配"));
        }

        return new ResolvedTicket(guild, config, ticket, null);
    }

    private async Task<MessageComponent?> ValidateAdminAsync(GuildConfig config)
    {
        return await RuleEngine.ValidateAsync(
            Context,
            new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
            new BaseRuleSet.RequireAdminRole(config.AllowManageTicketAdminRoleIds.ToHashSet()));
    }

    private async Task TryReportFailureAsync(string message)
    {
        try
        {
            var component = MessagePrefab.FunctionFail(message);
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
            _logger.Warning(ex, "向用户回报工单命令失败时再次失败");
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
