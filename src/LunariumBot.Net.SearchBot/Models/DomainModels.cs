namespace LunariumBot.Net.SearchBot.Models;

// ============================================
// 核心业务模型（与 Discord.NET 交互，使用 ulong）
// ============================================

/// <summary>
/// 频道数据
/// </summary>
public class ChannelData
{
    public ulong ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public ulong GuildId { get; set; }
    public string GuildName { get; set; } = "";
}

/// <summary>
/// 消息数据
/// </summary>
public class MessageData
{
    public ulong MessageId { get; set; }
    public ulong AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong? ThreadId { get; set; } = null;
    public string MessageJumpUrl { get; set; } = "";
    public bool IsCreateThread { get; set; }
    public bool IsUpdate { get; set; }
}

/// <summary>
/// 用户数据
/// </summary>
public class UserData
{
    public ulong UserId { get; set; }
    public string UserName { get; set; } = "";
    public int UserThreadCountDelta { get; set; }
    public int UserMessageCountDelta { get; set; }
    public int UserUpdateCountDelta { get; set; }
    public int UserCreditLv1Delta { get; set; }
    public int UserCreditLv2Delta { get; set; }
    public int UserCreditLv3Delta { get; set; }
}

/// <summary>
/// 帖子成员数据
/// </summary>
public class PostMemberData
{
    public ulong UserId { get; set; }
    public ulong ThreadId { get; set; }
    public bool IsThreadOwner { get; set; }
    public DateTimeOffset FirstJoinAt { get; set; }
    public DateTimeOffset LastJoinAt { get; set; }
    public DateTimeOffset? LeaveAt { get; set; }
    public bool IsLeave { get; set; }
    public ulong MessageCount { get; set; } = 0;
    public ulong? LastMessageId { get; set; } = null;
    public DateTimeOffset? LastMessageAt { get; set; } = null;
}

/// <summary>
/// 标签信息
/// </summary>
public class TagInfo
{
    public ulong TagId { get; set; }
    public ulong ChannelId { get; set; }
    public string TagName { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildName { get; set; } = "";
    public int UsageCount { get; set; }
}

/// <summary>
/// 帖子展示模型
/// </summary>
public class PostDisplayModel
{
    // ================== 核心识别信息 ==================
    /// <summary>
    /// 帖子的 Discord 雪花 ID
    /// </summary>
    public ulong ThreadId { get; set; }

    /// <summary>
    /// 帖子首条消息的 Discord 雪花 ID
    /// </summary>
    public ulong FirstMessageId { get; set; }

    // ================== 核心内容与链接 ==================
    /// <summary>
    /// 帖子标题, 最长100字符
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 帖子首条消息的内容, 最长 2000 或 4000(nitro会员) 字符
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 跳转到帖子的 URL
    /// </summary>
    public string? JumpUrl { get; set; }

    /// <summary>
    /// 跳转到更新消息的 URL (用于推送通知)
    /// </summary>
    public string UpdateMessageJumpUrl { get; set; } = "";

    /// <summary>
    /// 帖子首条消息的附件 URL
    /// </summary>
    public List<string>? AttachmentUrls { get; set; } = null;

    // ================== 上下文信息 ==================
    /// <summary>
    /// 帖子作者的 Discord 雪花 ID
    /// </summary>
    public ulong AuthorId { get; set; }

    /// <summary>
    /// 帖子所在频道的 Discord 雪花 ID
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// 帖子所在频道的名称
    /// </summary>
    public string? ChannelName { get; set; }

    /// <summary>
    /// 帖子所在服务器的 Discord 雪花 ID
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// 帖子所在服务器的名称
    /// </summary>
    public string? GuildName { get; set; }

    // ================== 统计数据 ==================
    /// <summary>
    /// 帖子的回应 (Reaction) 数量
    /// </summary>
    public int ReactionCount { get; set; }

    /// <summary>
    /// 帖子的回复数量
    /// </summary>
    public int ReplyCount { get; set; }

    // ================== 时间戳 (Unix Timestamp) ==================
    /// <summary>
    /// 帖子创建时间的 Unix 时间戳
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 帖子最后活跃时间的 Unix 时间戳
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>
    /// 帖子最后更新时间的 Unix 时间戳 (用于推送)
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    // ================== 关联数据 ==================
    /// <summary>
    /// 帖子的标签, 以逗号分隔的字符串
    /// </summary>
    public string? Tags { get; set; }

    // ================== 用户界面与元数据 ==================
    /// <summary>
    /// (在收藏夹中) 收藏此帖子的时间
    /// </summary>
    // public long AddedAt { get; set; }
    /// <summary>
    /// (在收藏夹中) 用户为此收藏设置的别名
    /// </summary>
    // public string? FavoriteAs { get; set; }

    public void Update(PostDisplayModel newData)
    {
        FirstMessageId = newData.FirstMessageId;
        Title = newData.Title;
        Content = newData.Content;
        JumpUrl = newData.JumpUrl;
        UpdateMessageJumpUrl = newData.UpdateMessageJumpUrl;
        AttachmentUrls = newData.AttachmentUrls;
        AuthorId = newData.AuthorId;
        ChannelId = newData.ChannelId;
        ChannelName = newData.ChannelName;
        GuildId = newData.GuildId;
        GuildName = newData.GuildName;
        ReactionCount = newData.ReactionCount;
        ReplyCount = newData.ReplyCount;
        CreatedAt = newData.CreatedAt;
        LastActiveAt = newData.LastActiveAt;
        UpdatedAt = newData.UpdatedAt;
        Tags = newData.Tags;
    }
}
