using Discord.Rest;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Model;

namespace Lunarium.Bot.Archive.Archiver;

/// <summary>单个服务器一轮归档的上下文（配置快照）。</summary>
public sealed record GuildContext(SocketGuild Guild, GuildConfig Config)
{
    public int ActiveLimit => Config.ArchiveLimit;
}

// 步骤1：GetEligibleChannelsFromGuild
public sealed record ActiveThreads(IReadOnlyList<RestThreadChannel> Items);

// 步骤2：GetThreadsWithLastActivityTime
// Unreadable = 本轮拿不到最后活跃时间（无消息历史权限 / 读取失败 / 帖子无消息），
// 这类线程按最活跃处理，本轮不会参与归档。
public sealed record TimedThread(
    RestThreadChannel Thread,
    DateTimeOffset LastMessageAt,
    bool Unreadable = false);

// 步骤3：ArchiveExcessiveThreads 的单个结果
public sealed record ArchiveItemResult(
    RestThreadChannel Thread,
    DateTimeOffset LastActiveAt,
    bool Success,
    Exception? Error = null);

// 步骤3 的整体结果
public sealed record ArchiveOutcome(
    int TotalActiveThreads,
    IReadOnlyList<ArchiveItemResult> Items,
    bool HasExcessThreads,
    int PlannedCount = 0,
    int UnreadableCount = 0);
