using System.Text.Json;

using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.SearchBot.Models.Database;
// ============================================
// 数据库模型(用于 PostgreSQL 存储,使用 long)
// ============================================

/// <summary>
/// 数据库元数据模型
/// </summary>
public class DbInfo
{
    public int Version { get; set; }
    public long AppliedAt { get; set; }
    public long LastSaveAt { get; set; }
    public long InitializeAt { get; set; }
}

// ============================================
// 内部数据库模型(仅在数据访问层使用)
// ============================================


/// <summary>
/// 频道数据 - 数据库模型
/// </summary>
internal class ChannelDataDb
{
    public long ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public long GuildId { get; set; }
    public string GuildName { get; set; } = "";

    public static ChannelDataDb FromDomain(ChannelData domain) => new()
    {
        ChannelId = domain.ChannelId.ToDbId(),
        ChannelName = domain.ChannelName,
        GuildId = domain.GuildId.ToDbId(),
        GuildName = domain.GuildName
    };

    public ChannelData ToDomain() => new()
    {
        ChannelId = ChannelId.ToDiscordId(),
        ChannelName = ChannelName,
        GuildId = GuildId.ToDiscordId(),
        GuildName = GuildName
    };
}

/// <summary>
/// 帖子数据 - 数据库模型
/// </summary>
internal class ThreadDataDb
{
    public long ThreadId { get; set; }
    public long FirstMessageId { get; set; }
    public long AuthorId { get; set; }
    public long ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public long GuildId { get; set; }
    public string GuildName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int ReactionCount { get; set; }
    public int ReplyCount { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public string JumpUrl { get; set; } = "";
    public string? AttachmentUrl { get; set; } = null;
    public DateTimeOffset? UpdatedAt { get; set; } = null;
    public string? UpdatedJumpUrl { get; set; } = null;
    public int UpdateCount { get; set; } = 0;
    public string? Tags { get; set; } = null;
    public List<long> TagsId { get; set; } = new();
    public bool IsValid { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public bool InForum { get; set; } = false;
    public DateTimeOffset? DeleteAt { get; set; } = null;
    public string Title { get; set; } = "";
    public string? FirstMessageContent { get; set; } = null;
    public string TitleTokenized { get; set; } = "";
    public string? ContentTokenized { get; set; } = null;

    public static ThreadDataDb FromDomain(ThreadData domain) => new()
    {
        ThreadId = domain.ThreadId.ToDbId(),
        FirstMessageId = domain.FirstMessageId.ToDbId(),
        AuthorId = domain.AuthorId.ToDbId(),
        ChannelId = domain.ChannelId.ToDbId(),
        ChannelName = domain.ChannelName,
        GuildId = domain.GuildId.ToDbId(),
        GuildName = domain.GuildName,
        CreatedAt = domain.CreatedAt.ToUniversalTime(),
        ReactionCount = domain.ReactionCount,
        ReplyCount = domain.ReplyCount,
        LastActiveAt = domain.LastActiveAt.ToUniversalTime(),
        JumpUrl = domain.JumpUrl,
        AttachmentUrl = domain.AttachmentUrl,
        UpdatedAt = domain.UpdatedAt?.ToUniversalTime(),
        UpdatedJumpUrl = domain.UpdatedJumpUrl,
        UpdateCount = domain.UpdateCount,
        Tags = domain.Tags,
        TagsId = domain.TagsId.Select(id => id.ToDbId()).ToList(),
        IsValid = domain.IsValid,
        IsLocked = domain.IsLocked,
        IsDeleted = domain.IsDeleted,
        InForum = domain.InForum,
        DeleteAt = domain.DeleteAt?.ToUniversalTime(),
        Title = domain.Title,
        FirstMessageContent = domain.FirstMessageContent,
        TitleTokenized = domain.TitleTokenized,
        ContentTokenized = domain.ContentTokenized
    };

    public ThreadData ToDomain()
    {
        return new ThreadData
        {
            ThreadId = ThreadId.ToDiscordId(),
            FirstMessageId = FirstMessageId.ToDiscordId(),
            AuthorId = AuthorId.ToDiscordId(),
            ChannelId = ChannelId.ToDiscordId(),
            ChannelName = ChannelName,
            GuildId = GuildId.ToDiscordId(),
            GuildName = GuildName,
            CreatedAt = CreatedAt.ToUniversalTime(),
            ReactionCount = ReactionCount,
            ReplyCount = ReplyCount,
            LastActiveAt = LastActiveAt.ToUniversalTime(),
            JumpUrl = JumpUrl,
            AttachmentUrl = AttachmentUrl,
            UpdatedAt = UpdatedAt?.ToUniversalTime(),
            UpdatedJumpUrl = UpdatedJumpUrl,
            UpdateCount = UpdateCount,
            Tags = Tags,
            TagsId = TagsId.Select(id => id.ToDiscordId()).ToList(),
            IsValid = IsValid,
            IsLocked = IsLocked,
            IsDeleted = IsDeleted,
            InForum = InForum,
            DeleteAt = DeleteAt?.ToUniversalTime(),
            Title = Title,
            FirstMessageContent = FirstMessageContent,
            TitleTokenized = TitleTokenized,
            ContentTokenized = ContentTokenized
        };
    }
}

/// <summary>
/// 消息数据 - 数据库模型
/// </summary>
internal class MessageDataDb
{
    public long MessageId { get; set; }
    public long AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long GuildId { get; set; }
    public long ChannelId { get; set; }
    public long? ThreadId { get; set; } = null;
    public string MessageJumpUrl { get; set; } = "";
    public bool IsCreateThread { get; set; }
    public bool IsUpdate { get; set; }

    public static MessageDataDb FromDomain(MessageData domain) => new()
    {
        MessageId = domain.MessageId.ToDbId(),
        AuthorId = domain.AuthorId.ToDbId(),
        CreatedAt = domain.CreatedAt.ToUniversalTime(),
        GuildId = domain.GuildId.ToDbId(),
        ChannelId = domain.ChannelId.ToDbId(),
        ThreadId = domain.ThreadId?.ToDbId(),
        MessageJumpUrl = domain.MessageJumpUrl,
        IsCreateThread = domain.IsCreateThread,
        IsUpdate = domain.IsUpdate
    };

    public MessageData ToDomain() => new()
    {
        MessageId = MessageId.ToDiscordId(),
        AuthorId = AuthorId.ToDiscordId(),
        CreatedAt = CreatedAt.ToUniversalTime(),
        GuildId = GuildId.ToDiscordId(),
        ChannelId = ChannelId.ToDiscordId(),
        ThreadId = ThreadId?.ToDiscordId(),
        MessageJumpUrl = MessageJumpUrl,
        IsCreateThread = IsCreateThread,
        IsUpdate = IsUpdate
    };
}

/// <summary>
/// 用户数据 - 数据库模型
/// </summary>
internal class UserDataDb
{
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public int UserThreadCountDelta { get; set; }
    public int UserMessageCountDelta { get; set; }
    public int UserUpdateCountDelta { get; set; }
    public int UserCreditLv1Delta { get; set; }
    public int UserCreditLv2Delta { get; set; }
    public int UserCreditLv3Delta { get; set; }

    public static UserDataDb FromDomain(UserData domain) => new()
    {
        UserId = domain.UserId.ToDbId(),
        UserName = domain.UserName,
        UserThreadCountDelta = domain.UserThreadCountDelta,
        UserMessageCountDelta = domain.UserMessageCountDelta,
        UserUpdateCountDelta = domain.UserUpdateCountDelta,
        UserCreditLv1Delta = domain.UserCreditLv1Delta,
        UserCreditLv2Delta = domain.UserCreditLv2Delta,
        UserCreditLv3Delta = domain.UserCreditLv3Delta
    };

    public UserData ToDomain() => new()
    {
        UserId = UserId.ToDiscordId(),
        UserName = UserName,
        UserThreadCountDelta = UserThreadCountDelta,
        UserMessageCountDelta = UserMessageCountDelta,
        UserUpdateCountDelta = UserUpdateCountDelta,
        UserCreditLv1Delta = UserCreditLv1Delta,
        UserCreditLv2Delta = UserCreditLv2Delta,
        UserCreditLv3Delta = UserCreditLv3Delta
    };
}

/// <summary>
/// 帖子成员数据 - 数据库模型
/// </summary>
internal class PostMemberDataDb
{
    public long UserId { get; set; }
    public long ThreadId { get; set; }
    public bool IsThreadOwner { get; set; }
    public DateTimeOffset FirstJoinAt { get; set; }
    public DateTimeOffset LastJoinAt { get; set; }
    public DateTimeOffset? LeaveAt { get; set; }
    public bool IsLeave { get; set; }
    public long MessageCount { get; set; } = 0;
    public long? LastMessageId { get; set; } = null;
    public DateTimeOffset? LastMessageAt { get; set; } = null;

    public static PostMemberDataDb FromDomain(PostMemberData domain) => new()
    {
        UserId = domain.UserId.ToDbId(),
        ThreadId = domain.ThreadId.ToDbId(),
        IsThreadOwner = domain.IsThreadOwner,
        FirstJoinAt = domain.FirstJoinAt.ToUniversalTime(),
        LastJoinAt = domain.LastJoinAt.ToUniversalTime(),
        LeaveAt = domain.LeaveAt?.ToUniversalTime(),
        IsLeave = domain.IsLeave,
        MessageCount = domain.MessageCount.ToDbId(),
        LastMessageId = domain.LastMessageId?.ToDbId(),
        LastMessageAt = domain.LastMessageAt?.ToUniversalTime()
    };

    public PostMemberData ToDomain() => new()
    {
        UserId = UserId.ToDiscordId(),
        ThreadId = ThreadId.ToDiscordId(),
        IsThreadOwner = IsThreadOwner,
        FirstJoinAt = FirstJoinAt.ToUniversalTime(),
        LastJoinAt = LastJoinAt.ToUniversalTime(),
        LeaveAt = LeaveAt?.ToUniversalTime(),
        IsLeave = IsLeave,
        MessageCount = MessageCount.ToDiscordId(),
        LastMessageId = LastMessageId?.ToDiscordId(),
        LastMessageAt = LastMessageAt?.ToUniversalTime()
    };
}

/// <summary>
/// 标签信息 - 数据库模型
/// </summary>
internal class TagInfoDb
{
    public long TagId { get; set; }
    public long ChannelId { get; set; }
    public string TagName { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildName { get; set; } = "";
    public int UsageCount { get; set; }

    public static TagInfoDb FromDomain(TagInfo domain) => new()
    {
        TagId = domain.TagId.ToDbId(),
        ChannelId = domain.ChannelId.ToDbId(),
        TagName = domain.TagName,
        ChannelName = domain.ChannelName,
        GuildName = domain.GuildName,
        UsageCount = domain.UsageCount
    };

    public TagInfo ToDomain() => new()
    {
        TagId = TagId.ToDiscordId(),
        ChannelId = ChannelId.ToDiscordId(),
        TagName = TagName,
        ChannelName = ChannelName,
        GuildName = GuildName,
        UsageCount = UsageCount
    };
}

/// <summary>
/// 帖子展示模型 - 数据库模型
/// </summary>
internal class PostDisplayModelDb
{
    // ================== 核心识别信息 ==================
    public long ThreadId { get; set; }
    public long FirstMessageId { get; set; }

    // ================== 核心内容与链接 ==================
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? JumpUrl { get; set; }
    public string UpdateMessageJumpUrl { get; set; } = "";
    public string? AttachmentUrl { get; set; }

    // ================== 上下文信息 ==================
    public long AuthorId { get; set; }
    public long ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public long GuildId { get; set; }
    public string? GuildName { get; set; }

    // ================== 统计数据 ==================
    public int ReactionCount { get; set; }
    public int ReplyCount { get; set; }

    // ================== 时间戳 (Unix Timestamp) ==================
    public long CreatedAt { get; set; }
    public long LastActiveAt { get; set; }
    public long UpdatedAt { get; set; } = 0;

    // ================== 关联数据 ==================
    public string? Tags { get; set; }

    // ================== 用户界面与元数据 ==================
    public long AddedAt { get; set; }
    public string? FavoriteAS { get; set; }
    public bool IsFallbackData { get; set; } = false;


    // ================== 转换方法 ==================

    public static PostDisplayModelDb FromDomain(PostDisplayModel domain) => new()
    {
        // 核心识别信息
        ThreadId = domain.ThreadId.ToDbId(),
        FirstMessageId = domain.FirstMessageId.ToDbId(),
        // 核心内容与链接
        Title = domain.Title,
        Content = domain.Content,
        JumpUrl = domain.JumpUrl,
        UpdateMessageJumpUrl = domain.UpdateMessageJumpUrl,
        AttachmentUrl = domain.AttachmentUrl,
        // 上下文信息
        AuthorId = domain.AuthorId.ToDbId(),
        ChannelId = domain.ChannelId.ToDbId(),
        ChannelName = domain.ChannelName,
        GuildId = domain.GuildId.ToDbId(),
        GuildName = domain.GuildName,
        // 统计数据
        ReactionCount = domain.ReactionCount,
        ReplyCount = domain.ReplyCount,
        // 时间戳
        CreatedAt = domain.CreatedAt,
        LastActiveAt = domain.LastActiveAt,
        UpdatedAt = domain.UpdatedAt,
        // 关联数据
        Tags = domain.Tags,
        // 用户界面与元数据
        AddedAt = domain.AddedAt,
        FavoriteAS = domain.FavoriteAs,
        IsFallbackData = domain.IsFallbackData
    };

    public PostDisplayModel ToDomain() => new()
    {
        // 核心识别信息
        ThreadId = ThreadId.ToDiscordId(),
        FirstMessageId = FirstMessageId.ToDiscordId(),
        // 核心内容与链接
        Title = Title,
        Content = Content,
        JumpUrl = JumpUrl,
        UpdateMessageJumpUrl = UpdateMessageJumpUrl,
        AttachmentUrl = AttachmentUrl,
        // 上下文信息
        AuthorId = AuthorId.ToDiscordId(),
        ChannelId = ChannelId.ToDiscordId(),
        ChannelName = ChannelName,
        GuildId = GuildId.ToDiscordId(),
        GuildName = GuildName,
        // 统计数据
        ReactionCount = ReactionCount,
        ReplyCount = ReplyCount,
        // 时间戳
        CreatedAt = CreatedAt,
        LastActiveAt = LastActiveAt,
        UpdatedAt = UpdatedAt,
        // 关联数据
        Tags = Tags,
        // 用户界面与元数据
        AddedAt = AddedAt,
        FavoriteAs = FavoriteAS,
        IsFallbackData = IsFallbackData
    };
}
