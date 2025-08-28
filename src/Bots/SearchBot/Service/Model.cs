using Discord;
using Discord.Interactions;
using System.Text;

namespace SearchBot.Model;

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

/// <summary>
/// 帖子数据模型
/// 从IThreadChannel解析填入而来
/// </summary>
/// <summary>
/// 数据模型: 帖子信息
/// 用于封装和传递从数据库或API获取的帖子数据。
/// </summary>
public class ThreadDataModel
{
    // --- 定义模型属性 ---
    public ulong ThreadId { get; set; }        // 帖子ID (使用 ulong 更安全，见下方小贴士)
    public ulong FirstMessageId { get; set; }  // 首条消息ID
    public ulong AuthorId { get; set; }        // 作者ID
    public ulong ChannelId { get; set; }       // 频道ID
    public string ChannelName { get; set; } = "";
    public ulong GuildId { get; set; }         // 服务器ID
    public string GuildName { get; set; } = "";
    public long CreatedAt { get; set; } // 创建时间 (使用 DateTimeOffset 更专业)
    public int MemberCount { get; set; }       // 成员数量
    public int ReactionCount { get; set; }     // 回应数量
    public int ReplyCount { get; set; }        // 回复数量
    public long LastActiveAt { get; set; } // 最后活跃时间
    public string JumpUrl { get; set; } = "";  // 跳转链接
    public string? AttachmentUrl { get; set; } = ""; // 附件链接
    public string Tags { get; set; } = ""; // 标签
    public List<ulong> TagsId { get; set; } = new();
    public string Title { get; set; } = "";    // 标题
    public string Content { get; set; } = "";  // 内容
}

/// <summary>
/// 扫描全部结果数据
/// </summary>
public class ScanResult
{
    /// <summary>
    /// 扫描全部结果数量。
    /// </summary>
    public int ScanAllResult { get; set; }
    /// <summary>
    /// 并发帖子处理结果。
    /// </summary>
    public ThreadProcessResult ThreadProcessResult { get; set; } = new ThreadProcessResult();
    /// <summary>
    /// 数据库写入结果。
    /// </summary>
    public InsertPostResult InsertPostResult { get; set; } = new InsertPostResult();
    /// <summary>
    /// 扫描过时帖子结果。
    /// </summary>
    public int ScanObsoleteResult { get; set; }
    /// <summary>
    /// 删除过时帖子结果。
    /// </summary>
    public int ScanRemoveResult { get; set; }
}

public class InsertPostResult
{
    /// <summary>
    /// 主表受影响的行数。
    /// </summary>
    public int MainAffectedRows { get; set; }
    /// <summary>
    /// 全文索引表受影响的行数。
    /// </summary>
    public int FtsAffectedRows { get; set; }
    /// <summary>
    /// 标签连接表受影响的行数。
    /// </summary>
    public int TagJunctionAffectedRows { get; set; }
}

/// <summary>
/// 帖子处理结果数据
/// </summary>
public class ThreadProcessResult
{
    /// <summary>
    /// 成功处理的任务数量。
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 处理失败的任务数量。
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 被跳过处理的任务数量。
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 尝试处理的总任务数量 (等于 成功 + 失败 + 跳过)。
    /// </summary>
    public int ThreadsCount { get; set; }

    /// <summary>
    /// 完成所有任务所花费的总时间，单位为秒。
    /// </summary>
    public double TotalElapsed { get; set; }

    /// <summary>
    /// 整体的平均处理速度，单位为“任务/秒”。
    /// </summary>
    public double OverallAvgSpeed { get; set; }
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
/// 用户推送列表的辅助类
/// </summary>
public class PushListInfo
{
    public ulong ThreadId { get; set; }
    public string MessageJumpUrl { get; set; } = "";
    public long UpdatedAt { get; set; }
}

/// <summary>
/// 用户收藏帖子关系表的辅助类
/// </summary>
public class FavoriteInfo
{
    public ulong ThreadId { get; set; }
    public long AddedAt { get; set; }
    public string? FavoriteAS { get; set; }
}

/// <summary>
/// 分页状态类，用于存储分页数据和过期时间
/// </summary>
public class PaginatorState
{
    public List<List<Embed>> Pages { get; init; } = new();
    public DateTime ExpiryTime { get; init; } // 记下过期时间点
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

/// <summary>
/// 用于接收模态框数据的辅助类
/// </summary>
public class JumpPageModal : IModal
{
    public string Title => "跳转到页面";

