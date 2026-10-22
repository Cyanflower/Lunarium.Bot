
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

    /// <summary>附件（含 embed 图片与正文外链图片）；无附件时为 <see langword="null"/>。</summary>
    public List<TicketAttachment>? Attachments { get; init; }
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