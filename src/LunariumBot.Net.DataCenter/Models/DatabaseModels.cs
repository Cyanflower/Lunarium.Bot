using System.Text.Json;

using LunariumBot.Net.DataCenter.Models;
using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.DataCenter.Models.Database;
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
    public Jsonb<List<string>> AttachmentUrls { get; set; } = null;
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
        AttachmentUrls = (domain.AttachmentUrls is not null && domain.AttachmentUrls.Count > 0) ? domain.AttachmentUrls : null,
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
            AttachmentUrls = AttachmentUrls,
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
    public string UserUsername { get; set; } = "";
    public string? UserNikcname { get; set; }
    public string? UserGlobalName { get; set; }
    public string? UserAvatarUrl { get; set; }
    public List<long> UserRoles { get; set; } = new();
    public int UserThreadCountDelta { get; set; } = 0;
    public int UserMessageCountDelta { get; set; } = 0;
    public int UserUpdateCountDelta { get; set; } = 0;
    public int UserCreditLv1Delta { get; set; } = 0;
    public int UserCreditLv2Delta { get; set; } = 0;
    public int UserCreditLv3Delta { get; set; } = 0;

    public static UserDataDb FromDomain(UserData domain) => new()
    {
        UserId = domain.UserId.ToDbId(),
        UserUsername = domain.UserUsername,
        UserNikcname = domain.UserNikcname,
        UserGlobalName = domain.UserGlobalName,
        UserAvatarUrl = domain.UserAvatarUrl,
        UserRoles = domain.UserRoles.Select(id => id.ToDbId()).ToList(),
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
        UserUsername = UserUsername,
        UserNikcname = UserNikcname,
        UserGlobalName = UserGlobalName,
        UserAvatarUrl = UserAvatarUrl,
        UserRoles = UserRoles.Select(id => id.ToDiscordId()).ToList(),
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
internal class TagDataDb
{
    public long TagId { get; set; }
    public long ChannelId { get; set; }
    public string TagName { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildName { get; set; } = "";
    public int UsageCount { get; set; }

    public static TagDataDb FromDomain(TagData domain) => new()
    {
        TagId = domain.TagId.ToDbId(),
        ChannelId = domain.ChannelId.ToDbId(),
        TagName = domain.TagName,
        ChannelName = domain.ChannelName,
        GuildName = domain.GuildName,
        UsageCount = domain.UsageCount
    };

    public TagData ToDomain() => new()
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
    public long ThreadId { get; set; }
    public string? Title { get; set; }
    public string? JumpUrl { get; set; }
    public Jsonb<List<string>> AttachmentUrls { get; set; }
    public long FirstMessageId { get; set; }
    public long AuthorId { get; set; }
    public long ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public long GuildId { get; set; }
    public string? GuildName { get; set; }
    public long CreatedAt { get; set; }
    public int MemberCount { get; set; }
    public int ReactionCount { get; set; }
    public int ReplyCount { get; set; }
    public long LastActiveAt { get; set; }
    public string? Content { get; set; }
    public string? Tags { get; set; }
    public long AddedAt { get; set; }
    public bool IsFallbackData { get; set; } = false;
    public string? FavoriteAS { get; set; }
    public long UpdatedAt { get; set; } = 0;
    public string MessageJumpUrl { get; set; } = "";

    public static PostDisplayModelDb FromDomain(PostDisplayModel domain) => new()
    {
        ThreadId = domain.ThreadId.ToDbId(),
        Title = domain.Title,
        JumpUrl = domain.JumpUrl,
        AttachmentUrls = (domain.AttachmentUrls is not null && domain.AttachmentUrls.Count > 0) ? domain.AttachmentUrls : null,
        FirstMessageId = domain.FirstMessageId.ToDbId(),
        AuthorId = domain.AuthorId.ToDbId(),
        ChannelId = domain.ChannelId.ToDbId(),
        ChannelName = domain.ChannelName,
        GuildId = domain.GuildId.ToDbId(),
        GuildName = domain.GuildName,
        CreatedAt = domain.CreatedAt,
        MemberCount = domain.MemberCount,
        ReactionCount = domain.ReactionCount,
        ReplyCount = domain.ReplyCount,
        LastActiveAt = domain.LastActiveAt,
        Content = domain.Content,
        Tags = domain.Tags,
        AddedAt = domain.AddedAt,
        IsFallbackData = domain.IsFallbackData,
        FavoriteAS = domain.FavoriteAS,
        UpdatedAt = domain.UpdatedAt,
        MessageJumpUrl = domain.MessageJumpUrl
    };

    public PostDisplayModel ToDomain() => new()
    {
        ThreadId = ThreadId.ToDiscordId(),
        Title = Title,
        JumpUrl = JumpUrl,
        AttachmentUrls = AttachmentUrls,
        FirstMessageId = FirstMessageId.ToDiscordId(),
        AuthorId = AuthorId.ToDiscordId(),
        ChannelId = ChannelId.ToDiscordId(),
        ChannelName = ChannelName,
        GuildId = GuildId.ToDiscordId(),
        GuildName = GuildName,
        CreatedAt = CreatedAt,
        MemberCount = MemberCount,
        ReactionCount = ReactionCount,
        ReplyCount = ReplyCount,
        LastActiveAt = LastActiveAt,
        Content = Content,
        Tags = Tags,
        AddedAt = AddedAt,
        IsFallbackData = IsFallbackData,
        FavoriteAS = FavoriteAS,
        UpdatedAt = UpdatedAt,
        MessageJumpUrl = MessageJumpUrl
    };
}

public class RoleDataDb
{
    public long RoleId { get; set; }
    public int RolePosition { get; set; }
    public string RoleName { get; set; } = "";
    public string? RoleIconUrl { get; set; }
    public string? RoleEmoji { get; set; }
    public int? RolePrimaryColor { get; set; }
    public int? RoleSecondaryColor { get; set; }
    public int? RoleTertiaryColor { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeleteAt { get; set; }

    public static RoleDataDb FromDomain(RoleData roleData) => new()
    {
        RoleId = roleData.RoleId.ToDbId(),
        RoleName = roleData.RoleName,
        RolePosition = roleData.RolePosition,
        RoleIconUrl = roleData.RoleIconUrl,
        RoleEmoji = roleData.RoleEmoji,
        RolePrimaryColor = roleData.RolePrimaryColor?.ToDbId(),
        RoleSecondaryColor = roleData.RoleSecondaryColor?.ToDbId(),
        RoleTertiaryColor = roleData.RoleTertiaryColor?.ToDbId(),
        IsDeleted = roleData.IsDeleted,
        DeleteAt = roleData.DeleteAt,
    };


    public RoleData ToDomain() => new()
    {
        RoleId = RoleId.ToDiscordId(),
        RoleName = RoleName,
        RolePosition = RolePosition,
        RoleIconUrl = RoleIconUrl,
        RoleEmoji = RoleEmoji,
        RolePrimaryColor = RolePrimaryColor?.ToDiscordId(),
        RoleSecondaryColor = RoleSecondaryColor?.ToDiscordId(),
        RoleTertiaryColor = RoleTertiaryColor?.ToDiscordId(),
        IsDeleted = IsDeleted,
        DeleteAt = DeleteAt,
    };
}