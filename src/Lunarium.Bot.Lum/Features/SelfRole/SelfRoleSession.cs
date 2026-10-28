using Discord;

using Lunarium.Bot.Framework.Interaction;

namespace Lunarium.Bot.Lum.Features.SelfRole;

/// <summary>自助身份组面板的会话上下文。</summary>
/// <param name="ChannelId">面板发布目标频道/帖子 ID。</param>
public sealed record SelfRoleContext(ulong ChannelId) : ISessionContext<SelfRoleSession>
{
    public SelfRoleSession CreateSession(string sessionId, DateTimeOffset expiresAt)
        => new(sessionId, expiresAt, this);
}

/// <summary>
/// 自助身份组面板的撰写会话：攒齐卡片字段，提交时发布一条带两个固定按钮的消息。
/// <para>
/// 发布后的按钮 customId 形如 <c>selfrole:grant:{roleId}</c> / <c>selfrole:revoke:{roleId}</c>，
/// 身份组 ID 直接编码在 customId 里，因此不需要持久化消息或会话。
/// </para>
/// </summary>
public sealed class SelfRoleSession : BaseInteractionsSession<SelfRoleContext>
{
    private const int ContentPreviewLength = 300;

    private static readonly Color PanelColor = Color.Blue;

    private bool _isCancelled;
    private bool _isCompleted;

    public SelfRoleSession(string sessionId, DateTimeOffset expiresAt, SelfRoleContext context)
        : base(sessionId, expiresAt, context)
    {
    }

    protected override string Scope => "rolepanel";

    public string Content { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? Footer { get; private set; }

    public Color Color { get; private set; } = Color.Blue;

    /// <summary>被领取/放弃的身份组 ID；0 表示尚未选择。</summary>
    public ulong RoleId { get; private set; }

    public string GrantLabel { get; private set; } = "领取身份组";

    public string RevokeLabel { get; private set; } = "放弃身份组";

    /// <summary>发布成功后的消息跳转链接。</summary>
    public string? ResultUrl { get; private set; }

    private bool IsFinished => _isCancelled || _isCompleted;

    public void SetContent(string? content) => Content = content?.Trim() ?? string.Empty;

    public void SetTitle(string? title) => Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    public void SetFooter(string? footer) => Footer = string.IsNullOrWhiteSpace(footer) ? null : footer.Trim();

    public void SetColor(Color color) => Color = color;

    public void SetRole(ulong roleId) => RoleId = roleId;

    /// <summary>设置两个按钮的文字；为空时保持原值。</summary>
    public void SetLabels(string? grantLabel, string? revokeLabel)
    {
        if (!string.IsNullOrWhiteSpace(grantLabel))
        {
            GrantLabel = grantLabel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(revokeLabel))
        {
            RevokeLabel = revokeLabel.Trim();
        }
    }

    public void Complete(string? resultUrl)
    {
        _isCompleted = true;
        ResultUrl = resultUrl;
    }

    public void Cancel() => _isCancelled = true;

    /// <summary>发布到频道的那张卡片。</summary>
    public MessageComponent BuildPanelCard()
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

            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: GrantLabel,
                    customId: $"selfrole:grant:{RoleId}",
                    style: ButtonStyle.Success);

                row.WithButton(
                    label: RevokeLabel,
                    customId: $"selfrole:revoke:{RoleId}",
                    style: ButtonStyle.Secondary);
            });

            if (!string.IsNullOrWhiteSpace(Footer))
            {
                container.WithSeparator();
                container.WithTextDisplay($"-# {Footer}");
            }
        }).Build();
    }

    public override MessageComponent Render()
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(PanelColor);

            container.WithTextDisplay($"""
                ## 自助身份组面板
                - 目标: {MentionUtils.MentionChannel(Context.ChannelId)}
                """);

            container.WithSeparator();
            container.WithTextDisplay($"### 身份组\n{(RoleId == 0 ? "（未选择）" : MentionUtils.MentionRole(RoleId))}");
            container.WithActionRow(row => row.WithButton(
                label: "选择身份组",
                customId: CustomId("editRole"),
                style: ButtonStyle.Primary,
                disabled: IsFinished));

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

            container.WithSeparator();
            container.WithTextDisplay($"### 内容\n{FormatContent(Content)}");
            container.WithActionRow(row => row.WithButton(
                label: "编辑内容",
                customId: CustomId("editContent"),
                style: ButtonStyle.Primary,
                disabled: IsFinished));

            container.WithSeparator();
            container.WithTextDisplay($"### 按钮文字\n- 领取: {GrantLabel}\n- 放弃: {RevokeLabel}");
            container.WithActionRow(row => row.WithButton(
                label: "编辑按钮文字",
                customId: CustomId("editLabels"),
                style: ButtonStyle.Secondary,
                disabled: IsFinished));

            container.WithSeparator();
            container.WithTextDisplay($"### 页脚\n{FormatOptional(Footer)}");
            container.WithActionRow(row => row.WithButton(
                label: "编辑页脚",
                customId: CustomId("editFooter"),
                style: ButtonStyle.Secondary,
                disabled: IsFinished));

            container.WithSeparator();

            if (_isCancelled)
            {
                container.WithTextDisplay("已取消");
            }
            else if (_isCompleted)
            {
                container.WithTextDisplay(ResultUrl is null
                    ? "已发布"
                    : $"已发布：[查看面板]({ResultUrl})");
            }
            else
            {
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "发布",
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
