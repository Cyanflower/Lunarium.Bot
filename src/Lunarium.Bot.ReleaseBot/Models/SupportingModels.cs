using System.Collections.Immutable;
using Discord.Rest;

namespace Lunarium.Bot.ReleaseBot.Models;

// ============================================
// 服务结果
// ============================================

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

// ============================================
// 枚举类型
// ============================================

// 与数据库数据关联
// 绝对避免修改值, 新追加应当只在现有值之后
public enum ReleaseModel
{
    NoLimit = 10,
    Reaction = 15,
    ReactionOrReply = 20,
    Password = 30,
    RoRAndPwd = 40
}

/// <summary>
/// 处理结果状态
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// 成功
    /// </summary>
    Success,
    /// <summary>
    /// 输入不合法或不符合规则
    /// </summary>
    InvalidInput,
    // --- 业务失败 ---
    /// <summary>
    /// INSERT 冲突 (关系性数据已存在)
    /// </summary>
    Conflict,
    /// <summary>
    /// 未查询到任何结果
    /// </summary>
    NotFound,
    /// <summary>
    /// 预检查已存在 (关系性数据已存在)
    /// </summary>
    Exist,
    // --- 系统失败 ---
    InternalError
}