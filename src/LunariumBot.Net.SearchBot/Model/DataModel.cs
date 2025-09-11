// .NET 核心库
using System.Text;
// NuGet 包依赖
using Discord;
using Discord.Interactions;

namespace LunariumBot.Net.SearchBot.Model;

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
/// 一个轻量级模型，用于在删除帖子前从数据库获取必要的统计信息。
/// </summary>
public class PostInfoForStats
{
    public ulong ThreadId { get; set; }
    public long CreatedAt { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public int ReplyCount { get; set; }
}

public class TagInfo
{
    public string? TagId { get; set; } = "";
    public ulong? ChannelId { get; set; }
    public string? TagName { get; set; } = "";
    public string? ChannelName { get; set; } = "";
    public string? GuildName { get; set; } = "";
    public int UsageCount { get; set; }
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
    public DateTime ExpiryTime { get; init; } = DateTime.UtcNow.AddMinutes(15);
}

/// <summary>
/// 用于支持交互式分页（如带删除按钮）的状态模型
/// </summary>
public class InteractivePaginatorState
{
    // 存储所有帖子的原始数据
    private List<PostDisplayModel> AllPosts { get; set; }
    // 存储所有帖子的Embed表示
    private List<Embed> AllEmbeds { get; set; }
    
    public DateTime ExpiryTime { get; init; }
    
    // 每页显示的数量 (硬编码或从配置读取)
    public int ChunkSize { get; } = 8; // 每页x个

    // 这个分页器的类型，用于生成正确的删除按钮 customId
    public PaginatorType Type { get; }

    public InteractivePaginatorState(List<PostDisplayModel> posts, List<Embed> embeds, PaginatorType type)
    {
        AllPosts = posts;
        AllEmbeds = embeds;
        Type = type;
        ExpiryTime = DateTime.UtcNow.AddMinutes(15);
    }
    
    /// <summary>
    /// 获取总页数
    /// </summary>
    public int GetTotalPages() => (int)Math.Ceiling((double)AllPosts.Count / ChunkSize);
    
    /// <summary>
    /// 获取指定页的数据 "虚拟切片"
    /// </summary>
    /// <param name="pageNumber">页码 (从1开始)</param>
    /// <returns>当前页的帖子数据和Embeds</returns>
    public (List<PostDisplayModel> Posts, List<Embed> Embeds) GetPage(int pageNumber)
    {
        var skip = (pageNumber - 1) * ChunkSize;
        var posts = AllPosts.Skip(skip).Take(ChunkSize).ToList();
        var embeds = AllEmbeds.Skip(skip).Take(ChunkSize).ToList();
        return (posts, embeds);
    }
    
    /// <summary>
    /// 根据 ThreadId 移除一个项目
    /// </summary>
    /// <param name="threadId">要移除的帖子ID</param>
    /// <returns>如果成功移除则返回 true</returns>
    public bool RemoveItem(ulong threadId)
    {
        // 找到要删除的帖子索引
        var index = AllPosts.FindIndex(p => p.ThreadId == threadId);
        
        if (index != -1)
        {
            // 同步移除两个列表中的对应项
            AllPosts.RemoveAt(index);
            AllEmbeds.RemoveAt(index);
            return true;
        }
        
        return false;
    }
}

/// <summary>
/// 定义分页器的类型
/// </summary>
public enum PaginatorType
{
    Favorite,
    Subscription
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
    // "AND 包含": 每个内部 list 代表一个标签概念 (OR)，外部 list 代表 AND
    public List<List<ulong>> IncludeTagsAnd { get; set; } = new(); 
    // "OR 包含": 扁平化的列表
    public List<ulong> IncludeTagsOr { get; set; } = new();
    // "AND 排除": 结构同 "AND 包含"
    public List<List<ulong>> ExcludeTagsAnd { get; set; } = new();
    // "OR 排除": 扁平化的列表
    public List<ulong> ExcludeTagsOr { get; set; } = new();

    // --- 排除作者 ---
    public List<ulong> ExcludeAuthorIds { get; set; } = new();

    // --- 时间范围 (Unix时间戳) ---
    public long? StartTimestamp { get; set; }
    public long? EndTimestamp { get; set; }

    // --- 排序与分页 ---
    public string OrderBy { get; set; } = "posted_date_desc";
    public int ResultLimit { get; set; } = 200;
    
