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

namespace Lunarium.Bot.Lum.Features.MessageComposer;

/// <summary>
/// 消息面板入口：<c>/发消息</c>、<c>/改消息</c>、右键「编辑 bot 消息」，
/// 以及面板上的按钮与模态窗。
/// </summary>
public sealed class MessageComposerCommands : InteractionModuleBase<SocketInteractionContext>
{
    private const string Scope = "composer";
    private const string LayoutCard = "card";
    private const string LayoutPlain = "plain";

    private readonly ILogger _logger;
    private readonly SessionManager _sessionManager;
    private readonly ConfigLoader<LumConfig> _configLoader;
    private readonly DiscordSocketClient _client;

    public MessageComposerCommands(
        ILogger logger,
        SessionManager sessionManager,
        ConfigLoader<LumConfig> configLoader,
        DiscordSocketClient client)
    {
        _logger = logger.ForContext("MessageComposer");
        _sessionManager = sessionManager;
        _configLoader = configLoader;
        _client = client;
    }

    [SlashCommand("发消息", "以 bot 身份发送一条消息")]
    public Task SendMessageAsync(
        [Summary("频道", "目标频道或帖子，留空则为当前频道")]
        [ChannelTypes(ChannelType.Text, ChannelType.News, ChannelType.NewsThread, ChannelType.PublicThread, ChannelType.PrivateThread)]
        IChannel? channel = null,
        [Summary("布局", "消息形态")]
        [Choice("卡片", LayoutCard)]
        [Choice("纯文本", LayoutPlain)]
        string layout = LayoutCard)
        => OpenPanelAsync(ComposerMode.Send, (channel ?? Context.Channel).Id, 0, ParseLayout(layout));

    [SlashCommand("改消息", "原地全量替换一条 bot 消息")]
    public async Task EditMessageAsync(
        [Summary("消息", "消息链接或消息 ID")] string message,
        [Summary("布局", "消息形态（原消息内容不会被读取）")]
        [Choice("卡片", LayoutCard)]
        [Choice("纯文本", LayoutPlain)]
        string layout = LayoutCard)
    {
        if (!TryParseMessageReference(message, out var channelId, out var messageId))
        {
            await RespondAsync(components: MessagePrefab.AnyFail("无法解析消息链接或消息 ID", "### 输入无效"), ephemeral: true);
            return;
        }

        await OpenPanelAsync(
            ComposerMode.Edit,
            channelId == 0 ? Context.Channel.Id : channelId,
            messageId,
            ParseLayout(layout));
    }

    [MessageCommand("编辑 bot 消息")]
    public Task EditBotMessageAsync(IMessage message)
    {
        // 带组件的消息按卡片打开，纯文本消息按纯文本打开；内容一律不读取
        var layout = message.Components.Count > 0 ? ComposerLayout.Card : ComposerLayout.Plain;

        return OpenPanelAsync(ComposerMode.Edit, message.Channel.Id, message.Id, layout);
    }

    [ComponentInteraction("composer:*:*")]
    public async Task HandleComponentAsync(string action, string sessionId)
    {
        if (Context.Interaction is not SocketMessageComponent component)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是消息组件交互"), ephemeral: true);
            return;
        }

        if (!SessionLookup.TryGet(_sessionManager, Context, sessionId, out MessageComposerSession session, out var error))
        {
            await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
            return;
        }

