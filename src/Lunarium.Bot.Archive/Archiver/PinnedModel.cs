using System.Text.Json.Serialization;

namespace Lunarium.Bot.Archive.Archiver;

/// <summary>当前认为仍应处于置顶状态的帖子。</summary>
public readonly record struct PinnedThread(ulong Id);

/// <summary>置顶帖所属的父频道。领域约束：一个父频道同时只能有一个置顶帖。</summary>
public readonly record struct PinnedChannel(ulong Id);

/// <summary>跟踪表的服务器键。</summary>
public readonly record struct GuildKey(ulong Id);

/// <summary>一条告警的唯一键：同一服务器 + 同一父频道只会存在一条告警。</summary>
public readonly record struct PinnedAlertKey(ulong GuildId, ulong ChannelId);

/// <summary>本轮扫描相对上一轮的变化。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PinnedChangeKind>))]
public enum PinnedChangeKind
{
    /// <summary>本轮新出现的置顶帖（含重启后首次建立基线）。</summary>
    New,

    /// <summary>上一轮还在、本轮消失的置顶帖。</summary>
    Lost,

    /// <summary>同一父频道换了置顶帖。</summary>
    Updated,

    /// <summary>与上一轮一致。</summary>
    Unchanged
}

/// <summary>告警的处理结果。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PinnedDecision>))]
public enum PinnedDecision
{
    /// <summary>待处理：等人工点按钮，或到期自动处理。</summary>
    Pending,

    /// <summary>人工选择恢复置顶。</summary>
    Restored,

    /// <summary>人工选择放弃（忽略掉置顶 / 保持新置顶帖）。</summary>
    Discarded,

    /// <summary>无人处理，到期自动处理。</summary>
    TimedOut
}

/// <summary>告警类型，决定到期自动处理时做什么。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PinnedAlertKind>))]
public enum PinnedAlertKind
{
    /// <summary>父频道里一个置顶帖都没有了 → 到期自动恢复置顶。</summary>
    Lost,

    /// <summary>置顶帖被替换 → 到期自动保持新置顶帖。</summary>
    Updated
}

/// <summary>本轮扫描结果里的一条记录。</summary>
/// <param name="ThreadId">当前置顶帖；<see cref="PinnedChangeKind.Lost"/> 时是丢失的那个帖。</param>
/// <param name="ChannelId">所属父频道。</param>
/// <param name="Kind">变化类型。</param>
/// <param name="PreviousThreadId">仅 <see cref="PinnedChangeKind.Updated"/> 有值：被替换掉的旧置顶帖。</param>
public record PinnedChange(
    ulong ThreadId,
    ulong ChannelId,
    PinnedChangeKind Kind,
    ulong PreviousThreadId = 0);

/// <summary>
/// 一条待处理的置顶告警。
/// <para>持久化模型：字段名会直接落盘，改名需要迁移。</para>
/// </summary>
public sealed class PinnedAlert
{
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    /// <summary>当前置顶帖；<see cref="PinnedAlertKind.Lost"/> 时是掉置顶的那个帖。</summary>
    public ulong ThreadId { get; set; }

    /// <summary>仅 <see cref="PinnedAlertKind.Updated"/> 有值：被替换掉的旧置顶帖。</summary>
    public ulong PreviousThreadId { get; set; }

    public PinnedAlertKind Kind { get; set; }

    /// <summary>告警卡片所在频道（配置变更后仍能编辑旧卡片）。</summary>
    public ulong StaffChannelId { get; set; }

    /// <summary>告警卡片消息 Id；0 表示卡片没发出去。</summary>
    public ulong MessageId { get; set; }

    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>到期时间；届时仍为 <see cref="PinnedDecision.Pending"/> 就按类型自动处理。</summary>
    public DateTimeOffset ExpireAt { get; set; }

    public PinnedDecision Decision { get; set; } = PinnedDecision.Pending;

    /// <summary>处理人；到期自动处理时为 0。</summary>
    public ulong ActorId { get; set; }

    /// <summary>恢复失败重试次数。</summary>
    public int Attempts { get; set; }

    [JsonIgnore]
    public PinnedAlertKey Key => new(GuildId, ChannelId);

    [JsonIgnore]
    public bool IsPending => Decision == PinnedDecision.Pending;

    /// <summary>要恢复的目标帖：<see cref="PinnedAlertKind.Updated"/> 恢复的是旧帖。</summary>
    [JsonIgnore]
    public ulong RestoreTargetId => PreviousThreadId != 0 ? PreviousThreadId : ThreadId;
}

/// <summary>一次按钮/到期处理的结果。</summary>
public sealed record PinnedDecisionResult(bool Success, string Message)
{
    public static PinnedDecisionResult Ok(string message) => new(true, message);

    public static PinnedDecisionResult Fail(string message) => new(false, message);
}

/// <summary>置顶监控（跟踪 + 告警）的持久化文档。</summary>
public sealed class PinnedMonitoringDocument
{
    public int Version { get; set; } = 1;

    public List<PinnedGuildEntry> Guilds { get; set; } = [];

    public List<PinnedAlert> Alerts { get; set; } = [];
}

/// <summary>一个服务器跟踪中的置顶帖。</summary>
public sealed class PinnedGuildEntry
{
    public ulong GuildId { get; set; }

    public List<PinnedChannelEntry> Channels { get; set; } = [];
}

/// <summary>一个父频道下的置顶帖。</summary>
public sealed class PinnedChannelEntry
{
    public ulong ChannelId { get; set; }

    public ulong ThreadId { get; set; }
}

/// <summary>置顶监控的源生成 JSON 上下文（AOT/裁剪下必须走 TypeInfo 重载）。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PinnedMonitoringDocument))]
internal partial class PinnedJsonContext : JsonSerializerContext
{
}
