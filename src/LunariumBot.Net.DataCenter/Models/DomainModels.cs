// .NET 核心库
using System.Collections.Immutable;

// NuGet 包依赖
using Discord;
using Discord.Rest;
using Discord.Interactions;

namespace LunariumBot.Net.DataCenter.Model;

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
/// 帖子数据
/// </summary>
public class ThreadData
{
    public ulong ThreadId { get; set; }
    public ulong FirstMessageId { get; set; }
    public ulong AuthorId { get; set; }
    public ulong ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public ulong GuildId { get; set; }
    public string GuildName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int ReactionCount { get; set; }
    public int ReplyCount { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public string JumpUrl { get; set; } = "";
    public string? AttachmentUrl { get; set; } = null;

    // 更新数据
    public DateTimeOffset? UpdatedAt { get; set; } = null;
    public string? UpdatedJumpUrl { get; set; } = null;
    public int UpdateCount { get; set; } = 0;

    // 标签
    public string? Tags { get; set; } = null;
    public List<ulong> TagsId { get; set; } = [];

    // 状态标志
    public bool IsValid { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public bool InForum { get; set; } = false;
    public DateTimeOffset? DeleteAt { get; set; } = null;

    // 标题和内容
    public string Title { get; set; } = "";
    public string? FirstMessageContent { get; set; } = null;
    public string TitleTokenized { get; set; } = "";
    public string? ContentTokenized { get; set; } = null;
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
    // 核心字段
    public ulong ThreadId { get; set; }
    public string? Title { get; set; }
    public string? JumpUrl { get; set; }
    public string? AttachmentUrl { get; set; }

    // 详细字段
    public ulong FirstMessageId { get; set; }
    public ulong AuthorId { get; set; }
    public ulong ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public ulong GuildId { get; set; }
    public string? GuildName { get; set; }
    public long CreatedAt { get; set; }
    public int MemberCount { get; set; }
    public int ReactionCount { get; set; }
    public int ReplyCount { get; set; }
    public long LastActiveAt { get; set; }
    public string? Content { get; set; }
    public string? Tags { get; set; }

    // 辅助字段
    public long AddedAt { get; set; }
    public bool IsFallbackData { get; set; } = false;
    public string? FavoriteAS { get; set; }
    public long UpdatedAt { get; set; } = 0;
    public string MessageJumpUrl { get; set; } = "";
}