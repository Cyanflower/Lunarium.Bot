namespace Lunarium.Bot.Logger.GlobalConfig;

/// <summary>
/// 日志时间戳模式
/// </summary>
internal enum LogTimestampMode
{
    Local,  // default
    Utc,
    Custom
}

/// <summary>
/// 日志时间全局配置
/// </summary>
internal static class LogTimestampConfig
{
    /// <summary>
    /// 日志时间戳模式（默认为 UTC）
    /// </summary>
    internal static LogTimestampMode Mode { get; private set; } = LogTimestampMode.Local;
    
    /// <summary>
    /// 自定义时区信息（仅在 Mode 为 Custom 时使用）
    /// </summary>
    internal static TimeZoneInfo CustomTimeZone { get; private set; } = TimeZoneInfo.Utc;


    /// <summary>
    /// 设置为使用本地时间
    /// </summary>
    internal static void UseLocalTime()
    {
        Mode = LogTimestampMode.Local;
    }

    /// <summary>
    /// 设置为使用 UTC 时间
    /// </summary>
    internal static void UseUtcTime()
    {
        Mode = LogTimestampMode.Utc;
        CustomTimeZone = TimeZoneInfo.Utc;
    }

    /// <summary>
    /// 设置为使用自定义时区
    /// </summary>
    /// <param name="timeZone">指定的时区信息</param>
    /// <exception cref="ArgumentNullException">timeZone 为 null</exception>
    internal static void UseCustomTimeZone(TimeZoneInfo timeZone)
    {
        if (timeZone == null)
            throw new ArgumentNullException(nameof(timeZone));
            
        Mode = LogTimestampMode.Custom;
        CustomTimeZone = timeZone;
    }
    
    /// <summary>
    /// 获取当前配置的时间戳
    /// </summary>
    /// <returns>根据配置模式返回的 DateTime</returns>
    internal static DateTimeOffset GetTimestamp()
    {
        return Mode switch
        {
            LogTimestampMode.Local => DateTimeOffset.Now,
            LogTimestampMode.Utc => DateTimeOffset.UtcNow,
            LogTimestampMode.Custom => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CustomTimeZone),
            _ => DateTimeOffset.UtcNow
        };
    }
}