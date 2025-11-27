using System.Collections.Immutable;
using Discord.Rest;

namespace LunariumBot.Net.SearchBot.Models;


public class ServiceResult
{
    public ServiceStatus Status { get; init; }
    public string? Message { get; init; }
}

public class ServiceResult<T> where T : class
{
    public ServiceStatus Status { get; init; }
    public T? Payload { get; init; }
    public string? Message { get; init; }
}

public class ExistenceCheckResult<T> where T : class
{
    public bool Exists { get; init; }
    public T? Payload { get; init; }
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


// ============================================
// 枚举类型
// ============================================

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
    Exist,
    ValidationFailed,
    // ... 其他业务失败

    // --- 系统失败 ---
    InternalError
}


/// <summary>
/// 数值更新类型
/// </summary>
public enum NumericUpdateType
{
    Increase,  // 增加
    Decrease   // 减少
}