namespace Lunarium.Bot.Ticket.Model;

/// <summary>
/// 配置项的合法取值范围。
/// <para>
/// 集中定义，供 <c>GuildConfig</c> 的校验与斜杠命令的参数校验共用，
/// 避免同一区间在多处硬编码后各自漂移。
/// </para>
/// <para>
/// 注意：该类的常量也与斜杠命令的参数说明（<c>[Summary]</c>）保持一致，
/// 改区间时请记得同步——特性参数必须是编译期常量，无法直接引用这里的值。
/// </para>
/// </summary>
public static class TicketConfigLimits
{
    /// <summary>工单冷却时间下界（含，小时）。</summary>
    public const int TicketCdInclusiveMin = 1;

    /// <summary>工单冷却时间上界（含，小时）。30 天。</summary>
    public const int TicketCdInclusiveMax = 720;

    /// <summary>工单冷却时间是否合法（1 ≤ hours ≤ 720）。</summary>
    public static bool IsValidTicketCd(int hours) => hours is >= TicketCdInclusiveMin and <= TicketCdInclusiveMax;

    /// <summary>待审核工单自动通过时间下界（含，小时）。<c>0</c> 表示不自动通过。</summary>
    public const int AutoApproveHoursInclusiveMin = 0;

    /// <summary>待审核工单自动通过时间上界（含，小时）。30 天。</summary>
    public const int AutoApproveHoursInclusiveMax = 720;

    /// <summary>待审核工单自动通过时间是否合法（0 ≤ hours ≤ 720，<c>0</c> 表示不自动通过）。</summary>
    public static bool IsValidAutoApproveHours(int hours) => hours is >= AutoApproveHoursInclusiveMin and <= AutoApproveHoursInclusiveMax;

    /// <summary>自动安抚延迟下界（含，分钟）。</summary>
    public const int SootheDelayMinutesInclusiveMin = 5;

    /// <summary>自动安抚延迟上界（含，分钟）。7 天。</summary>
    public const int SootheDelayMinutesInclusiveMax = 10080;

    /// <summary>自动安抚延迟是否合法（5 ≤ minutes ≤ 10080）。</summary>
    public static bool IsValidSootheDelayMinutes(int minutes) => minutes is >= SootheDelayMinutesInclusiveMin and <= SootheDelayMinutesInclusiveMax;

    /// <summary>归档后删除频道的倒计时下界（含，秒）。</summary>
    public const int ArchiveCountdownInclusiveMin = 5;

    /// <summary>归档后删除频道的倒计时上界（含，秒）。10 分钟。</summary>
    public const int ArchiveCountdownInclusiveMax = 600;

    /// <summary>归档删除倒计时是否合法（5 ≤ seconds ≤ 600）。</summary>
    public static bool IsValidArchiveCountdown(int seconds) => seconds is >= ArchiveCountdownInclusiveMin and <= ArchiveCountdownInclusiveMax;

    /// <summary>临时冷却许可时长下界（含，小时）。</summary>
    public const int TempPermissionHoursInclusiveMin = 1;

    /// <summary>临时冷却许可时长上界（含，小时）。3 天。</summary>
    public const int TempPermissionHoursInclusiveMax = 72;

    /// <summary>临时冷却许可时长是否合法（1 ≤ hours ≤ 72）。</summary>
    public static bool IsValidTempPermissionHours(int hours) => hours is >= TempPermissionHoursInclusiveMin and <= TempPermissionHoursInclusiveMax;

}