    /// <summary>
    /// 检查是否所有过滤条件都为空。
    /// 注意：不检查 OrderBy 和 ResultLimit，因为它们不是“过滤”条件。
    /// </summary>
    /// <returns>如果没有任何有效的过滤条件，则返回 true。</returns>
    public bool IsEmpty()
    {
        return ForumChannelIds.Count == 0 &&
               !AuthorId.HasValue &&
               IncludeKeywords.Count == 0 &&
               ExcludeKeywords.Count == 0 &&
               // ⬇️⬇️⬇️ **更新这里的判断** ⬇️⬇️⬇️
               IncludeTagsAnd.Count == 0 &&
               IncludeTagsOr.Count == 0 &&
               ExcludeTagsAnd.Count == 0 &&
               ExcludeTagsOr.Count == 0 &&
               ExcludeAuthorIds.Count == 0 &&
               !StartTimestamp.HasValue &&
               !EndTimestamp.HasValue;
    }
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
/// 辅助类，用于将 SearchCriteria 映射为用户友好的文本。
/// </summary>
public static class SearchParameterMapper
{
    /// <summary>
    /// 生成一个简洁的、用于UI（如下拉菜单描述）的摘要标签。
    /// </summary>
    /// <param name="criteria">搜索条件对象</param>
    /// <returns>一行简洁的摘要文本</returns>
    public static string ToDisplayLabel(SearchCriteria? criteria)
    {
        if (criteria == null) return "参数解析失败";

        var parts = new List<string>();

        // 这里我们不能反向解析出分区名，但可以表明设置了分区
        if (criteria.ForumChannelIds.Any()) parts.Add("分区"); 
        if (criteria.AuthorId.HasValue) parts.Add($"作者:<@{criteria.AuthorId.Value}>");
        if (criteria.IncludeKeywords.Any()) parts.Add($"关键词:{criteria.IncludeKeywords.Sum(g => g.Count)}个");
        
        // 智能计算标签总数
        int includeTagCount = criteria.IncludeTagsAnd.Count + criteria.IncludeTagsOr.Count;
        if (includeTagCount > 0)
        {
            string mode = criteria.IncludeTagsAnd.Any() ? "(AND)" : "(OR)";
            parts.Add($"含标签:{includeTagCount}个{mode}");
        }

        int excludeTagCount = criteria.ExcludeTagsAnd.Count + criteria.ExcludeTagsOr.Count;
        if (excludeTagCount > 0)
        {
            string mode = criteria.ExcludeTagsAnd.Any() ? "(AND)" : "(OR)";
            parts.Add($"除标签:{excludeTagCount}个{mode}");
        }

        if (criteria.ExcludeKeywords.Any()) parts.Add($"除关键词:{criteria.ExcludeKeywords.Sum(g => g.Count)}个");
        if (criteria.ExcludeAuthorIds.Any()) parts.Add($"除作者:{criteria.ExcludeAuthorIds.Count}个");
        if (criteria.StartTimestamp.HasValue) parts.Add($"始于:{DateTimeOffset.FromUnixTimeSeconds(criteria.StartTimestamp.Value).ToString("yyyy-MM-dd")}");
        if (criteria.EndTimestamp.HasValue) parts.Add($"止于:{DateTimeOffset.FromUnixTimeSeconds(criteria.EndTimestamp.Value).ToString("yyyy-MM-dd")}");

        var label = string.Join(" | ", parts); // 使用 | 分隔符更清晰
        return string.IsNullOrEmpty(label) ? "无任何有效参数" : label;
    }

    /// <summary>
    /// 生成一个详细的、类似命令格式的字符串，用于删除确认等场景。
    /// 注意：由于无法从Tag ID反向解析出原始名称，这里会用数量和模式来表示。
    /// </summary>
    /// <param name="criteria">搜索条件对象</param>
    /// <returns>一个多行的、详细的参数描述</returns>
    public static string ToCommandString(SearchCriteria? criteria)
    {
        if (criteria == null) return "参数解析失败";

        var sb = new StringBuilder();

        // 辅助函数，用于添加参数到字符串构建器
        void AppendParameter(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                sb.AppendLine($"• **{name}:** {value}");
            }
        }
        
        // 无法反查分区名，但可以告知用户设置了分区
        if (criteria.ForumChannelIds.Any()) sb.AppendLine("• **论坛分区:** [已设置]");
        if (criteria.AuthorId.HasValue) AppendParameter("帖子作者", $"<@{criteria.AuthorId.Value}>");

        // 处理关键词
        for (int i = 0; i < criteria.IncludeKeywords.Count; i++)
        {
            AppendParameter($"关键词{i + 1}", $"`{string.Join(" AND ", criteria.IncludeKeywords[i])}`");
        }
        for (int i = 0; i < criteria.ExcludeKeywords.Count; i++)
        {
            AppendParameter($"排除关键词{i + 1}", $"`{string.Join(" OR ", criteria.ExcludeKeywords[i])}`");
        }

        // --- 全新的、智能的标签处理逻辑 ---
        if (criteria.IncludeTagsAnd.Any())
        {
            AppendParameter("包含标签 (模式: AND)", $"{criteria.IncludeTagsAnd.Count} 个");
        }
        else if (criteria.IncludeTagsOr.Any())
        {
            AppendParameter("包含标签 (模式: OR)", $"{criteria.IncludeTagsOr.Count} 个");
        }

        if (criteria.ExcludeTagsAnd.Any())
        {
            AppendParameter("排除标签 (模式: AND)", $"{criteria.ExcludeTagsAnd.Count} 个");
        }
        else if (criteria.ExcludeTagsOr.Any())
        {
            AppendParameter("排除标签 (模式: OR)", $"{criteria.ExcludeTagsOr.Count} 个");
        }
        
        // 处理其他参数
        if (criteria.ExcludeAuthorIds.Any()) AppendParameter("排除作者", string.Join(", ", criteria.ExcludeAuthorIds.Select(id => $"<@{id}>")));
        if (criteria.StartTimestamp.HasValue) AppendParameter("起始日期", DateTimeOffset.FromUnixTimeSeconds(criteria.StartTimestamp.Value).ToString("yyyy-MM-dd"));
        if (criteria.EndTimestamp.HasValue) AppendParameter("结束日期", DateTimeOffset.FromUnixTimeSeconds(criteria.EndTimestamp.Value).ToString("yyyy-MM-dd"));
        if (criteria.OrderBy != "posted_date_desc") AppendParameter("排序方式", $"`{criteria.OrderBy}`");
        if (criteria.ResultLimit != 200) AppendParameter("结果上限", criteria.ResultLimit.ToString());

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? "无任何有效参数" : result.TrimEnd();
    }
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