    [InputLabel("页码")]
    [ModalTextInput("page_number_input", TextInputStyle.Short, "请输入目标页码")]
    public string PageNumber { get; set; } = "";
}

/// <summary>
/// 封装所有搜索条件的模型
/// 这个模型是用户输入经过解析后的最终产物，可以直接用于构建数据库查询。
/// 它可以被序列化为JSON，用于实现“快捷搜索”功能。
/// </summary>
public class SearchCriteria
{
    // --- 频道/作者 过滤 ---
    // 改为List<ulong>以支持多频道OR查询
    public List<ulong> ForumChannelIds { get; set; } = new();
    public ulong? AuthorId { get; set; }

    // --- 关键词 (FTS) ---
    // 每个 List<string> 代表一个 OR 分组, List 内部的 string 是 AND 关系
    // 例如: { {"cat", "cute"}, {"dog"} } -> (cat AND cute) OR (dog)
    public List<List<string>> IncludeKeywords { get; set; } = new();
    public List<List<string>> ExcludeKeywords { get; set; } = new();

    // --- 标签 (使用解析后的Tag ID) ---
    // 从数据库中找到所有同名tag的ID，放进这里
    public List<ulong> IncludeTagsAnd { get; set; } = new(); // 必须全部包含 (AND)
    public List<ulong> IncludeTagsOr { get; set; } = new();  // 包含任意一个 (OR)
    public List<ulong> ExcludeTags { get; set; } = new();    // 不能包含任意一个 (NOT IN)

    // --- 排除作者 ---
    public List<ulong> ExcludeAuthorIds { get; set; } = new();

    // --- 时间范围 (Unix时间戳) ---
    public long? StartTimestamp { get; set; }
    public long? EndTimestamp { get; set; }

    // --- 排序与分页 ---
    public string OrderBy { get; set; } = "posted_date_desc";
    public int ResultLimit { get; set; } = 200;
}

/// <summary>
/// 用于存储和读取快捷搜索命令的模型
/// </summary>
public class QuickSearchCommandModel
{
    public ulong UserId { get; set; }
    public string CommandName { get; set; } = "";
    public string CriteriaJson { get; set; } = "";
}

/// <summary>
/// 辅助类，用于将 SearchCriteria 映射为用户友好的命令字符串。
/// </summary>
/// <summary>
/// 辅助类，用于将 SearchCriteria 映射为用户友好的命令字符串。
/// </summary>
public static class SearchParameterMapper
{
    // 映射表保持不变
    private static readonly Dictionary<string, string> PropertyToParameterMap = new()
    {
        { nameof(SearchCriteria.ForumChannelIds), "论坛分区" },
        { nameof(SearchCriteria.AuthorId), "帖子作者" },
        // ✨ 为了更清晰地展示，我们将关键词和标签细分
        { "IncludeKeywords1", "关键词1" },
        { "IncludeKeywords2", "关键词2" },
        { "IncludeKeywords3", "关键词3" },
        { "ExcludeKeywords1", "排除关键词1" },
        { "ExcludeKeywords2", "排除关键词2" },
        { "ExcludeKeywords3", "排除关键词3" },
        { nameof(SearchCriteria.IncludeTagsAnd), "包含标签-AND包含组-" },
        { nameof(SearchCriteria.IncludeTagsOr), "包含标签-OR包含组-" },
        { nameof(SearchCriteria.ExcludeTags), "排除标签-OR排除组-" },
        { nameof(SearchCriteria.ExcludeAuthorIds), "排除作者" },
        { nameof(SearchCriteria.StartTimestamp), "起始日期" },
        { nameof(SearchCriteria.EndTimestamp), "结束日期" },
        { nameof(SearchCriteria.OrderBy), "排序方式" },
        { nameof(SearchCriteria.ResultLimit), "结果上限" }
    };
    
