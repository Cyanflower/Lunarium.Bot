using Discord;

using Lunarium.Bot.Framework.Interaction;

namespace Lunarium.Bot.Lum.Features.MessageComposer;

/// <summary>消息面板的目标类型。</summary>
public enum ComposerMode
{
    /// <summary>向频道新发一条消息。</summary>
    Send,

    /// <summary>原地全量替换一条已有消息。</summary>
    Edit
}

/// <summary>消息形态。</summary>
public enum ComposerLayout
{
    /// <summary>纯文本消息（无组件）。</summary>
    Plain,

    /// <summary>Component V2 卡片（容器 + 文本显示）。</summary>
    Card
}

/// <summary>
/// 消息面板的会话上下文：只保存"目标"，不保存消息内容。
/// </summary>
/// <param name="ChannelId">目标频道/帖子 ID。</param>
/// <param name="MessageId">目标消息 ID（仅 <see cref="ComposerMode.Edit"/> 有意义，否则为 0）。</param>
/// <param name="Mode">新发还是替换。</param>
/// <param name="Layout">初始布局。</param>
public sealed record MessageComposerContext(
    ulong ChannelId,
    ulong MessageId,
    ComposerMode Mode,
    ComposerLayout Layout) : ISessionContext<MessageComposerSession>
{
    public MessageComposerSession CreateSession(string sessionId, DateTimeOffset expiresAt)
        => new(sessionId, expiresAt, this);
}

/// <summary>
/// 消息撰写会话：在内存里攒齐一条消息的全部字段，提交时一次性全量发送/替换。
/// <para>
/// 不做任何持久化——会话过期即丢弃；编辑时也不读取原消息内容，属于"原地全量替换"。
/// </para>
/// </summary>
public sealed class MessageComposerSession : BaseInteractionsSession<MessageComposerContext>
{
    /// <summary>面板里正文预览的截断长度。</summary>
    private const int ContentPreviewLength = 300;

    private static readonly Color PanelColor = Color.Blue;

    private bool _isCancelled;
    private bool _isCompleted;

    public MessageComposerSession(string sessionId, DateTimeOffset expiresAt, MessageComposerContext context)
        : base(sessionId, expiresAt, context)
    {
        Layout = context.Layout;
    }

    protected override string Scope => "composer";

    public ComposerLayout Layout { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? Footer { get; private set; }

    public Color Color { get; private set; } = Color.Blue;

    /// <summary>提交成功后的消息跳转链接。</summary>
    public string? ResultUrl { get; private set; }

    private bool IsFinished => _isCancelled || _isCompleted;

    public void SetContent(string? content) => Content = content?.Trim() ?? string.Empty;

    public void SetTitle(string? title) => Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    public void SetFooter(string? footer) => Footer = string.IsNullOrWhiteSpace(footer) ? null : footer.Trim();

    public void SetColor(Color color) => Color = color;

    public void ToggleLayout()
        => Layout = Layout == ComposerLayout.Card ? ComposerLayout.Plain : ComposerLayout.Card;

    public void Complete(string? resultUrl)
    {
        _isCompleted = true;
        ResultUrl = resultUrl;
    }

    public void Cancel() => _isCancelled = true;

    /// <summary>按当前字段拼出 Component V2 卡片。</summary>
    public MessageComponent BuildCard()
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color);

            if (!string.IsNullOrWhiteSpace(Title))
            {
                container.WithTextDisplay(Title);
                container.WithSeparator();
            }

            container.WithTextDisplay(Content);

            if (!string.IsNullOrWhiteSpace(Footer))
            {
                container.WithSeparator();
                container.WithTextDisplay($"-# {Footer}");
            }
        }).Build();
    }

    public override MessageComponent Render()
    {
        var target = Context.Mode == ComposerMode.Send
            ? MentionUtils.MentionChannel(Context.ChannelId)
            : $"`{Context.MessageId}`（原地替换）";

        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(PanelColor);

            container.WithTextDisplay($"""
                ## {(Context.Mode == ComposerMode.Send ? "发送消息" : "编辑消息")}
                - 目标: {target}
                - 布局: {(Layout == ComposerLayout.Card ? "卡片" : "纯文本")}
                """);

            if (Layout == ComposerLayout.Card)
            {
                container.WithSeparator();
                container.WithTextDisplay($"### 标题\n{FormatOptional(Title)}");
                container.WithActionRow(row => row.WithButton(
                    label: "编辑标题",
                    customId: CustomId("editTitle"),
                    style: ButtonStyle.Secondary,
                    disabled: IsFinished));

                container.WithSeparator();
                container.WithTextDisplay($"### 颜色\n#{Color.RawValue:X6}");
                container.WithActionRow(row => row.WithButton(
                    label: "编辑颜色",
                    customId: CustomId("editColor"),
                    style: ButtonStyle.Secondary,
                    disabled: IsFinished));
            }

            container.WithSeparator();
            container.WithTextDisplay($"### 内容\n{FormatContent(Content)}");
            container.WithActionRow(row => row.WithButton(
                label: "编辑内容",
                customId: CustomId("editContent"),
                style: ButtonStyle.Primary,
                disabled: IsFinished));

            if (Layout == ComposerLayout.Card)
            {
                container.WithSeparator();
                container.WithTextDisplay($"### 页脚\n{FormatOptional(Footer)}");
                container.WithActionRow(row => row.WithButton(
                    label: "编辑页脚",
                    customId: CustomId("editFooter"),
                    style: ButtonStyle.Secondary,
                    disabled: IsFinished));
            }

            container.WithSeparator();

            if (_isCancelled)
            {
                container.WithTextDisplay("已取消");
            }
            else if (_isCompleted)
            {
                container.WithTextDisplay(ResultUrl is null
                    ? "已提交"
                    : $"已提交：[查看消息]({ResultUrl})");
            }
            else
            {
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: Layout == ComposerLayout.Card ? "切换为纯文本" : "切换为卡片",
                        customId: CustomId("toggleLayout"),
                        style: ButtonStyle.Secondary);

                    row.WithButton(
                        label: Context.Mode == ComposerMode.Send ? "发送" : "应用",
                        customId: CustomId("submit"),
                        style: ButtonStyle.Success);

                    row.WithButton(
                        label: "取消",
                        customId: CustomId("cancel"),
                        style: ButtonStyle.Danger);
                });
            }
        }).Build();
    }

    private static string FormatOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? "（未设置）" : value;

    private static string FormatContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "（空）";
        }

        return content.Length <= ContentPreviewLength
            ? content
            : content[..ContentPreviewLength] + "…";
    }
}
