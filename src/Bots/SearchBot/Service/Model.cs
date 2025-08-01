namespace SearchBot.Model;

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
    public ulong GuildId { get; set; }         // 服务器ID
    public long CreatedAt { get; set; } // 创建时间 (使用 DateTimeOffset 更专业)
    public int MemberCount { get; set; }       // 成员数量
    public int ReactionCount { get; set; }     // 回应数量
    public int ReplyCount { get; set; }        // 回复数量
    public long LastActiveAt { get; set; } // 最后活跃时间
    public string JumpUrl { get; set; } = "";  // 跳转链接
    public string? AttachmentUrl { get; set; } = ""; // 附件链接
    public List<ulong> Tags { get; set; } = new List<ulong>(); // 标签
    public string Title { get; set; } = "";    // 标题
    public string Content { get; set; } = "";  // 内容
}

/// <summary>
/// 收藏夹是否存在标识
/// </summary>
public enum FavoriteType
{
    Exist,
    New
}

/// <summary>
/// 扫描全部结果数据
/// </summary>
public class ScanResult
{
    public int ScanAllResult { get; set; }
    public ThreadProcessResult ThreadProcessResult { get; set; } = new ThreadProcessResult();
    public InsertPostResult InsertPostResult { get; set; } = new InsertPostResult();
    public int ScanObsoleteResult { get; set; }
    public int ScanRemoveResult { get; set; }
}

public class InsertPostResult
{
    public int MainAffectedRows { get; set; }
    public int FtsAffectedRows { get; set; }
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
