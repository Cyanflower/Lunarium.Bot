using System.Text.Json;

using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.SearchBot.Models.Database;

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

    public static TagInfoDb FromDomain(TagData domain) => new()
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
    // ================== 核心识别信息 ==================
    public long ThreadId { get; set; }
    public long FirstMessageId { get; set; }

    // ================== 核心内容与链接 ==================
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? JumpUrl { get; set; }
    public string UpdateMessageJumpUrl { get; set; } = "";
    public Jsonb<List<string>> AttachmentUrls { get; set; } = null;

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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

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
        AttachmentUrls = (domain.AttachmentUrls is not null && domain.AttachmentUrls.Count > 0) ? domain.AttachmentUrls : null,
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
        Tags = domain.Tags
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
        AttachmentUrls = AttachmentUrls,
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
        Tags = Tags
    };
}
