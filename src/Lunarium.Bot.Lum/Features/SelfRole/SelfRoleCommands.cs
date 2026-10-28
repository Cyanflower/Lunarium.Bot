using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Bot.Lum.Features.Shared;
using Lunarium.Bot.Lum.Model;
using Lunarium.Logging;

namespace Lunarium.Bot.Lum.Features.SelfRole;

/// <summary>
/// 自助身份组：<c>/自助身份组</c> 打开发布面板，
/// 发布出的消息带两个固定 customId 的按钮（<c>selfrole:grant:{roleId}</c> / <c>selfrole:revoke:{roleId}</c>）。
/// </summary>
public sealed class SelfRoleCommands : InteractionModuleBase<SocketInteractionContext>
{
    private const string Scope = "rolepanel";

    private readonly ILogger _logger;
    private readonly SessionManager _sessionManager;
    private readonly ConfigLoader<LumConfig> _configLoader;
    private readonly DiscordSocketClient _client;

    public SelfRoleCommands(
        ILogger logger,
        SessionManager sessionManager,
        ConfigLoader<LumConfig> configLoader,
        DiscordSocketClient client)
    {
        _logger = logger.ForContext("SelfRole");
        _sessionManager = sessionManager;
        _configLoader = configLoader;
        _client = client;
    }

    [SlashCommand("自助身份组", "发布一个自助领取/放弃身份组的面板")]
    public Task PublishPanelAsync(
        [Summary("频道", "目标频道或帖子，留空则为当前频道")]
        [ChannelTypes(ChannelType.Text, ChannelType.News, ChannelType.NewsThread, ChannelType.PublicThread, ChannelType.PrivateThread)]
        IChannel? channel = null)
        => OpenPanelAsync((channel ?? Context.Channel).Id);

    [ComponentInteraction("rolepanel:*:*")]
    public async Task HandleComponentAsync(string action, string sessionId)
    {
        if (Context.Interaction is not SocketMessageComponent component)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是消息组件交互"), ephemeral: true);
            return;
        }

        if (!SessionLookup.TryGet(_sessionManager, Context, sessionId, out SelfRoleSession session, out var error))
        {
            _logger.Info("自助身份组面板交互被拒绝: Action={Action}, SessionId={SessionId}, UserId={UserId}, Reason={Reason}",
                action, sessionId, Context.User.Id, error);
            await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
            return;
        }

