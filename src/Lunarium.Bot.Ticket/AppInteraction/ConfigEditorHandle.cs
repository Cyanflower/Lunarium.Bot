using System.Globalization;

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
        _logger = logger.ForContext("AppInteraction.ConfigEditorHandle");
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
                case "editRoles":
                    await RespondWithModalAsync(BuildRolesModal(
                        new CustomId("editor", action, session.SessionId), session.Snapshot));
                    return;

                case "editChannels":
                    await RespondWithModalAsync(BuildChannelsModal(
                        new CustomId("editor", action, session.SessionId), session.Snapshot));
                    return;

                case "editNumbers":
                    await RespondWithModalAsync(BuildNumbersModal(
                        new CustomId("editor", action, session.SessionId), session.Snapshot));
                    return;

                case "toggleTicketCd":
                    var cooldownEnabled = !session.Snapshot.EnableTicketCd;
                    session.SetEnableTicketCd(cooldownEnabled);
                    track.Info("AppInteraction: {Action} 切换投诉冷却: Enabled={Enabled}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, cooldownEnabled, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    await UpdatePanelAsync(component, session);
                    return;

                case "toggleSoothe":
                    var sootheEnabled = !session.Snapshot.EnableAutoSoothe;
                    session.SetEnableAutoSoothe(sootheEnabled);
                    track.Info("AppInteraction: {Action} 切换自动安抚: Enabled={Enabled}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, sootheEnabled, session.Context.Guild.Id, Context.User.Id, session.SessionId);
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
                case "editRoles":
                    var adminRoleIds = ParseIds(data, "rolesAdmin");
                    var ticketRoleIds = ParseIds(data, "rolesTicket");
                    session.SetAdminRoleIds(adminRoleIds);
                    session.SetAllowTicketRoleIds(ticketRoleIds);
                    track.Info("AppInteraction: {Action} 更新身份组: AdminRoleCount={AdminRoleCount}, TicketRoleCount={TicketRoleCount}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, adminRoleIds.Count, ticketRoleIds.Count, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editChannels":
                    if (!TryParseChannelId(data, "channelEntryPane", out var entryPaneChannel))
                    {
                        track.Warning("AppInteraction: {Action} 投诉入口面板频道无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择投诉入口面板频道", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    if (!TryParseChannelId(data, "channelNotifier", out var notifierChannel))
                    {
                        track.Warning("AppInteraction: {Action} 工单通知频道无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择工单通知频道", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    if (!TryParseChannelId(data, "channelLog", out var ticketLogChannel))
                    {
                        track.Warning("AppInteraction: {Action} 工单日志频道无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择工单日志频道", "### 输入无效"), ephemeral: true);
                        return;
                    }
                    if (!TryParseChannelId(data, "channelCategory", out var ticketCategory))
                    {
                        track.Warning("AppInteraction: {Action} 工单频道分组无效: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                            action, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                        await RespondAsync(components: MessagePrefab.AnyFail("请选择工单频道分组", "### 输入无效"), ephemeral: true);
                        return;
                    }

                    session.SetEntryPaneChannel(entryPaneChannel);
                    session.SetNotifierChannel(notifierChannel);
                    session.SetTicketLogChannel(ticketLogChannel);
                    session.SetTicketCategory(ticketCategory);
                    track.Info("AppInteraction: {Action} 更新频道: EntryPane={EntryPane}, Notifier={Notifier}, Log={Log}, Category={Category}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, entryPaneChannel, notifierChannel, ticketLogChannel, ticketCategory, session.Context.Guild.Id, Context.User.Id, session.SessionId);
                    break;

                case "editNumbers":
                    // 冷却/安抚关闭时与 GuildConfig.Validate 保持一致：只要求非负，不做区间校验，
                    // 否则"关闭冷却 + 冷却值 0"的配置改其他数值时会被无故拦住。
                    if (!TryParseInt(data, "numTicketCd", out var ticketCd) || ticketCd < 0
                        || (session.Snapshot.EnableTicketCd && !TicketConfigLimits.IsValidTicketCd(ticketCd)))
                    {
                        await RespondNumbersFailAsync(track, action, session, "numTicketCd",
                            session.Snapshot.EnableTicketCd
                                ? $"投诉冷却必须是 {TicketConfigLimits.TicketCdInclusiveMin}–{TicketConfigLimits.TicketCdInclusiveMax} 之间的整数"
                                : "投诉冷却必须是 0 或正整数（当前冷却已关闭）");
                        return;
                    }
                    if (!TryParseInt(data, "numAutoApprove", out var autoApproveHours) || !TicketConfigLimits.IsValidAutoApproveHours(autoApproveHours))
                    {
                        await RespondNumbersFailAsync(track, action, session, "numAutoApprove",
                            $"自动通过时间必须是 {TicketConfigLimits.AutoApproveHoursInclusiveMin}–{TicketConfigLimits.AutoApproveHoursInclusiveMax} 之间的整数（0 表示不自动通过）");
                        return;
                    }
                    if (!TryParseInt(data, "numArchiveCountdown", out var archiveCountdown) || !TicketConfigLimits.IsValidArchiveCountdown(archiveCountdown))
                    {
                        await RespondNumbersFailAsync(track, action, session, "numArchiveCountdown",
                            $"归档删除倒计时必须是 {TicketConfigLimits.ArchiveCountdownInclusiveMin}–{TicketConfigLimits.ArchiveCountdownInclusiveMax} 之间的整数（秒）");
                        return;
                    }
                    if (!TryParseInt(data, "numSootheDelay", out var sootheDelay) || sootheDelay < 0
                        || (session.Snapshot.EnableAutoSoothe && !TicketConfigLimits.IsValidSootheDelayMinutes(sootheDelay)))
                    {
                        await RespondNumbersFailAsync(track, action, session, "numSootheDelay",
                            session.Snapshot.EnableAutoSoothe
                                ? $"自动安抚延迟必须是 {TicketConfigLimits.SootheDelayMinutesInclusiveMin}–{TicketConfigLimits.SootheDelayMinutesInclusiveMax} 之间的整数（分钟）"
                                : "自动安抚延迟必须是 0 或正整数（当前自动安抚已关闭）");
                        return;
                    }

                    session.SetTicketCd(ticketCd);
                    session.SetAutoApproveHours(autoApproveHours);
                    session.SetArchiveDeleteCountdownSeconds(archiveCountdown);
                    session.SetSootheDelayMinutes(sootheDelay);
                    track.Info("AppInteraction: {Action} 更新自动化数值: TicketCd={TicketCd}, AutoApproveHours={AutoApproveHours}, ArchiveCountdown={ArchiveCountdown}, SootheDelay={SootheDelay}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
                        action, ticketCd, autoApproveHours, archiveCountdown, sootheDelay, session.Context.Guild.Id, Context.User.Id, session.SessionId);
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

        track.Info("AppInteraction: apply 开始应用配置: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, EnableTicketCd={EnableTicketCd}, TicketCd={TicketCd}, AutoApproveHours={AutoApproveHours}, EnableAutoSoothe={EnableAutoSoothe}, SootheDelayMinutes={SootheDelayMinutes}, ArchiveDeleteCountdownSeconds={ArchiveDeleteCountdownSeconds}, EntryPaneChannel={EntryPaneChannel}, NotifierChannel={NotifierChannel}, TicketLogChannel={TicketLogChannel}, TicketCategory={TicketCategory}, AdminRoleCount={AdminRoleCount}, TicketRoleCount={TicketRoleCount}",
            guildId, Context.User.Id, session.SessionId,
            newConfig.EnableTicketCd, newConfig.TicketCd, newConfig.AutoApproveHours,
            newConfig.EnableAutoSoothe, newConfig.SootheDelayMinutes, newConfig.ArchiveDeleteCountdownSeconds,
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

    /// <summary>
    /// 身份组模态窗：两个多选合并为一次提交，减少一个按钮与一次往返。
    /// </summary>
    private static Modal BuildRolesModal(CustomId customId, GuildConfig config)
    {
        return new ModalBuilder()
            .WithTitle("编辑身份组")
            .WithCustomId(customId.ToString())
            .AddSelectMenu(
                label: "允许管理工单的管理组身份组",
                customId: "rolesAdmin",
                minValues: 0,
                maxValues: 25,
                required: false,
                type: ComponentType.RoleSelect,
                defaultValues: ToRoleDefaults(config.AllowManageTicketAdminRoleIds),
                description: "拥有工单管理权限")
            .AddSelectMenu(
                label: "允许提交工单的身份组",
                customId: "rolesTicket",
                minValues: 0,
                maxValues: 25,
                required: false,
                type: ComponentType.RoleSelect,
                defaultValues: ToRoleDefaults(config.AllowTicketRoleIds),
                description: "可使用投诉入口面板")
            .Build();
    }

    /// <summary>
    /// 频道模态窗：四项频道配置合并为一次提交。
    /// </summary>
    private static Modal BuildChannelsModal(CustomId customId, GuildConfig config)
    {
        return new ModalBuilder()
            .WithTitle("编辑频道配置")
            .WithCustomId(customId.ToString())
            .AddSelectMenu(
                label: "投诉入口面板频道",
                customId: "channelEntryPane",
                minValues: 1,
                maxValues: 1,
                required: true,
                type: ComponentType.ChannelSelect,
                channelTypes: [ChannelType.Text, ChannelType.News],
                defaultValues: ToChannelDefaults(config.EntryPaneChannel),
                description: "入口面板消息发布至此")
            .AddSelectMenu(
                label: "工单通知频道",
                customId: "channelNotifier",
                minValues: 1,
                maxValues: 1,
                required: true,
                type: ComponentType.ChannelSelect,
                channelTypes: [ChannelType.Text, ChannelType.News],
                defaultValues: ToChannelDefaults(config.NotifierChannel),
                description: "安抚等系统通知发送至此")
            .AddSelectMenu(
                label: "工单日志频道",
                customId: "channelLog",
                minValues: 1,
                maxValues: 1,
                required: true,
                type: ComponentType.ChannelSelect,
                channelTypes: [ChannelType.Text, ChannelType.News],
                defaultValues: ToChannelDefaults(config.TicketLogChannel),
                description: "状态卡片与操作记录发送至此")
            .AddSelectMenu(
                label: "工单频道分组",
                customId: "channelCategory",
                minValues: 1,
                maxValues: 1,
                required: true,
                type: ComponentType.ChannelSelect,
                channelTypes: [ChannelType.Category],
                defaultValues: ToChannelDefaults(config.TicketCategory),
                description: "新建工单频道放置于此")
            .Build();
    }

    /// <summary>
    /// 数值模态窗：四项时间/数值配置合并为一次提交（Discord 上限 5 个组件）。
    /// </summary>
    private static Modal BuildNumbersModal(CustomId customId, GuildConfig config)
    {
        return new ModalBuilder()
            .WithTitle("编辑自动化数值")
            .WithCustomId(customId.ToString())
            .AddTextInput(
                label: "投诉冷却（小时）",
                customId: "numTicketCd",
                style: TextInputStyle.Short,
                minLength: 1,
                maxLength: 3,
                required: true,
                value: config.TicketCd.ToString(CultureInfo.InvariantCulture),
                description: $"{TicketConfigLimits.TicketCdInclusiveMin}–{TicketConfigLimits.TicketCdInclusiveMax}，冷却关闭时忽略")
            .AddTextInput(
                label: "自动通过（小时）",
                customId: "numAutoApprove",
                style: TextInputStyle.Short,
                minLength: 1,
                maxLength: 3,
                required: true,
                value: config.AutoApproveHours.ToString(CultureInfo.InvariantCulture),
                description: $"{TicketConfigLimits.AutoApproveHoursInclusiveMin}–{TicketConfigLimits.AutoApproveHoursInclusiveMax}，0 表示不自动通过")
            .AddTextInput(
                label: "归档删除倒计时（秒）",
                customId: "numArchiveCountdown",
                style: TextInputStyle.Short,
                minLength: 1,
                maxLength: 3,
                required: true,
                value: config.ArchiveDeleteCountdownSeconds.ToString(CultureInfo.InvariantCulture),
                description: $"{TicketConfigLimits.ArchiveCountdownInclusiveMin}–{TicketConfigLimits.ArchiveCountdownInclusiveMax} 秒")
            .AddTextInput(
                label: "自动安抚延迟（分钟）",
                customId: "numSootheDelay",
                style: TextInputStyle.Short,
                minLength: 1,
                maxLength: 5,
                required: true,
                value: config.SootheDelayMinutes.ToString(CultureInfo.InvariantCulture),
                description: $"{TicketConfigLimits.SootheDelayMinutesInclusiveMin}–{TicketConfigLimits.SootheDelayMinutesInclusiveMax}，自动通过后计时")
            .Build();
    }

    private static SelectMenuDefaultValue[] ToRoleDefaults(IEnumerable<ulong> ids)
        => ids.Select(id => new SelectMenuDefaultValue(id, SelectDefaultValueType.Role)).ToArray();

    private static SelectMenuDefaultValue[]? ToChannelDefaults(ulong channelId)
        => channelId == 0
            ? null
            : [new SelectMenuDefaultValue(channelId, SelectDefaultValueType.Channel)];

    /// <summary>数值模态窗校验失败时的统一回执。</summary>
    private async Task RespondNumbersFailAsync(
        TrackScope track,
        string action,
        ConfigEditorSession session,
        string field,
        string message)
    {
        track.Warning("AppInteraction: {Action} 数值输入无效: Field={Field}, GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
            action, field, session.Context.Guild.Id, Context.User.Id, session.SessionId);
        await RespondAsync(components: MessagePrefab.AnyFail(message, "### 输入无效"), ephemeral: true);
    }

    private static bool TryParseInt(IModalInteractionData data, string customId, out int value)
    {
        value = 0;
        var raw = data.Components.FirstOrDefault(c => c.CustomId == customId)?.Value;
        return int.TryParse(raw, out value);
    }

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