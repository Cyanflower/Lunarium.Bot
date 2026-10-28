namespace Lunarium.Bot.Archive.Model;

/// <summary>
/// 配置项的合法取值范围。
/// <para>
/// 集中定义，供 <see cref="ArchiveConfig.Validate"/> 与斜杠命令的参数校验共用，
/// 避免同一区间在多处硬编码后各自漂移。
/// </para>
/// <para>
/// 注意：<c>[Summary]</c> 等特性的参数必须是编译期常量（且不能对 int 做常量插值），
/// 所以命令参数说明里的区间文本仍需手工书写，改区间时记得同步。
/// </para>
/// </summary>
public static class ArchiveConfigLimits
{
    /// <summary>活跃线程上限下界（含）。</summary>
    public const int LimitInclusiveMin = 900;

    /// <summary>活跃线程上限上界（不含）。Discord 的活跃线程上限是 1000，此处留出缓冲区。</summary>
    public const int LimitExclusiveMax = 1000;

    /// <summary>默认归档间隔（分钟）。</summary>
    public const int DefaultIntervalMin = 10;

    /// <summary>归档间隔下界（含，分钟）。</summary>
    public const int IntervalInclusiveMin = 5;

    /// <summary>归档间隔上界（含，分钟）。</summary>
    public const int IntervalInclusiveMax = 1440;

    /// <summary>活跃线程上限是否合法（900 ≤ limit &lt; 1000）。</summary>
    public static bool IsValidLimit(int limit) => limit is >= LimitInclusiveMin and < LimitExclusiveMax;

    /// <summary>归档间隔是否合法（5 ≤ minutes ≤ 1440）。</summary>
    public static bool IsValidInterval(int minutes) => minutes is >= IntervalInclusiveMin and <= IntervalInclusiveMax;
}