        switch (action)
        {
            case "editContent":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑内容", label: "消息内容",
                    value: session.Content, style: TextInputStyle.Paragraph, maxLength: 4000, required: true,
                    placeholder: "支持 Markdown",
                    description: "卡片正文 / 纯文本正文"));
                return;

            case "editTitle":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑标题", label: "标题（可留空）",
                    value: session.Title, style: TextInputStyle.Short, maxLength: 200, required: false,
                    placeholder: "不填则不显示标题",
                    description: "卡片顶部，独立成段并自动追加分隔线"));
                return;

            case "editFooter":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑页脚", label: "页脚（可留空）",
                    value: session.Footer, style: TextInputStyle.Short, maxLength: 200, required: false,
                    placeholder: "不填则不显示页脚",
                    description: "以小字显示在卡片底部"));
                return;

            case "editColor":
                await RespondWithModalAsync(InteractionInputs.TextModal(
                    Scope, action, session.SessionId,
                    title: "编辑颜色", label: "颜色（#RRGGBB）",
                    value: $"#{session.Color.RawValue:X6}", style: TextInputStyle.Short, maxLength: 7, required: true,
                    placeholder: "#5865F2",
                    description: "卡片左侧色条，默认蓝色"));
                return;

            case "toggleLayout":
                session.ToggleLayout();
                await component.UpdateAsync(msg => msg.Components = session.Render());
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
                _logger.Warning("收到未注册的面板 Action: Action={Action}, UserId={UserId}, SessionId={SessionId}",
                    action, Context.User.Id, sessionId);
                await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Action: {action}"), ephemeral: true);
                return;
        }
    }

    [ModalInteraction("composer:*:*")]
    public async Task HandleModalAsync(string action, string sessionId, TextInputModal modal)
    {
        if (Context.Interaction is not SocketModal socketModal)
        {
            await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
            return;
        }

        if (!SessionLookup.TryGet(_sessionManager, Context, sessionId, out MessageComposerSession session, out var error))
        {
            await RespondAsync(components: MessagePrefab.AuthFail(error), ephemeral: true);
            return;
        }

        var raw = modal.Text ?? string.Empty;

        switch (action)
        {
            case "editContent":
                session.SetContent(raw);
                break;

            case "editTitle":
                session.SetTitle(raw);
                break;

            case "editFooter":
                session.SetFooter(raw);
                break;

            case "editColor":
                if (!InteractionInputs.TryParseColor(raw, out var color))
                {
                    await RespondAsync(components: MessagePrefab.AnyFail(
                        "颜色格式应为 #RRGGBB，例如 #5865F2", "### 输入无效"), ephemeral: true);
                    return;
                }
                session.SetColor(color);
                break;

            default:
                _logger.Warning("收到未注册的模态 Action: Action={Action}, UserId={UserId}, SessionId={SessionId}",
                    action, Context.User.Id, sessionId);
                await RespondAsync(components: MessagePrefab.FunctionFail($"未注册的 Action: {action}"), ephemeral: true);
                return;
        }

        await socketModal.UpdateAsync(msg => msg.Components = session.Render());
    }

    private async Task OpenPanelAsync(ComposerMode mode, ulong channelId, ulong messageId, ComposerLayout layout)
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

        if (await _client.GetChannelAsync(channelId) is not IMessageChannel channel)
        {
            await RespondAsync(components: MessagePrefab.AnyFail("目标频道不存在或不支持消息", "### 目标无效"), ephemeral: true);
            return;
        }

        if (mode == ComposerMode.Edit)
        {
            if (await channel.GetMessageAsync(messageId) is not IUserMessage target)
            {
                await RespondAsync(components: MessagePrefab.AnyFail("目标消息不存在", "### 目标无效"), ephemeral: true);
                return;
            }

            if (target.Author.Id != _client.CurrentUser.Id)
            {
                await RespondAsync(components: MessagePrefab.AnyFail("只能修改 bot 自己发送的消息", "### 目标无效"), ephemeral: true);
                return;
            }
        }

        var session = _sessionManager.Register<MessageComposerSession>(
            new MessageComposerContext(channelId, messageId, mode, layout),
            Context.User.Id.ToString());

        _logger.Info("消息面板已创建: Mode={Mode}, Layout={Layout}, ChannelId={ChannelId}, MessageId={MessageId}, UserId={UserId}, SessionId={SessionId}",
            mode, layout, channelId, messageId, Context.User.Id, session.SessionId);

        await RespondAsync(components: session.Render(), ephemeral: true);
    }

    private async Task SubmitAsync(MessageComposerSession session)
    {
        if (string.IsNullOrWhiteSpace(session.Content))
        {
            await RespondAsync(components: MessagePrefab.AnyFail("内容不能为空", "### 无法提交"), ephemeral: true);
            return;
        }

        // 发送/编辑可能超过交互的 3 秒窗口，先 defer 再改原消息
        await DeferAsync();

        try
        {
            var channel = await _client.GetChannelAsync(session.Context.ChannelId) as IMessageChannel
                ?? throw new InvalidOperationException("目标频道不存在或不支持消息");

            var url = session.Context.Mode == ComposerMode.Send
                ? await SendAsync(channel, session)
                : await EditAsync(channel, session);

            session.Complete(url);
            _sessionManager.TryRemove(session.SessionId);

            _logger.Info("消息已提交: Mode={Mode}, Layout={Layout}, ChannelId={ChannelId}, MessageId={MessageId}, UserId={UserId}, Url={Url}",
                session.Context.Mode, session.Layout, session.Context.ChannelId, session.Context.MessageId, Context.User.Id, url);

            await ModifyOriginalResponseAsync(msg => msg.Components = session.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "提交消息失败: Mode={Mode}, Layout={Layout}, ChannelId={ChannelId}, MessageId={MessageId}, UserId={UserId}",
                session.Context.Mode, session.Layout, session.Context.ChannelId, session.Context.MessageId, Context.User.Id);

            await ModifyOriginalResponseAsync(msg => msg.Components = MessagePrefab.FunctionFail(ex.Message));
        }
    }

    private static async Task<string> SendAsync(IMessageChannel channel, MessageComposerSession session)
    {
        var sent = session.Layout == ComposerLayout.Plain
            ? await channel.SendMessageAsync(session.Content)
            : await channel.SendMessageAsync(components: session.BuildCard());

        return sent.GetJumpUrl();
    }

    private async Task<string> EditAsync(IMessageChannel channel, MessageComposerSession session)
    {
        var message = await channel.GetMessageAsync(session.Context.MessageId) as IUserMessage
            ?? throw new InvalidOperationException("目标消息不存在");

        if (message.Author.Id != _client.CurrentUser.Id)
        {
            throw new InvalidOperationException("只能修改 bot 自己发送的消息");
        }

        if (session.Layout == ComposerLayout.Plain)
        {
            await message.ModifyAsync(props =>
            {
                props.Content = session.Content;
                props.Components = new ComponentBuilder().Build();
                props.Flags = MessageFlags.None;
            });
        }
        else
        {
            var card = session.BuildCard();

            await message.ModifyAsync(props =>
            {
                // 全量替换：清空正文与旧组件，换成新的 Component V2 卡片
                props.Content = string.Empty;
                props.Components = card;
                props.Flags = MessageFlags.ComponentsV2;
            });
        }

        return message.GetJumpUrl();
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

    private static ComposerLayout ParseLayout(string value)
        => string.Equals(value, LayoutPlain, StringComparison.OrdinalIgnoreCase)
            ? ComposerLayout.Plain
            : ComposerLayout.Card;

    /// <summary>
    /// 解析消息引用：纯消息 ID，或 <c>https://discord.com/channels/{guild}/{channel}/{message}</c> 链接。
    /// 纯 ID 时 <paramref name="channelId"/> 为 0（由调用方使用当前频道）。
    /// </summary>
    private static bool TryParseMessageReference(string input, out ulong channelId, out ulong messageId)
    {
        channelId = 0;
        messageId = 0;

        var text = input.Trim();
        if (ulong.TryParse(text, out var id))
        {
            messageId = id;
            return messageId != 0;
        }

        var segments = text.Split('?', '#')[0].TrimEnd('/').Split('/');

        // 从尾部找连续两段数字：channelId / messageId
        for (var i = segments.Length - 1; i >= 1; i--)
        {
            if (ulong.TryParse(segments[i], out var message) &&
                ulong.TryParse(segments[i - 1], out var channel) &&
                channel != 0 && message != 0)
            {
                channelId = channel;
                messageId = message;
                return true;
            }
        }

        return false;
    }
}