        switch (action)
        {
            case "editRole":
                await RespondWithModalAsync(ModalWindowPrefab.MultiRoleSelectField(
                    new CustomId(Scope, action, session.SessionId),
                    title: "选择身份组",
                    label: "身份组",
                    description: "发布后，成员可通过面板按钮领取或放弃该身份组",
                    minValues: 1,
                    maxValues: 1,
                    required: true));
                return;

            case "editTitle":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑标题", label: "标题（可留空）",
                    value: session.Title, style: TextInputStyle.Short, maxLength: 200, required: false,
                    placeholder: "不填则不显示标题",
                    description: "卡片顶部，独立成段并自动追加分隔线"));
                return;

            case "editColor":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑颜色", label: "颜色（#RRGGBB）",
                    value: $"#{session.Color.RawValue:X6}", style: TextInputStyle.Short, maxLength: 7, required: true,
                    placeholder: "#5865F2",
                    description: "卡片左侧色条，默认蓝色"));
                return;

            case "editContent":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑内容", label: "内容",
                    value: session.Content, style: TextInputStyle.Paragraph, maxLength: 4000, required: true,
                    placeholder: "说明这个身份组的作用",
                    description: "卡片正文，支持 Markdown"));
                return;

            case "editFooter":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑页脚", label: "页脚（可留空）",
                    value: session.Footer, style: TextInputStyle.Short, maxLength: 200, required: false,
                    placeholder: "不填则不显示页脚",
                    description: "以小字显示在卡片底部"));
                return;

            case "editLabels":
                await RespondWithModalAsync(BuildLabelsModal(session, action));
                return;

            case "cancel":
                session.Cancel();
                _sessionManager.TryRemove(session.SessionId);
                await component.UpdateAsync(msg => msg.Components = session.Render());
                return;

            case "submit":
                await SubmitAsync(session);
                return;

            default:
                _logger.Warning("收到未注册的自助身份组面板 Action: Action={Action}, UserId={UserId}, SessionId={SessionId}",
                    action, Context.User.Id, sessionId);
                await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Action: {action}"), ephemeral: true);
                return;
        }
    }

    [ModalInteraction("rolepanel:editRole:*")]
    public async Task EditRoleModalAsync(string sessionId, RoleSelectModal modal)
    {
        if (Context.Interaction is not SocketModal socketModal)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
            return;
        }

        if (!SessionLookup.TryGet(_sessionManager, Context, sessionId, out SelfRoleSession session, out var error))
        {
            await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
            return;
        }

        if (Context.Guild is null)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("只能在服务器内使用", "### 输入无效"), ephemeral: true);
            return;
        }

        var roleId = modal.Role?.Id ?? 0;
        if (roleId == 0)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("请选择一个有效身份组", "### 输入无效"), ephemeral: true);
            return;
        }

        if (!RoleGuard.CanAssign(Context.Guild, Context.Guild.GetRole(roleId), out var reason))
        {
            await RespondAsync(components: MessagePrefab.AnyFail(reason, "### 身份组不可用"), ephemeral: true);
            return;
        }

        session.SetRole(roleId);

        await socketModal.UpdateAsync(msg => msg.Components = session.Render());
    }

    [ModalInteraction("rolepanel:editLabels:*")]
    public async Task EditLabelsModalAsync(string sessionId, SelfRoleLabelsModal modal)
    {
        if (Context.Interaction is not SocketModal socketModal)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
            return;
        }

        if (!SessionLookup.TryGet(_sessionManager, Context, sessionId, out SelfRoleSession session, out var error))
        {
            await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
            return;
        }

        session.SetLabels(modal.Grant, modal.Revoke);

        await socketModal.UpdateAsync(msg => msg.Components = session.Render());
    }

    [ModalInteraction("rolepanel:editTitle:*")]
    public Task EditTitleModalAsync(string sessionId, TextInputModal modal)
        => HandleTextModalAsync("editTitle", sessionId, modal);

    [ModalInteraction("rolepanel:editContent:*")]
    public Task EditContentModalAsync(string sessionId, TextInputModal modal)
        => HandleTextModalAsync("editContent", sessionId, modal);

    [ModalInteraction("rolepanel:editFooter:*")]
    public Task EditFooterModalAsync(string sessionId, TextInputModal modal)
        => HandleTextModalAsync("editFooter", sessionId, modal);

    [ModalInteraction("rolepanel:editColor:*")]
    public Task EditColorModalAsync(string sessionId, TextInputModal modal)
        => HandleTextModalAsync("editColor", sessionId, modal);

    private async Task HandleTextModalAsync(string action, string sessionId, TextInputModal modal)
    {
        if (Context.Interaction is not SocketModal socketModal)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
            return;
        }

        if (!SessionLookup.TryGet(_sessionManager, Context, sessionId, out SelfRoleSession session, out var error))
        {
            await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
            return;
        }

        switch (action)
        {
            case "editTitle":
                session.SetTitle(modal.Text);
                break;

            case "editContent":
                session.SetContent(modal.Text);
                break;

            case "editFooter":
                session.SetFooter(modal.Text);
                break;

            case "editColor":
                if (!InteractionInputs.TryParseColor(modal.Text ?? string.Empty, out var color))
                {
                    await RespondAsync(components: MessagePrefab.AnyFail(
                        "颜色格式应为 #RRGGBB，例如 #5865F2", "### 输入无效"), ephemeral: true);
                    return;
                }
                session.SetColor(color);
                break;
        }

        await socketModal.UpdateAsync(msg => msg.Components = session.Render());
    }

    [ComponentInteraction("selfrole:grant:*")]
    public Task GrantRoleAsync(ulong roleId) => ChangeRoleAsync(roleId, grant: true);

    [ComponentInteraction("selfrole:revoke:*")]
    public Task RevokeRoleAsync(ulong roleId) => ChangeRoleAsync(roleId, grant: false);

    private async Task ChangeRoleAsync(ulong roleId, bool grant)
    {
        if (Context.Guild is null || Context.User is not SocketGuildUser user)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("只能在服务器内使用"), ephemeral: true);
            return;
        }

        var role = Context.Guild.GetRole(roleId);
        if (!RoleGuard.CanAssign(Context.Guild, role, out var reason))
        {
            _logger.Warning("自助身份组不可分配: GuildId={GuildId}, RoleId={RoleId}, UserId={UserId}, Reason={Reason}",
                Context.Guild.Id, roleId, user.Id, reason);
            await RespondAsync(components: MessagePrefab.AnyFail(reason, "### 身份组不可用"), ephemeral: true);
            return;
        }

        var hasRole = user.Roles.Any(r => r.Id == roleId);

        try
        {
            if (grant)
            {
                if (hasRole)
                {
                    await RespondAsync(components: MessagePrefab.AnyNotifier($"你已经在 {role!.Mention} 里了", "### 无需重复领取"), ephemeral: true);
                    return;
                }

                await user.AddRoleAsync(role!);
                _logger.Info("自助领取身份组成功: GuildId={GuildId}, RoleId={RoleId}, UserId={UserId}", Context.Guild.Id, roleId, user.Id);
                await RespondAsync(components: MessagePrefab.OperateSuccess($"已获得身份组 {role!.Mention}"), ephemeral: true);
                return;
            }

            if (!hasRole)
            {
                await RespondAsync(components: MessagePrefab.AnyNotifier($"你当前没有 {role!.Mention}", "### 无需放弃"), ephemeral: true);
                return;
            }

            await user.RemoveRoleAsync(role!);
            _logger.Info("自助放弃身份组成功: GuildId={GuildId}, RoleId={RoleId}, UserId={UserId}", Context.Guild.Id, roleId, user.Id);
            await RespondAsync(components: MessagePrefab.OperateSuccess($"已放弃身份组 {role!.Mention}"), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "自助身份组操作失败: GuildId={GuildId}, RoleId={RoleId}, UserId={UserId}, Grant={Grant}",
                Context.Guild.Id, roleId, user.Id, grant);

            await RespondAsync(components: MessagePrefab.FunctionFail("身份组操作失败，请稍后再试"), ephemeral: true);
        }
    }

    private async Task OpenPanelAsync(ulong channelId)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("只能在服务器内使用"), ephemeral: true);
            return;
        }

        var permissionFailure = await ValidatePermissionAsync(Context.Guild);
        if (permissionFailure is not null)
        {
            await RespondAsync(components: permissionFailure, ephemeral: true);
            return;
        }

        if (await _client.GetChannelAsync(channelId) is not IMessageChannel)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("目标频道不存在或不支持消息", "### 目标无效"), ephemeral: true);
            return;
        }

        var session = _sessionManager.Register<SelfRoleSession>(
            new SelfRoleContext(channelId),
            Context.User.Id.ToString());

        _logger.Info("自助身份组面板已创建: ChannelId={ChannelId}, UserId={UserId}, SessionId={SessionId}",
            channelId, Context.User.Id, session.SessionId);

        await RespondAsync(components: session.Render(), ephemeral: true);
    }

    private async Task SubmitAsync(SelfRoleSession session)
    {
        if (string.IsNullOrWhiteSpace(session.Content))
        {
            await RespondAsync(components: MessagePrefab.AnyFail("内容不能为空", "### 无法发布"), ephemeral: true);
            return;
        }

        if (session.RoleId == 0)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("请先选择身份组", "### 无法发布"), ephemeral: true);
            return;
        }

        if (Context.Guild is null)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("只能在服务器内使用", "### 无法发布"), ephemeral: true);
            return;
        }

        if (!RoleGuard.CanAssign(Context.Guild, Context.Guild.GetRole(session.RoleId), out var reason))
        {
            await RespondAsync(components: MessagePrefab.AnyFail(reason, "### 身份组不可用"), ephemeral: true);
            return;
        }

        // 发送可能超过交互的 3 秒窗口，先 defer 再改原消息
        await DeferAsync();

        try
        {
            var channel = await _client.GetChannelAsync(session.Context.ChannelId) as IMessageChannel
                ?? throw new InvalidOperationException("目标频道不存在或不支持消息");

            var sent = await channel.SendMessageAsync(components: session.BuildPanelCard());

            session.Complete(sent.GetJumpUrl());
            _sessionManager.TryRemove(session.SessionId);

            _logger.Info("自助身份组面板已发布: GuildId={GuildId}, ChannelId={ChannelId}, MessageId={MessageId}, RoleId={RoleId}, UserId={UserId}",
                Context.Guild.Id, channel.Id, sent.Id, session.RoleId, Context.User.Id);

            await ModifyOriginalResponseAsync(msg => msg.Components = session.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "发布自助身份组面板失败: ChannelId={ChannelId}, RoleId={RoleId}, UserId={UserId}",
                session.Context.ChannelId, session.RoleId, Context.User.Id);

            await ModifyOriginalResponseAsync(msg => msg.Components = MessagePrefab.FunctionFail(ex.Message));
        }
    }

    private async Task<MessageComponent?> ValidatePermissionAsync(SocketGuild guild)
    {
        var deployerIds = _configLoader.Value.DeployerConfig.DeployerIds.ToHashSet();

        var staffIds = _configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var guildConfig)
            ? guildConfig.StaffIds.ToHashSet()
            : new HashSet<ulong>();

        return await RuleEngine.ValidateAsync(
            Context,
            new BaseRuleSet.MaintainerBypass(deployerIds),
            new BaseRuleSet.RequireRole(staffIds));
    }

    private static Modal BuildLabelsModal(SelfRoleSession session, string action)
    {
        return new ModalBuilder()
            .WithTitle("编辑按钮文字")
            .WithCustomId(new CustomId(Scope, action, session.SessionId).ToString())
            .AddTextInput(
                label: "领取按钮文字",
                customId: "grant",
                style: TextInputStyle.Short,
                maxLength: 80,
                required: true,
                value: session.GrantLabel,
                description: "最多 80 字")
            .AddTextInput(
                label: "放弃按钮文字",
                customId: "revoke",
                style: TextInputStyle.Short,
                maxLength: 80,
                required: true,
                value: session.RevokeLabel,
                description: "最多 80 字")
            .Build();
    }
}

/// <summary><c>rolepanel:editRole</c> 模态窗的提交数据。</summary>
public sealed class RoleSelectModal : IModal
{
    public string Title => "选择身份组";

    [ModalRoleSelect("roles", 1, 1)]
    public IRole? Role { get; set; }
}

/// <summary><c>rolepanel:editLabels</c> 模态窗的提交数据。</summary>
public sealed class SelfRoleLabelsModal : IModal
{
    public string Title => "编辑按钮文字";

    [ModalTextInput("grant")]
    public string? Grant { get; set; }

    [ModalTextInput("revoke")]
    public string? Revoke { get; set; }
}
