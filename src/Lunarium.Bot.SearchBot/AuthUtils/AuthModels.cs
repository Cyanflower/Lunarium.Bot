

namespace Lunarium.Bot.SearchBot.AuthUtils;

public class AuthorizationResult
{
    public AuthorizationStatus Status { get; init; }
    public AuthorizationConditions ActualConditions { get; init; }
}

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