    // ToDisplayLabel 方法保持原样，它作为摘要非常出色
    public static string ToDisplayLabel(SearchCriteria? criteria)
    {
        if (criteria == null) return "参数解析失败";
        
        var parts = new List<string>();
        
        if (criteria.ForumChannelIds.Any()) parts.Add($"分区:{criteria.ForumChannelIds.Count}个");
        if (criteria.AuthorId.HasValue) parts.Add($"作者:<@{criteria.AuthorId.Value}>");
        if (criteria.IncludeKeywords.Any()) parts.Add($"关键词:{criteria.IncludeKeywords.Sum(g => g.Count)}个");
        if (criteria.IncludeTagsAnd.Any() || criteria.IncludeTagsOr.Any()) parts.Add($"含标签:{criteria.IncludeTagsAnd.Count + criteria.IncludeTagsOr.Count}个");
        if (criteria.ExcludeKeywords.Any()) parts.Add($"除关键词:{criteria.ExcludeKeywords.Sum(g => g.Count)}个");
        if (criteria.ExcludeTags.Any()) parts.Add($"除标签:{criteria.ExcludeTags.Count}个");
        if (criteria.ExcludeAuthorIds.Any()) parts.Add($"除作者:{criteria.ExcludeAuthorIds.Count}个");
        if (criteria.StartTimestamp.HasValue) parts.Add($"始于:{DateTimeOffset.FromUnixTimeSeconds(criteria.StartTimestamp.Value):yyyy-MM-dd}");
        
        var label = string.Join(" ", parts);
        return string.IsNullOrEmpty(label) ? "无任何有效参数" : label;
    }
    
    // ✨ 全新实现的 ToCommandString 方法，用于精确展示
    public static string ToCommandString(SearchCriteria? criteria)
    {
        if (criteria == null) return "/搜索 [参数解析失败]";

        var sb = new StringBuilder("/搜索");

        // 辅助函数，用于添加参数到字符串
        void AppendParameter(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                sb.Append($" {name}:\"{value}\"");
            }
        }
        
        // ✨ 分别处理每个参数，展示具体内容
        if (criteria.AuthorId.HasValue) sb.Append($" {PropertyToParameterMap[nameof(criteria.AuthorId)]}:<@{criteria.AuthorId.Value}>");

        // 处理关键词 (这里我们假设IncludeKeywords列表的顺序对应keyWord1, 2, 3)
        for(int i = 0; i < criteria.IncludeKeywords.Count; i++)
        {
            AppendParameter(PropertyToParameterMap[$"IncludeKeywords{i+1}"], string.Join(" ", criteria.IncludeKeywords[i]));
        }
        for(int i = 0; i < criteria.ExcludeKeywords.Count; i++)
        {
            AppendParameter(PropertyToParameterMap[$"ExcludeKeywords{i+1}"], string.Join(" ", criteria.ExcludeKeywords[i]));
        }

        // 处理标签 (这里简化处理，实际效果如果不够好可能需要从ID反查名称)
        if (criteria.IncludeTagsAnd.Any()) sb.Append($" {PropertyToParameterMap[nameof(criteria.IncludeTagsAnd)]}:[包含 {criteria.IncludeTagsAnd.Count} 个标签]");
        if (criteria.IncludeTagsOr.Any()) sb.Append($" {PropertyToParameterMap[nameof(criteria.IncludeTagsOr)]}:[包含 {criteria.IncludeTagsOr.Count} 个标签]");
        if (criteria.ExcludeTags.Any()) sb.Append($" {PropertyToParameterMap[nameof(criteria.ExcludeTags)]}:[排除 {criteria.ExcludeTags.Count} 个标签]");

        // 处理其他参数
        if (criteria.StartTimestamp.HasValue) AppendParameter(PropertyToParameterMap[nameof(criteria.StartTimestamp)], DateTimeOffset.FromUnixTimeSeconds(criteria.StartTimestamp.Value).ToString("yyyy-MM-dd"));
        if (criteria.EndTimestamp.HasValue) AppendParameter(PropertyToParameterMap[nameof(criteria.EndTimestamp)], DateTimeOffset.FromUnixTimeSeconds(criteria.EndTimestamp.Value).ToString("yyyy-MM-dd"));
        
        // 对于默认值，可以不显示
        if (criteria.OrderBy != "posted_date_desc") AppendParameter(PropertyToParameterMap[nameof(criteria.OrderBy)], criteria.OrderBy);
        if (criteria.ResultLimit != 200) AppendParameter(PropertyToParameterMap[nameof(criteria.ResultLimit)], criteria.ResultLimit.ToString());

        return sb.ToString();
    }
}