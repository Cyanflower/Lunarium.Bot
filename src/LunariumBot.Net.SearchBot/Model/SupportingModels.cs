using System.Collections.Immutable;
using Discord.Rest;

namespace LunariumBot.Net.SearchBot.Model;

// ============================================
// 枚举类型
// ============================================

/// <summary>
/// 帖子的处理结果状态
/// </summary>
public enum ProcessingStatus
{
    Success,  // 成功处理
    Invalid,  // 按规则视为无效
    Failed    // 发生意外错误
}

/// <summary>
/// 数值更新类型
/// </summary>
public enum NumericUpdateType
{
    Increase,  // 增加
    Decrease   // 减少
}

// ============================================
// 视图模型 / 业务逻辑模型
// ============================================

/// <summary>
/// 帖子处理结果
/// </summary>
public class ProcessingResult
{
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Success;
    public ThreadData? Data { get; set; } = null;
    public RestThreadChannel? RestThreadChannel { get; set; } = null;
}

/// <summary>
/// 论坛统计信息视图模型
/// </summary>
public class ForumInfo
{
    public string? ChannelName { get; set; } = "";
    
    // 统计概览
    public int TotalPost { get; set; }
    public int PostAdd24h { get; set; }
    public int PostAdd7d { get; set; }
    public int TotalReply { get; set; }
    public int ReplyAdd24h { get; set; }
    public int ReplyAdd7d { get; set; }

    // 标签概览
    public Dictionary<TagInfo, int> TagData { get; set; } = new();
    public Dictionary<TagInfo, int> TagDataHistory { get; set; } = new();

    // 焦点帖子
    public List<PostDisplayModel> ReactionTop7d { get; set; } = new();
    public List<PostDisplayModel> ReplyTop7d { get; set; } = new();
    public List<PostDisplayModel> ReactionTopHistory { get; set; } = new();
    public List<PostDisplayModel> ReplyTopHistory { get; set; } = new();
}

// ============================================
// 数据库迁移相关模型
// ============================================

/// <summary>
/// 数据库版本信息
/// </summary>
public record DatabaseInfo
{
    public required int Version { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public required DateTimeOffset LastSyncAt { get; init; }
    public required DateTimeOffset InitializeAt { get; init; }
}

/// <summary>
/// 数据库迁移脚本
/// </summary>
public record DatabaseMigrationScript
{
    public required string ScriptFileName { get; init; }
    public required string ScriptSQLCommandContent { get; init; }
}

/// <summary>
/// 数据库迁移信息
/// </summary>
public record DatabaseMigrationInfo
{
    public required DatabaseInfo CurrentVersionInfo { get; init; }
    public required DatabaseInfo PredictVersionInfo { get; init; }
    public ImmutableList<DatabaseMigrationScript> MigrationScripts { get; init; }
        = ImmutableList<DatabaseMigrationScript>.Empty;
}