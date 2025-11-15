using System.Collections.Immutable;
using Discord.Rest;

namespace LunariumBot.Net.SearchBot.Model;


public class AuthorizationResult
{
    public AuthorizationStatus Status { get; init; }
    public AuthorizationConditions ActualConditions { get; init; }
}

public class ServiceResult
{
    public ServiceStatus Status { get; init; }
    public string? Message { get; init; }
}

// ============================================
// 枚举类型
// ============================================

/// <summary>
/// 定义前置条件检查结果的各种状态。
/// </summary>
public enum AuthorizationStatus
{
    /// <summary>
    /// 检查通过
    /// </summary>
    Success,

    /// <summary>
    /// 命令在未被授权的服务器(Guild)中使用
    /// </summary>
    NotAuthorizedGuild,

    /// <summary>
    /// 执行用户缺少必要的权限
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// 命令在错误的频道中使用
    /// </summary>
    NotPermittedChannel,

    /// <summary>
    /// 系统失败
    /// </summary>
    InternalError
}

/// <summary>
/// 鉴权位标志枚举
/// </summary>
[Flags]
public enum AuthorizationConditions
{
    None = 0,

    /// <summary>
    /// 在已授权的服务器中
    /// </summary>
    IsInAuthorizedGuild = 1 << 0, // 1

    /// <summary>
    /// 用户拥有必要的权限(例如需要是管理员)
    /// </summary>
    HasRequiredPermission = 1 << 1, // 2

    /// <summary>
    /// 在允许的频道中
    /// </summary>
    IsInPermittedChannel = 1 << 2, // 4
    
    // 你可以根据需要添加更多条件...
    // IsUserNotMuted = 1 << 3,
    // IsCommandNotOnCooldown = 1 << 4,
}

/// <summary>
/// 帖子的处理结果状态
/// </summary>
public enum ServiceStatus
{
    Success,
    /// <summary>
    /// 输入不合法或不符合规则
    /// </summary>
    InvalidInput,
    /// <summary>
    /// 鉴权未通过
    /// </summary>
    PermissionDenied,
    // --- 业务失败 ---
    /// <summary>
    /// INSERT 冲突 (关系性数据已存在)
    /// </summary>
    Conflict,
    NotFound,
    ValidationFailed,
    // ... 其他业务失败

    // --- 系统失败 ---
    InternalError
}

public enum InsertResult
{
    Inserted,
    AlreadyExists
}

public enum DeleteResult
{
    Success,
    NoExists
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