using Discord.Interactions;
using Discord;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Bot.Ticket.InteractionSession;
using Lunarium.Bot.Framework;
using Lunarium.Common.UID;

namespace Lunarium.Bot.Ticket.AppInteraction;

public sealed class ConfigEditorHandle : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly SessionManager _sessionManager;

    public ConfigEditorHandle(ILogger logger, ConfigLoader<TicketConfig> configLoader, SessionManager sessionManager)
    {
        _logger = logger.ForContext("ConfigEditorHandle");
        _configLoader = configLoader;
        _sessionManager = sessionManager;
    }

    [ComponentInteraction("editor:*:*")]
    public async Task EditorHandle(string action, string sessionId)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            track.Info("AppInteraction: {Action} 收到配置编辑交互: GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}, SessionId={SessionId}",
                action, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id, sessionId);

            if (Context.Interaction is not SocketMessageComponent component)
            {
                track.Warning("AppInteraction: {Action} 当前交互不是消息组件交互: InteractionType={InteractionType}, UserId={UserId}",
                    action, Context.Interaction.Type, Context.User.Id);
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是消息组件交互"), ephemeral: true);
                return;
            }

            if (!TryGetSession(track, action, sessionId, out var session, out var error))
            {
                await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
                return;
            }

            track.Info("AppInteraction: {Action} 会话校验通过: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, ExpiresAt={ExpiresAt}",
                action, session.Context.Guild.Id, Context.User.Id, session.SessionId, session.ExpiresAt);

            switch (action)
            {
                case "editAdminRoleIds":
                    await RespondWithModalAsync(ModalWindowPrefab.MultiRoleSelectField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑管理身份组",
                        label: "允许管理投诉的管理组身份组",
                        description: "可多选，留空表示不限制",
                        minValues: 0,
                        maxValues: 25,
                        required: false));
                    return;

                case "editAllowTicketRoleIds":
                    await RespondWithModalAsync(ModalWindowPrefab.MultiRoleSelectField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑投诉身份组",
                        label: "允许投诉的身份组",
                        description: "可多选，留空表示不限制",
                        minValues: 0,
                        maxValues: 25,
                        required: false));
                    return;

                case "editEntryPaneChannel":
                    await RespondWithModalAsync(ModalWindowPrefab.ChannelSelectField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑投诉入口面板发布频道",
                        label: "投诉入口面板发布频道",
                        description: "工单面板所在频道",
                        channelTypes: [ChannelType.Text, ChannelType.News],
                        minValues: 1,
                        maxValues: 1,
                        required: true));
                    return;

                case "editNotifierChannel":
                    await RespondWithModalAsync(ModalWindowPrefab.ChannelSelectField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑工单通知频道",
                        label: "工单通知频道",
                        description: "新工单通知/跳转链接发送至此",
                        channelTypes: [ChannelType.Text, ChannelType.News],
                        minValues: 1,
                        maxValues: 1,
                        required: true));
                    return;

                case "editTicketLogChannel":
                    await RespondWithModalAsync(ModalWindowPrefab.ChannelSelectField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑工单日志频道",
                        label: "工单日志频道",
                        description: "工单操作记录/日志发送至此",
                        channelTypes: [ChannelType.Text, ChannelType.News],
                        minValues: 1,
                        maxValues: 1,
                        required: true));
                    return;

                case "editTicketCategory":
                    await RespondWithModalAsync(ModalWindowPrefab.ChannelSelectField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑投诉频道分组",
                        label: "创建投诉频道的分组",
                        description: "新建投诉频道时放置于此分类",
                        channelTypes: [ChannelType.Category],
                        minValues: 1,
                        maxValues: 1,
                        required: true));
                    return;

                case "enableTicketCD":
                    track.Info("AppInteraction: {Action} 启用投诉冷却时间: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    session.SetEnableTicketCd(true);
                    await UpdatePanelAsync(component, session);
                    return;

                case "disableTicketCD":
                    track.Info("AppInteraction: {Action} 关闭投诉冷却时间: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    session.SetEnableTicketCd(false);
                    await UpdatePanelAsync(component, session);
                    return;

                case "editTicketCD":
                    await RespondWithModalAsync(ModalWindowPrefab.SingleTextField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑投诉冷却时间",
                        label: "冷却时间（小时）",
                        description: $"同一用户两次提交投诉之间的最小间隔，{TicketConfigLimits.TicketCdInclusiveMin}–{TicketConfigLimits.TicketCdInclusiveMax} 小时",
                        placeholder: "例如 24"));
                    return;

                case "editAutoApprove":
                    await RespondWithModalAsync(ModalWindowPrefab.SingleTextField(
                        new CustomId("editor", action, session.SessionId),
                        title: "编辑待审核自动通过时间",
                        label: "自动通过时间（小时）",
                        description: $"待审核工单超过该时长无人处理则自动通过，{TicketConfigLimits.AutoApproveHoursInclusiveMin}–{TicketConfigLimits.AutoApproveHoursInclusiveMax} 小时，0 表示不自动通过",
                        placeholder: "例如 24，填 0 关闭"));
                    return;

                case "toggleDeleteChannelOnArchive":
                    track.Info("AppInteraction: {Action} 切换归档后删频道: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    session.SetDeleteChannelOnArchive(!session.DeleteChannelOnArchive);
                    await UpdatePanelAsync(component, session);
                    return;

                case "apply":
                    await ApplyAsync(track, component, session);
                    return;

                case "cancel":
                    track.Info("AppInteraction: {Action} 取消配置编辑: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    session.Cancel();
                    _sessionManager.TryRemove(session.SessionId);
                    await UpdatePanelAsync(component, session);
                    return;

                default:
                    track.Error("AppInteraction: {Action} 收到未注册的 Action: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, Context.Guild?.Id, Context.User.Id, sessionId);
                    await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Action: {action}"), ephemeral: true);
                    return;
            }
        }
        catch (Exception ex)
        {
            track.Error(ex, "AppInteraction: {Action} 处理配置编辑交互时发生异常: GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}, SessionId={SessionId}",
                action, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id, sessionId);
        }
    }

    [ModalInteraction("editor:*:*")]
    public async Task EditorModalHandle(string action, string sessionId, ConfigEditorModal shell)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            track.Info("AppInteraction: {Action} 收到配置编辑模态窗提交: GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}, SessionId={SessionId}",
                action, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id, sessionId);

            if (Context.Interaction is not SocketModal modal)
            {
                track.Warning("AppInteraction: {Action} 当前交互不是模态窗交互: InteractionType={InteractionType}, UserId={UserId}",
                    action, Context.Interaction.Type, Context.User.Id);
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
                return;
            }

            if (!TryGetSession(track, action, sessionId, out var session, out var error))
            {
                await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
                return;
            }

            track.Info("AppInteraction: {Action} 会话校验通过: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, ExpiresAt={ExpiresAt}",
                action, session.Context.Guild.Id, Context.User.Id, session.SessionId, session.ExpiresAt);

            var data = modal.Data;

            switch (action)
            {
                case "editAdminRoleIds":
                    var adminRoleIds = ParseIds(data, "roles");
                    session.SetAdminRoleIds(adminRoleIds);
                    track.Info("AppInteraction: {Action} 更新管理身份组: RoleCount={RoleCount}, RoleIds={RoleIds}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, adminRoleIds.Count, string.Join(',', adminRoleIds), session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editAllowTicketRoleIds":
                    var ticketRoleIds = ParseIds(data, "roles");
                    session.SetAllowTicketRoleIds(ticketRoleIds);
                    track.Info("AppInteraction: {Action} 更新投诉身份组: RoleCount={RoleCount}, RoleIds={RoleIds}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, ticketRoleIds.Count, string.Join(',', ticketRoleIds), session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editEntryPaneChannel":
                    if (!TryParseChannelId(data, "channels", out var entryPaneChannel))
                    {
                        track.Warning("AppInteraction: {Action} 频道选择无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择一个有效频道", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    session.SetEntryPaneChannel(entryPaneChannel);
                    track.Info("AppInteraction: {Action} 更新投诉入口面板发布频道: ChannelId={ChannelId}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, entryPaneChannel, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editNotifierChannel":
                    if (!TryParseChannelId(data, "channels", out var notifierChannel))
                    {
                        track.Warning("AppInteraction: {Action} 频道选择无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择一个有效频道", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    session.SetNotifierChannel(notifierChannel);
                    track.Info("AppInteraction: {Action} 更新工单通知频道: ChannelId={ChannelId}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, notifierChannel, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editTicketLogChannel":
                    if (!TryParseChannelId(data, "channels", out var ticketLogChannel))
                    {
                        track.Warning("AppInteraction: {Action} 频道选择无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择一个有效频道", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    session.SetTicketLogChannel(ticketLogChannel);
                    track.Info("AppInteraction: {Action} 更新工单日志频道: ChannelId={ChannelId}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, ticketLogChannel, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editTicketCategory":
                    if (!TryParseChannelId(data, "channels", out var ticketCategory))
                    {
                        track.Warning("AppInteraction: {Action} 分组选择无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择一个有效分组", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    session.SetTicketCategory(ticketCategory);
                    track.Info("AppInteraction: {Action} 更新投诉频道分组: CategoryId={CategoryId}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, ticketCategory, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editTicketCD":
                    var raw = data.Components.FirstOrDefault(c => c.CustomId == "text")?.Value;
                    if (!int.TryParse(raw, out var hours) || !TicketConfigLimits.IsValidTicketCd(hours))
                    {
                        track.Warning("AppInteraction: {Action} 冷却时间输入无效: RawValue={RawValue}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, raw, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail(
                            $"冷却时间必须是 {TicketConfigLimits.TicketCdInclusiveMin}–{TicketConfigLimits.TicketCdInclusiveMax} 之间的整数",
                            "### 输入无效"), ephemeral: true);
                        return;
                    }
                    session.SetTicketCd(hours);
                    track.Info("AppInteraction: {Action} 更新投诉冷却时间: TicketCd={TicketCd}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, hours, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editAutoApprove":
                    var rawAutoApprove = data.Components.FirstOrDefault(c => c.CustomId == "text")?.Value;
                    if (!int.TryParse(rawAutoApprove, out var autoApproveHours) || !TicketConfigLimits.IsValidAutoApproveHours(autoApproveHours))
                    {
                        track.Warning("AppInteraction: {Action} 自动通过时间输入无效: RawValue={RawValue}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, rawAutoApprove, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail(
                            $"自动通过时间必须是 {TicketConfigLimits.AutoApproveHoursInclusiveMin}–{TicketConfigLimits.AutoApproveHoursInclusiveMax} 之间的整数（0 表示不自动通过）",
                            "### 输入无效"), ephemeral: true);
                        return;
                    }
                    session.SetAutoApproveHours(autoApproveHours);
                    track.Info("AppInteraction: {Action} 更新待审核自动通过时间: AutoApproveHours={AutoApproveHours}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, autoApproveHours, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                default:
                    track.Error("AppInteraction: {Action} 收到未注册的 Modal Action: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Action: {action}"), ephemeral: true);
                    return;
            }

            await modal.UpdateAsync(msg => msg.Components = session.Render());
        }
        catch (Exception ex)
        {
            track.Error(ex, "AppInteraction: {Action} 处理配置编辑模态窗时发生异常: GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}, SessionId={SessionId}",
                action, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id, sessionId);
        }
    }

    /// <summary>
    /// 校验会话归属并取出会话；失败时返回对应的失败原因。
    /// </summary>
    private bool TryGetSession(TrackScope track, string action, string sessionId, out ConfigEditorSession session, out string error)
    {
        session = null!;
        error = string.Empty;

        var parsed = HasPrefixSessionId.TryParse(sessionId);
        if (parsed is null || !ulong.TryParse(parsed.Prefix, out var userId))
        {
            track.Error("AppInteraction: {Action} 未能解析 ID 前缀: SessionId={SessionId}, UserId={UserId}", action, sessionId, Context.User.Id);
            error = "未能解析 ID 前缀";
            return false;
        }

        if (userId != Context.User.Id)
        {
            track.Warning("AppInteraction: {Action} 非交互创建者不可交互: SessionId={SessionId}, UserId={UserId}, OwnerId={OwnerId}",
                action, sessionId, Context.User.Id, userId);
            error = "非交互创建者不可交互";
            return false;
        }
        // 注意不能用 parsed.SessionId
        // 注册侧走含 Prefix 前缀注册方法
        // 字典内真实键值为 "ulong|nanoId", 例如"123456789|AxfgqASdcv"
        // 从manager取Session需要使用完整key: "123456789|AxfgqASdcv"
        if (!_sessionManager.TryGet<ConfigEditorSession>(sessionId, out var found) || found is null)
        {
            track.Warning("AppInteraction: {Action} 交互会话不存在或已过期: SessionId={SessionId}, UserId={UserId}",
                action, sessionId, Context.User.Id);
            error = "交互已过期";
            return false;
        }

        session = found;
        return true;
    }

    private async Task ApplyAsync(TrackScope track, SocketMessageComponent component, ConfigEditorSession session)
    {
        if (!_configLoader.Value.DeployerConfig.AllowModifyByInteraction)
        {
            track.Warning("AppInteraction: apply 当前配置不允许通过交互修改: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                Context.Guild?.Id, Context.User.Id, session.SessionId);
            await RespondAsync(components: MessagePrefab.FunctionFail("当前配置不允许通过交互修改"), ephemeral: true);
            return;
        }

        var guildId = Context.Guild?.Id ?? 0;
        if (guildId == 0)
        {
            track.Warning("AppInteraction: apply 解析服务器信息失败: UserId={UserId}, SessionId={SessionId}",
                Context.User.Id, session.SessionId);
            await RespondAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"), ephemeral: true);
            return;
        }

        var newConfig = session.BuildConfig();

        track.Info("AppInteraction: apply 开始应用配置: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, EnableTicketCd={EnableTicketCd}, TicketCd={TicketCd}, AutoApproveHours={AutoApproveHours}, EntryPaneChannel={EntryPaneChannel}, NotifierChannel={NotifierChannel}, TicketLogChannel={TicketLogChannel}, TicketCategory={TicketCategory}, AdminRoleCount={AdminRoleCount}, TicketRoleCount={TicketRoleCount}",
            guildId, Context.User.Id, session.SessionId,
            newConfig.EnableTicketCd, newConfig.TicketCd, newConfig.AutoApproveHours,
            newConfig.EntryPaneChannel, newConfig.NotifierChannel, newConfig.TicketLogChannel, newConfig.TicketCategory,
            newConfig.AllowManageTicketAdminRoleIds.Count, newConfig.AllowTicketRoleIds.Count);

        var errors = _configLoader.TryUpdate(root =>
        {
            root.DeployGuildConfig ??= new();
            root.DeployGuildConfig[guildId] = newConfig;
        });

        if (errors.Count > 0)
        {
            track.Warning("AppInteraction: apply 配置应用失败: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, ErrorCount={ErrorCount}, Errors={Errors}",
                guildId, Context.User.Id, session.SessionId, errors.Count, string.Join(" | ", errors));

            var reason = errors.Count == 1
                ? errors[0]
                : string.Join('\n', errors.Select((error, index) => $"{index + 1}. {error}"));

            await RespondAsync(components: MessagePrefab.FunctionFail(reason), ephemeral: true);
            return;
        }

        session.Complete();
        _sessionManager.TryRemove(session.SessionId);

        track.Info("AppInteraction: apply 配置应用成功: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
            guildId, Context.User.Id, session.SessionId);

        await UpdatePanelAsync(component, session);
    }

    private static Task UpdatePanelAsync(SocketMessageComponent component, ConfigEditorSession session)
        => component.UpdateAsync(msg => msg.Components = session.Render());

    private static List<ulong> ParseIds(IModalInteractionData data, string customId)
    {
        var values = data.Components.FirstOrDefault(c => c.CustomId == customId)?.Values;
        if (values is null)
            return [];

        var ids = new List<ulong>(values.Count);
        foreach (var value in values)
        {
            if (ulong.TryParse(value, out var id) && id != 0)
                ids.Add(id);
        }

        return ids;
    }

    private static bool TryParseChannelId(IModalInteractionData data, string customId, out ulong channelId)
    {
        channelId = 0;
        var value = data.Components.FirstOrDefault(c => c.CustomId == customId)?.Values?.FirstOrDefault();
        return ulong.TryParse(value, out channelId) && channelId != 0;
    }
}

/// <summary>
///     InteractionService 要求 <c>[ModalInteraction]</c> 方法的最后一个参数实现 <see cref="IModal"/>。
///     这里仅用于满足该契约；实际的字段值仍由 <see cref="ConfigEditorHandle"/> 从
///     <c>modal.Data.Components</c> 手动读取。
/// </summary>
public sealed class ConfigEditorModal : IModal
{
    public string Title => "投诉系统配置";
}