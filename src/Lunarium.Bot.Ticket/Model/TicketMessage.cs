
namespace Lunarium.Bot.Ticket.Model;

/// <summary>
/// 工单频道内的单条消息快照。
/// <para>
/// 仅在归档时由 <c>TicketCrawler</c> 抓取并写入 <see cref="TicketData.TicketMessages"/>，
/// 活跃期的对话保留在 Discord 侧，不做实时快照。
/// </para>
/// </summary>
public sealed class TicketMessage
{
    /// <summary>消息ID（用于解析回复关系与跳转）。</summary>
    public ulong MessageId { get; init; }

    /// <summary>发送者用户ID。</summary>
    public ulong AuthorId { get; init; }

    /// <summary>发送者显示名（快照，用户改名后归档仍保留当时的名称）。</summary>
    public string AuthorDisplayName { get; init; } = string.Empty;

    /// <summary>发送者在归档内的头像相对路径（<c>attachment/avatar/xxx.png</c>）；未下载成功为 <see langword="null"/>。</summary>
    public string? AvatarPath { get; init; }

    /// <summary>消息正文（纯文本）。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>发送时间。</summary>
    public DateTimeOffset SentAt { get; init; }

    /// <summary>回复的目标消息ID；非回复消息为 <see langword="null"/>。</summary>
    public ulong? ReplyToMessageId { get; init; }

    /// <summary>附件（含正文外链图片）；无附件时为 <see langword="null"/>。</summary>
    public List<TicketAttachment>? Attachments { get; init; }

    /// <summary>消息中的 embed（含转发消息内的 embed）；无则为 <see langword="null"/>。</summary>
    public List<TicketEmbed>? Embeds { get; init; }

    /// <summary>消息里的贴纸；无则为 <see langword="null"/>。</summary>
    public List<TicketSticker>? Stickers { get; init; }

    /// <summary>消息组件（Components V2 的文本/按钮/媒体等，保留嵌套结构）；无则为 <see langword="null"/>。</summary>
    public List<TicketComponent>? Components { get; init; }

    /// <summary>转发的消息快照；无则为 <see langword="null"/>。</summary>
    public List<TicketForward>? Forwards { get; init; }
}

/// <summary>归档内的一条 embed（富文本卡片快照）。</summary>
public sealed class TicketEmbed
{
    /// <summary>标题。</summary>
    public string? Title { get; init; }

    /// <summary>描述（Markdown）。</summary>
    public string? Description { get; init; }

    /// <summary>标题链接。</summary>
    public string? Url { get; init; }

    /// <summary>左侧色条颜色（0xRRGGBB）；未设置为 <see langword="null"/>。</summary>
    public uint? Color { get; init; }

    /// <summary>作者名。</summary>
    public string? AuthorName { get; init; }

    /// <summary>作者头像的本地相对路径。</summary>
    public string? AuthorIconPath { get; init; }

    /// <summary>页脚文本。</summary>
    public string? FooterText { get; init; }

    /// <summary>embed 自带的时间戳。</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>字段列表（inline 语义与 Discord 一致）。</summary>
    public List<TicketEmbedField>? Fields { get; init; }

    /// <summary>大图的本地相对路径；未下载成功为 <see langword="null"/>。</summary>
    public string? ImagePath { get; init; }

    /// <summary>大图的原始 URL（下载失败时用于溯源）。</summary>
    public string? ImageUrl { get; init; }

    /// <summary>缩略图的本地相对路径；未下载成功为 <see langword="null"/>。</summary>
    public string? ThumbnailPath { get; init; }

    /// <summary>缩略图的原始 URL。</summary>
    public string? ThumbnailUrl { get; init; }
}

/// <summary>embed 内的单个字段。</summary>
public sealed class TicketEmbedField
{
    /// <summary>字段名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>字段值（Markdown）。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>是否与相邻字段并排显示。</summary>
    public bool Inline { get; init; }
}

/// <summary>
/// 归档内的一条消息组件（保留嵌套结构）。
/// <para>
/// Components V2 的正文放在 <c>TextDisplay</c> 组件里而不是消息 content，
/// 因此必须单独抓取，否则归档里只剩空气泡。
/// </para>
/// </summary>
public sealed class TicketComponent
{
    /// <summary>组件种类：text / button / select / container / section / row / media / file / divider。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>文本内容、按钮标签或下拉占位符。</summary>
    public string? Text { get; init; }

    /// <summary>补充说明（下拉选项、媒体 alt 文本等）。</summary>
    public string? Description { get; init; }

    /// <summary>链接按钮目标或媒体的原始 URL。</summary>
    public string? Url { get; init; }

    /// <summary>媒体下载后的本地相对路径。</summary>
    public string? Path { get; init; }

    /// <summary>子组件（container / section / row / gallery）。</summary>
    public List<TicketComponent>? Children { get; init; }
}

/// <summary>被转发的消息快照。</summary>
public sealed class TicketForward
{
    /// <summary>原消息作者ID；Discord 未提供快照作者时为 0。</summary>
    public ulong AuthorId { get; init; }

    /// <summary>原消息作者显示名。</summary>
    public string? AuthorDisplayName { get; init; }

    /// <summary>原消息作者的本地头像路径。</summary>
    public string? AuthorAvatarPath { get; init; }

    /// <summary>原消息正文。</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>原消息发送时间。</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>原消息的附件（含 embed 图片）；无则为 <see langword="null"/>。</summary>
    public List<TicketAttachment>? Attachments { get; init; }

    /// <summary>原消息的 embed。</summary>
    public List<TicketEmbed>? Embeds { get; init; }

    /// <summary>原消息的贴纸。</summary>
    public List<TicketSticker>? Stickers { get; init; }

    /// <summary>原消息的组件。</summary>
    public List<TicketComponent>? Components { get; init; }
}

/// <summary>消息里的一张贴纸（原件保存在 <c>attachment/sticker/</c> 下）。</summary>
public sealed class TicketSticker
{
    /// <summary>贴纸ID。</summary>
    public ulong Id { get; init; }

    /// <summary>贴纸名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>保存的文件格式：<c>png</c> / <c>gif</c> / <c>json</c>（Lottie）。</summary>
    public string Format { get; init; } = "png";

    /// <summary>下载时使用的 URL（仅作溯源）。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>工单目录内的相对路径；下载失败或 Lottie 未渲染时为 <see langword="null"/>。</summary>
    public string? LocalPath { get; init; }
}

/// <summary>归档内的单个附件（原件保存在工单目录的 <c>attachment/</c> 下）。</summary>
public sealed class TicketAttachment
{
    /// <summary>展示用文件名。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>来源 URL（Discord CDN 或外链，仅作溯源）。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>字节数；未知为 0。</summary>
    public long Size { get; init; }

    /// <summary>是否为图片（决定归档 HTML 里内嵌展示还是作为文件链接）。</summary>
    public bool IsImage { get; init; }

    /// <summary>是否为正文中的外链资源（区别于 Discord 附件）。</summary>
    public bool IsExternal { get; init; }

    /// <summary>工单目录内的相对路径（<c>attachment/xxxx.png</c>）；下载失败为 <see langword="null"/>。</summary>
    public string? LocalPath { get; init; }
}