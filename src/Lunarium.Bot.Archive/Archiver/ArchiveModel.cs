using Discord.Rest;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Framework.OpenAPI;

namespace Lunarium.Bot.Archive.Archiver;

/// <summary>单个服务器一轮归档的上下文（配置快照 + 置顶监控白名单快照）。</summary>
public sealed record GuildContext(
    SocketGuild Guild,
    GuildConfig Config,
    IReadOnlySet<ulong> PinnedChannels)
{
    public int ActiveLimit => Config.ArchiveLimit;
}

// 步骤1：GetEligibleChannelsFromGuild
public sealed record ActiveThreads(IReadOnlyList<ActiveThreadSnapshot> Items);

// 步骤2：ClassifyThreadsByPinnedStatus
public sealed record ClassifiedThreads(
    IReadOnlyList<ActiveThreadSnapshot> Pinned,
    IReadOnlyList<ActiveThreadSnapshot> Regular);

// 步骤3：PinnedMonitoring.ReconcileAsync 的返回契约。
// Archiver 只拿它打日志，不参与归档决策——置顶帖在上游已与归档候选彻底分流。
// 每个监控频道一条记录，Kind 表示本轮相对上一轮的变动：New / Lost / Updated / Unchanged。
public record PinnedReconcileResult(IReadOnlyList<PinnedChange> Items, bool Failed = false)
{
    // 各状态数量
    public int NewCount => Items.Count(x => x.Kind == PinnedChangeKind.New);
    public int LostCount => Items.Count(x => x.Kind == PinnedChangeKind.Lost);
    public int UpdatedCount => Items.Count(x => x.Kind == PinnedChangeKind.Updated);
    public int UnchangedCount => Items.Count(x => x.Kind == PinnedChangeKind.Unchanged);

    /// <summary>本轮是否存在任何变化（有变化才需要发通知）。</summary>
    public bool HasChange => Items.Any(x => x.Kind != PinnedChangeKind.Unchanged);
}

// 步骤4：GetThreadsWithLastActivityTime
// Unreadable = 本轮拿不到最后活跃时间（无消息历史权限 / 读取失败 / 帖子无消息），
// 这类线程按最活跃处理，本轮不会参与归档。
public sealed record TimedThread(
    RestThreadChannel Thread,
    DateTimeOffset LastMessageAt,
    bool Unreadable = false);

// 步骤5：ArchiveExcessiveThreads 的单个结果
public sealed record ArchiveItemResult(
    RestThreadChannel Thread,
    DateTimeOffset LastActiveAt,
    bool Success,
    Exception? Error = null);

// 步骤5 的整体结果
public sealed record ArchiveOutcome(
    int TotalActiveThreads,
    int PinnedCount,
    IReadOnlyList<ArchiveItemResult> Items,
    bool HasExcessThreads,
    int PlannedCount = 0,
    int UnreadableCount = 0);
