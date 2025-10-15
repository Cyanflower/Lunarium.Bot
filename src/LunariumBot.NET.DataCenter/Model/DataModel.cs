// .NET 核心库
using System.Text;
using System.Collections.Immutable;

// NuGet 包依赖
using Discord;
using Discord.Rest;
using Discord.Interactions;

namespace LunariumBot.Net.DataCenter.Model;

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

public class ChannelData
{
    public ulong ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public ulong GuildId { get; set; }
    public string GuildName { get; set; } = "";
}
/// <summary>
/// 数据模型: 帖子信息
/// 用于封装和传递从数据库或API获取的帖子数据。
/// </summary>
public class ThreadData
{
    // --- 定义模型属性 ---
    public ulong ThreadId { get; set; }        // 帖子ID
    public ulong FirstMessageId { get; set; }  // 首条消息ID
    public ulong AuthorId { get; set; }        // 作者ID
    public ulong ChannelId { get; set; }       // 频道ID
    public string ChannelName { get; set; } = "";
    public ulong GuildId { get; set; }         // 服务器ID
    public string GuildName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } // 创建时间
    public int MemberCount { get; set; }       // 成员数量
    public int ReactionCount { get; set; }     // 回应数量
    public int ReplyCount { get; set; }        // 回复数量
    public DateTimeOffset LastActiveAt { get; set; } // 最后活跃时间
    public string JumpUrl { get; set; } = "";  // 跳转链接
    public string? AttachmentUrl { get; set; } = null; // 附件链接

    // 更新数据
    public DateTimeOffset? UpdateAt { get; set; } = null;
    public string? UpdateJumpUrl { get; set; } = null;
    public int UpdateCount { get; set; } = 0;

    // 标签
    public string? Tags { get; set; } = null;
    public List<ulong> TagsId { get; set; } = [];

    // 是否有效
    public bool IsValid { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    // 是否已删除
    public bool IsDeleted { get; set; } = false;
    public DateTimeOffset? DeleteAt { get; set; } = null;

    // 标题和首楼内容
    public string Title { get; set; } = "";    // 标题
    public string? FirstMessageContent { get; set; } = null;  // 内容
    public string TitleTokenized { get; set; } = "";    // 标题(分词)
    public string? ContentTokenized { get; set; } = null;  // 内容(分词)
}

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

public class UserData
{
    public ulong UserId { get; set; }
    public string UserName { get; set; } = "";
    public int UserThreadCount { get; set; }
    public int UserMessageCount { get; set; }
    public int UserUpdateCount { get; set; }
    public int UserCreditLv1 { get; set; }
    public int UserCreditLv2 { get; set; }
    public int UserCreditLv3 { get; set; }
}

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

public class TagInfo
{
    public ulong TagId { get; set; }
    public ulong ChannelId { get; set; }
    public string TagName { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildName { get; set; } = "";
    public int UsageCount { get; set; }
}

public class ProcessingResult
{
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Success;
    public ThreadData? Data { get; set; } = null;
    public RestThreadChannel? RestThreadChannel { get; set; } = null;
}

public class PostDisplayModel
{
    // --- 核心字段 (两张表都有) ---
    public ulong ThreadId { get; set; }
    public string? Title { get; set; }
    public string? JumpUrl { get; set; }
    public string? AttachmentUrl { get; set; }

    // --- 仅 posts_main (热数据) 才有的详细字段 ---
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

    // 不是所有功能都需要, 在不需要该项功能处使用时默认保持默认值
    // 收藏功能使用
    // 上次更新时间
    // --- 用于业务逻辑的辅助字段 ---
    public long AddedAt { get; set; } // 从 user_favorite_posts 表来，用于排序
    public bool IsFallbackData { get; set; } = false;// 一个标志，告诉我们这是不是冷数据
    public string? FavoriteAS { get; set; }
    public long UpdatedAt { get; set; } = 0;
    public string MessageJumpUrl { get; set; } = "";
}

/// <summary>
/// 帖子的处理结果
/// </summary>
public enum ProcessingStatus
{
    Success, // 成功处理
    Skipped, // 按规则跳过
    Failed   // 发生意外错误
}

public class ForumInfo
{
    // 频道
    public string? ChannelName { get; set; } = "";
    // === 统计概览 ===
    // 总贴数
    public int TotalPost { get; set; }
    public int PostAdd24h { get; set; }
    public int PostAdd7d { get; set; }
    // 新增贴数
    // 总回复
    public int TotalReply { get; set; }
    public int ReplyAdd24h { get; set; }
    public int ReplyAdd7d { get; set; }

    // 标签概览(7d)
    public Dictionary<TagInfo, long> TagData = new();
    // [焦点标签] 历史热门
    public Dictionary<TagInfo, long> TagDataHistory = new();

    // [焦点帖子] 近期(7天)前三反应
    public List<PostDisplayModel> ReactionTop7d = new();
    // [焦点帖子] 近期(7天)前三回复
    public List<PostDisplayModel> ReplyTop7d = new();
    // [焦点帖子] 历史前三反应
    public List<PostDisplayModel> ReactionTopHistory = new();
    // [焦点帖子] 历史前三回复
    public List<PostDisplayModel> ReplyTopHistory = new();
}

public record DatabaseInfo
{
    public required int Version { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public required DateTimeOffset LastSyncAt { get; init; }
    public required DateTimeOffset InitializeAt { get; init; }
}

public record DatabaseMigrationScript
{
    public required string ScriptFileName { get; init; }
    public required string ScriptSQLCommandContent { get; init; }
}

public record DatabaseMigrationInfo
{
    public required DatabaseInfo CurrentVersionInfo { get; init; }
    public required DatabaseInfo PredictVersionInfo { get; init; }

    // 使用不可变集合
    public ImmutableList<DatabaseMigrationScript> MigrationScripts { get; init; }
        = ImmutableList<DatabaseMigrationScript>.Empty;
}

public enum NumericUpdateType
{
    Increase,
    Decrease 
}