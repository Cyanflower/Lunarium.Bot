using System.Collections.Immutable;
using Discord.Rest;

namespace Lunarium.Bot.SearchBot.Models;


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
/// 帖子的处理结果状态
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


// ============================================
// 枚举类型
// ============================================

public struct Jsonb<T>
{
    public T? Value { get; set; }

    public Jsonb(T? value) => Value = value;

    // 为了方便使用，重载隐式转换
    public static implicit operator T?(Jsonb<T> j) => j.Value;
    public static implicit operator Jsonb<T>(T? v) => new Jsonb<T>(v);

    // 重写 ToString 方便调试
    public override string? ToString() => Value?.ToString();
}



public class ForumData
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
    public Dictionary<TagData, int> TagData { get; set; } = new();
    public Dictionary<TagData, int> TagDataHistory { get; set; } = new();

    // 焦点帖子
    public List<PostDisplayModel> ReactionTop7d { get; set; } = new();
    public List<PostDisplayModel> ReplyTop7d { get; set; } = new();
    public List<PostDisplayModel> ReactionTopHistory { get; set; } = new();
    public List<PostDisplayModel> ReplyTopHistory { get; set; } = new();
}
