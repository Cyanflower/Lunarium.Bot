using System.Globalization;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Archiver;
using Lunarium.Bot.Archive.InteractionSession;
using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Common.UID;
using Lunarium.Logging;

namespace Lunarium.Bot.Archive.AppInteractionService;

/// <summary>归档配置面板与全局间隔设置。</summary>
[Group("维护", "归档维护配置命令")]
public sealed class ArchiveMaintenanceCommands : InteractionModuleBase<SocketInteractionContext>
{
    private const string EditConfigCommand = "编辑配置";
    private const string ResetIntervalCommand = "重设间隔";

    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly SessionManager _sessionManager;
    private readonly ArchiverManager _archiverManager;

    public ArchiveMaintenanceCommands(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        SessionManager sessionManager,
        ArchiverManager archiverManager)
    {
        _logger = logger.ForContext("AppInteraction.ArchiveMaintenance");
        _configLoader = configLoader;
        _sessionManager = sessionManager;
        _archiverManager = archiverManager;
    }

    [SlashCommand(EditConfigCommand, "添加或修改 Bot 在本服务器的归档配置")]
    public async Task EditConfigAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            await DeferAsync();

            if (Context.Guild is not SocketGuild guild)
            {
                await FollowupAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"));
                return;
            }

            var isNew = !_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var existing);
            var bypassIds = BuildBypassSet();

            MessageComponent? failure;
            if (isNew)
            {
                // 新建配置只能由部署者/维护者发起；没有本服务器 StaffRoleIds 可用
                failure = await RuleEngine.ValidateAsync(
                    Context,
                    new BaseRuleSet.RequireAdminUser(bypassIds));
            }
            else
            {
                failure = await RuleEngine.ValidateAsync(
                    Context,
                    new BaseRuleSet.MaintainerBypass(bypassIds),
                    new BaseRuleSet.RequireRole(existing!.StaffRoleIds.ToHashSet()));
            }

            if (failure is not null)
            {
                track.Warning("{Command} 权限校验未通过: GuildId={GuildId}, UserId={UserId}",
                    EditConfigCommand, guild.Id, Context.User.Id);
                await FollowupAsync(components: failure);
                return;
            }

            var config = isNew ? new GuildConfig() : existing!.GetDeepCopy();
            var context = new ArchiveConfigEditorContext(
                config,
                guild,
                isNew,
                _configLoader.Value.IntervalMin);

            var session = _sessionManager.Register<ArchiveConfigEditorSession>(
                context,
                Context.User.Id.ToString());

            track.Info("{Command} 已创建配置编辑会话: GuildId={GuildId}, UserId={UserId}, IsNew={IsNew}, SessionId={SessionId}",
                EditConfigCommand, guild.Id, Context.User.Id, isNew, session.SessionId);

            await FollowupAsync(components: session.Render());
        }
        catch (Exception ex)
        {
            track.Error(ex, "{Command} 打开配置面板失败: GuildId={GuildId}, UserId={UserId}",
                EditConfigCommand, Context.Guild?.Id, Context.User.Id);
        }
    }

    [SlashCommand(ResetIntervalCommand, "重新设置全局归档间隔（分钟，默认 10）")]
    public async Task ResetIntervalAsync(
        [Summary("分钟", "新的归档间隔（5-1440），不填恢复默认 10")] int? minutes = null)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            await DeferAsync(ephemeral: true);

            var interval = minutes ?? ArchiveConfigLimits.DefaultIntervalMin;

            if (!_configLoader.Value.DeployerConfig.AllowModifyByInteraction)
            {
                await FollowupAsync(
                    components: MessagePrefab.FunctionFail("当前配置不允许通过交互修改"),
                    ephemeral: true);
                return;
            }

            if (!ArchiveConfigLimits.IsValidInterval(interval))
            {
                await FollowupAsync(
                    components: MessagePrefab.AnyFail(
                        $"归档间隔必须在 {ArchiveConfigLimits.IntervalInclusiveMin}–{ArchiveConfigLimits.IntervalInclusiveMax} 分钟之间",
                        "### 输入无效"),
                    ephemeral: true);
                return;
            }

            var failure = await RuleEngine.ValidateAsync(
                Context,
                new BaseRuleSet.RequireAdminUser(BuildBypassSet()));

            if (failure is not null)
            {
                track.Warning("{Command} 权限校验未通过: UserId={UserId}", ResetIntervalCommand, Context.User.Id);
                await FollowupAsync(components: failure, ephemeral: true);
                return;
            }

            var errors = _configLoader.TryUpdate(root => root.IntervalMin = interval);
            if (errors.Count > 0)
            {
                track.Warning("{Command} 配置写回失败: UserId={UserId}, Errors={Errors}",
                    ResetIntervalCommand, Context.User.Id, string.Join(" | ", errors));

                await FollowupAsync(
                    components: MessagePrefab.FunctionFail(string.Join('\n', errors)),
                    ephemeral: true);
                return;
            }

            _archiverManager.UpdateInterval(interval);

            track.Info("{Command} 全局归档间隔已更新: UserId={UserId}, Interval={Interval}",
                ResetIntervalCommand, Context.User.Id, interval);

            await FollowupAsync(
                components: MessagePrefab.OperateSuccess($"全局归档间隔已设置为 {interval} 分钟"),
                ephemeral: true);
        }
        catch (Exception ex)
        {
            track.Error(ex, "{Command} 设置归档间隔失败: UserId={UserId}", ResetIntervalCommand, Context.User.Id);
            await FollowupAsync(
                components: MessagePrefab.FunctionFail("设置归档间隔时发生内部错误"),
                ephemeral: true);
        }
    }

    // 本模块带 [Group("维护")]，Discord.Net 默认会把组名拼进组件/模态命令路径，
    // 而查找时只用原始 customId → 必须 ignoreGroupNames 才能匹配。
    [ComponentInteraction("archive-editor:*:*", ignoreGroupNames: true)]
    public async Task EditorHandleAsync(string action, string sessionId)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (Context.Interaction is not SocketMessageComponent component)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是消息组件交互"), ephemeral: true);
                return;
            }

            if (!TryGetSession(track, action, sessionId, out var session, out var error))
            {
                await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
                return;
            }

            switch (action)
            {
                case "edit":
                    await RespondWithModalAsync(BuildEditModal(
                        new CustomId("archive-editor", action, session.SessionId),
                        session.Snapshot));
                    return;

                case "apply":
                    await ApplyAsync(track, component, session);
                    return;

                case "cancel":
                    session.Cancel();
                    _sessionManager.TryRemove(session.SessionId);
                    await component.UpdateAsync(message => message.Components = session.Render());
                    return;

                default:
                    track.Warning("{Action} 收到未注册的配置面板操作: UserId={UserId}, SessionId={SessionId}",
                        action, Context.User.Id, sessionId);
                    await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Action: {action}"), ephemeral: true);
                    return;
            }
        }
        catch (Exception ex)
        {
            track.Error(ex, "处理归档配置面板交互失败: Action={Action}, UserId={UserId}, SessionId={SessionId}",
                action, Context.User.Id, sessionId);
        }
    }

    [ModalInteraction("archive-editor:*:*", ignoreGroupNames: true)]
    public async Task EditorModalHandleAsync(string action, string sessionId, ArchiveConfigEditorModal modalShell)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (Context.Interaction is not SocketModal modal)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
                return;
            }

            if (!string.Equals(action, "edit", StringComparison.Ordinal))
            {
                await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Modal Action: {action}"), ephemeral: true);
                return;
            }

            if (!TryGetSession(track, action, sessionId, out var session, out var error))
            {
                await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
                return;
            }

            var data = modal.Data;

            if (!TryParseInt(data, "archiveLimit", out var archiveLimit)
                || !ArchiveConfigLimits.IsValidLimit(archiveLimit))
            {
                await RespondAsync(
                    components: MessagePrefab.AnyFail(
                        $"活跃线程上限必须是 {ArchiveConfigLimits.LimitInclusiveMin}–{ArchiveConfigLimits.LimitExclusiveMax - 1} 之间的整数",
                        "### 输入无效"),
                    ephemeral: true);
                return;
            }

            var staffRoles = ParseIds(data, "staffRoles");

            session.SetArchiveLimit(archiveLimit);
            session.SetStaffRoles(staffRoles);

            track.Info("归档配置模态窗已更新: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, Limit={Limit}, StaffCount={StaffCount}",
                session.Context.Guild.Id, Context.User.Id, session.SessionId,
                archiveLimit, staffRoles.Count);

            await modal.UpdateAsync(message => message.Components = session.Render());
        }
        catch (Exception ex)
        {
            track.Error(ex, "处理归档配置模态窗失败: Action={Action}, UserId={UserId}, SessionId={SessionId}",
                action, Context.User.Id, sessionId);
        }
    }

    private async Task ApplyAsync(TrackScope track, SocketMessageComponent component, ArchiveConfigEditorSession session)
    {
        if (!_configLoader.Value.DeployerConfig.AllowModifyByInteraction)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前配置不允许通过交互修改"), ephemeral: true);
            return;
        }

        var guildId = session.Context.Guild.Id;
        var newConfig = session.BuildConfig();

        var errors = _configLoader.TryUpdate(root =>
        {
            root.DeployGuildConfig[guildId] = newConfig;
        });

        if (errors.Count > 0)
        {
            track.Warning("归档配置应用失败: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}, Errors={Errors}",
                guildId, Context.User.Id, session.SessionId, string.Join(" | ", errors));

            await RespondAsync(
                components: MessagePrefab.FunctionFail(string.Join('\n', errors)),
                ephemeral: true);
            return;
        }

        session.Complete();
        _sessionManager.TryRemove(session.SessionId);

        track.Info("归档配置应用成功: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}",
            guildId, Context.User.Id, session.SessionId);

        await component.UpdateAsync(message => message.Components = session.Render());
    }

    private bool TryGetSession(
        TrackScope track,
        string action,
        string sessionId,
        out ArchiveConfigEditorSession session,
        out string error)
    {
        session = null!;
        error = string.Empty;

        var parsed = HasPrefixSessionId.TryParse(sessionId);
        if (parsed is null || !ulong.TryParse(parsed.Prefix, out var userId))
        {
            track.Error("{Action} 未能解析会话前缀: SessionId={SessionId}, UserId={UserId}",
                action, sessionId, Context.User.Id);
            error = "未能解析会话前缀";
            return false;
        }

        if (userId != Context.User.Id)
        {
            track.Warning("{Action} 非交互创建者不可操作: SessionId={SessionId}, UserId={UserId}, OwnerId={OwnerId}",
                action, sessionId, Context.User.Id, userId);
            error = "非交互创建者不可操作";
            return false;
        }

        if (!_sessionManager.TryGet<ArchiveConfigEditorSession>(sessionId, out var found) || found is null)
        {
            track.Warning("{Action} 配置会话不存在或已过期: SessionId={SessionId}, UserId={UserId}",
                action, sessionId, Context.User.Id);
            error = "交互已过期";
            return false;
        }

        session = found;
        return true;
    }

    private HashSet<ulong> BuildBypassSet()
        => _configLoader.Value.DeployerConfig.DeployerIds
            .Append(_configLoader.Value.DeployerConfig.MaintainerId)
            .ToHashSet();

    private static Modal BuildEditModal(CustomId customId, GuildConfig config)
    {
        return new ModalBuilder()
            .WithTitle("编辑归档配置")
            .WithCustomId(customId.ToString())
            .AddTextInput(
                label: "活跃线程上限",
                customId: "archiveLimit",
                style: TextInputStyle.Short,
                minLength: 1,
                maxLength: 3,
                required: true,
                value: config.ArchiveLimit.ToString(CultureInfo.InvariantCulture),
                description: $"{ArchiveConfigLimits.LimitInclusiveMin}–{ArchiveConfigLimits.LimitExclusiveMax - 1}")
            .AddSelectMenu(
                label: "归档管理身份组",
                customId: "staffRoles",
                minValues: 0,
                maxValues: 25,
                required: false,
                type: ComponentType.RoleSelect,
                defaultValues: ToRoleDefaults(config.StaffRoleIds),
                description: "可修改本服务器的归档配置")
            .Build();
    }

    private static SelectMenuDefaultValue[] ToRoleDefaults(IEnumerable<ulong> ids)
        => ids.Select(id => new SelectMenuDefaultValue(id, SelectDefaultValueType.Role)).ToArray();

    private static bool TryParseInt(IModalInteractionData data, string customId, out int value)
    {
        value = 0;
        var raw = data.Components.FirstOrDefault(component => component.CustomId == customId)?.Value;
        return int.TryParse(raw, out value);
    }

    private static List<ulong> ParseIds(IModalInteractionData data, string customId)
    {
        var values = data.Components.FirstOrDefault(component => component.CustomId == customId)?.Values;
        if (values is null)
        {
            return [];
        }

        var ids = new List<ulong>(values.Count);
        foreach (var value in values)
        {
            if (ulong.TryParse(value, out var id) && id != 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}

/// <summary>InteractionService 要求 ModalInteraction 最后一个参数实现 IModal。</summary>
public sealed class ArchiveConfigEditorModal : IModal
{
    public string Title => "归档配置";
}
