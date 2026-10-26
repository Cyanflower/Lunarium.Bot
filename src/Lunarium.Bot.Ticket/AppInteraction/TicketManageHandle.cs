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
        _logger = logger.ForContext("AppInteraction.TicketManageHandle");
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

    [SlashCommand("关闭", "关闭当前工单（仅管理组）")]
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

            // 只允许管理组关闭：提交人自行关闭后再归档仍需管理介入，不如统一由管理收口
            var failure = await ValidateAdminAsync(config);
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
                if (ticket.Status != TicketStatus.Open)
                {
                    await RespondAsync(
                        components: MessagePrefab.AnyNotifier($"工单当前状态为 {ticket.Status}，无法关闭", "### 状态不符"),
                        ephemeral: true);
                    return;
                }

                await DeferAsync(ephemeral: true);
                track.Info("关闭: Defer 完成，开始执行工作流: Ticket={Ticket}", ticket);

                var result = await _workflow.CloseAsync(guild, ticket, TicketActor.From(Context.User), reason);
                track.Info("关闭: 工作流完成 Success={Success} Message={Message}", result.Success, result.Message);

                await TrySendResultAsync(result);
                track.Info("关闭: 回执发送结束");
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
    public async Task ArchiveAsync(
        [Summary("删除频道", "归档后是否删除频道，默认删除")] bool deleteChannel = true)
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

                // 抓取大附件 + 打包可能耗时数十秒，先把 deferred 响应改成可见的进度提示
                await TryUpdateProgressAsync("正在抓取工单对话并打包归档，请稍候…", "### 处理中");

                var result = await _workflow.ArchiveAsync(guild, resolved.Config!, ticket, TicketActor.From(Context.User), deleteChannel);
                await TrySendResultAsync(result);
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

    /// <summary>关闭通告上的「归档并删除频道」按钮。</summary>
    [ComponentInteraction("close:archive:delete:*", ignoreGroupNames: true)]
    public Task ArchiveDeleteFromNoticeAsync(string ticketId)
        => HandleCloseNoticeArchiveAsync(ticketId, deleteChannel: true);

    /// <summary>关闭通告上的「归档并保留频道」按钮。</summary>
    [ComponentInteraction("close:archive:keep:*", ignoreGroupNames: true)]
    public Task ArchiveKeepFromNoticeAsync(string ticketId)
        => HandleCloseNoticeArchiveAsync(ticketId, deleteChannel: false);

    /// <summary>
    /// 关闭通告上的归档按钮：与 <c>/工单 归档</c> 等价。
    /// 按钮对提交人可见，所以必须重新鉴权；工单号以 customId 为准，并校验按钮所在频道就是工单频道。
    /// </summary>
    private async Task HandleCloseNoticeArchiveAsync(string ticketIdText, bool deleteChannel)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (!int.TryParse(ticketIdText, out var ticketId))
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("按钮参数无效"), ephemeral: true);
                return;
            }

            if (Context.Guild is not SocketGuild guild)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"), ephemeral: true);
                return;
            }

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
            {
                await RespondAsync(components: MessagePrefab.ConfigFail("当前配置中并无本服务器配置"), ephemeral: true);
                return;
            }

            // customId 里的工单号可能被复用到其它频道，必须用频道交叉校验；
            // LoadAny 在归档后仍能读到磁盘数据，便于识别“已归档”并自我修复按钮状态
            var ticket = _ticketManager.LoadAny(ticketId);
            if (ticket is null
                || ticket.GuildId != guild.Id
                || ticket.ChannelId != Context.Channel.Id)
            {
                await RespondAsync(
                    components: MessagePrefab.AnyFail("按钮与当前工单不匹配", "### 无法操作"),
                    ephemeral: true);
                return;
            }

            var failure = await ValidateAdminAsync(config);
            if (failure is not null)
            {
                await RespondAsync(components: failure, ephemeral: true);
                return;
            }

            if (ticket.Status == TicketStatus.Archived)
            {
                await TryMarkCloseNoticeArchivedAsync(ticketId, deleteChannel: null);
                await RespondAsync(
                    components: MessagePrefab.AnyNotifier($"工单 {ticket} 已归档，无需重复操作", "### 已归档"),
                    ephemeral: true);
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
                        components: MessagePrefab.AnyNotifier(
                            $"工单当前状态为 {ticket.Status}，需先关闭才能归档", "### 状态不符"),
                        ephemeral: true);
                    return;
                }

                await DeferAsync(ephemeral: true);
                await TryUpdateProgressAsync("正在抓取工单对话并打包归档，请稍候…", "### 处理中");

                var result = await _workflow.ArchiveAsync(
                    guild, config, ticket, TicketActor.From(Context.User), deleteChannel);

                if (result.Success)
                {
                    await TryMarkCloseNoticeArchivedAsync(ticketId, deleteChannel);
                }

                await TrySendResultAsync(result);
            }
            finally
            {
                _ticketManager.EndTransition(ticket.Id);
            }
        }
        catch (Exception ex)
        {
            track.Error(ex, "关闭通告归档按钮处理失败");
            await TryReportFailureAsync("归档工单时发生内部错误");
        }
    }

    /// <summary>
    /// 把关闭通告上的按钮改为禁用状态（保留操作痕迹）。
    /// <para>
    /// 此时已经 <c>DeferAsync</c>，<c>SocketMessageComponent.UpdateAsync</c> 会因交互已响应而失败，
    /// 所以必须走 REST 的 <c>Message.ModifyAsync</c>。
    /// </para>
    /// </summary>
    private async Task TryMarkCloseNoticeArchivedAsync(int ticketId, bool? deleteChannel)
    {
        if (Context.Interaction is not SocketMessageComponent component)
        {
            return;
        }

        try
        {
            var components = ComponentTemplate.CloseNotice.ToArchivedComponents(ticketId, deleteChannel);
            await component.Message.ModifyAsync(props => props.Components = components);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "更新关闭通告按钮状态失败: TicketId={TicketId}", ticketId);
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
            // 归档后活跃索引会移除该频道；用频道名反推工单号，给出准确提示而不是「频道不匹配」
            if (Context.Channel is IGuildChannel channel
                && TryParseTicketId(channel.Name, out var ticketId)
                && _ticketManager.LoadAny(ticketId) is { Status: TicketStatus.Archived } archived)
            {
                return ResolvedTicket.Fail(MessagePrefab.AnyNotifier(
                    $"工单 {archived} 已归档，无需重复操作", "### 工单已归档"));
            }

            return ResolvedTicket.Fail(MessagePrefab.AnyFail("此命令只能在工单频道中使用", "### 频道不匹配"));
        }

        return new ResolvedTicket(guild, config, ticket, null);
    }

    /// <summary>从 <c>ticket-00001</c> 形式的频道名解析工单号。</summary>
    private static bool TryParseTicketId(string channelName, out int ticketId)
    {
        const string Prefix = "ticket-";
        ticketId = 0;
        return channelName.Length > Prefix.Length
            && channelName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(channelName.AsSpan(Prefix.Length), out ticketId);
    }

    private async Task<MessageComponent?> ValidateAdminAsync(GuildConfig config)
    {
        return await RuleEngine.ValidateAsync(
            Context,
            new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
            new BaseRuleSet.RequireAdminRole(config.AllowManageTicketAdminRoleIds.ToHashSet()));
    }

    /// <summary>
    ///     发送命令结果。用 <c>FollowupAsync</c> 而不是编辑原始响应：
    ///     编辑路径需要手动补 Components V2 标志，行为在 defer 后不稳定；
    ///     Followup 由库自动补标志，与审核回执一致，已验证可靠。
    ///     回执失败只记日志，不再报「内部错误」。
    /// </summary>
    private async Task TrySendResultAsync(WorkflowResult result)
    {
        var component = result.Success
            ? MessagePrefab.OperateSuccess(result.Message)
            : MessagePrefab.FunctionFail(result.Message);

        try
        {
            await FollowupAsync(components: component, ephemeral: true);
            _logger.Info("命令结果回执已发送: Success={Success}", result.Success);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "发送命令结果回执失败: Success={Success} Message={Message}",
                result.Success, result.Message);
        }
    }

    /// <summary>把 deferred 响应更新为进度提示；失败只记日志，不影响后续流程。</summary>
    private Task TryUpdateProgressAsync(string message, string title)
        => TryEditOriginalAsync(MessagePrefab.AnyNotifier(message, title), "进度提示");

    /// <summary>
    ///     编辑 deferred 原始响应。注意 Components V2 必须显式带上 <see cref="MessageFlags.ComponentsV2"/>：
    ///     Followup 路径由库自动补这个标志，而编辑路径不会，缺了会被 Discord 以
    ///     <c>UNION_TYPE_CHOICES</c> 拒绝。编辑失败时退回发送新消息，保证用户至少能看到回执。
    /// </summary>
    private async Task TryEditOriginalAsync(MessageComponent component, string stage)
    {
        try
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Components = component;
                props.Flags = MessageFlags.ComponentsV2;
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "编辑原始响应失败，回退为发送新消息: {Stage}", stage);

            try
            {
                await FollowupAsync(components: component, ephemeral: true);
            }
            catch (Exception followupEx)
            {
                _logger.Warning(followupEx, "发送回执失败: {Stage}", stage);
            }
        }
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
