namespace Lunarium.Bot.Logger.GlobalConfig;

/// <summary>
/// JSON格式的时间戳模式
/// </summary>
internal enum JsonTimestampMode
{
    /// <summary>Unix时间戳(秒)</summary>
    Unix,
    /// <summary>Unix时间戳(毫秒)</summary>
    UnixMs,
    /// <summary>ISO 8601标准格式(默认)</summary>
    ISO8601,
    /// <summary>自定义格式</summary>
    Custom
}

/// <summary>
/// 文本格式的时间戳模式
/// </summary>
internal enum TextTimestampMode
{
    /// <summary>Unix时间戳(秒)</summary>
    Unix,
    /// <summary>Unix时间戳(毫秒)</summary>
    UnixMs,
    /// <summary>ISO 8601标准格式</summary>
    ISO8601,
    /// <summary>自定义格式(默认)</summary>
    Custom
}

/// <summary>
/// 时间戳格式配置类，用于全局配置日志记录器的时间戳输出格式
/// </summary>
internal static class TimestampFormatConfig
{
    /// <summary>JSON时间戳模式</summary>
    internal static JsonTimestampMode JsonMode { get; private set; } = JsonTimestampMode.ISO8601;
    
    /// <summary>文本时间戳模式</summary>
    internal static TextTimestampMode TextMode { get; private set; } = TextTimestampMode.Custom;
    
    /// <summary>JSON自定义格式字符串</summary>
    internal static string JsonCustomFormat { get; private set; } = "O";
    
    /// <summary>文本自定义格式字符串</summary>
    internal static string TextCustomFormat { get; private set; } = "s";

    /// <summary>
    /// 配置JSON时间戳模式
    /// </summary>
    /// <param name="jsonTimestampMode">要设置的JSON时间戳模式</param>
    internal static void ConfigJsonMode(JsonTimestampMode jsonTimestampMode)
    {
        JsonMode = jsonTimestampMode;
    }
    
    /// <summary>
    /// 配置文本时间戳模式
    /// </summary>
    /// <param name="textTimestampMode">要设置的文本时间戳模式</param>
    internal static void ConfigTextMode(TextTimestampMode textTimestampMode)
    {
        TextMode = textTimestampMode;
    }
    
    /// <summary>
    /// 配置JSON自定义格式字符串
    /// </summary>
    /// <param name="format">自定义格式字符串</param>
    internal static void ConfigJsonCustomFormat(string format)
    {
        JsonCustomFormat = format;
    }
    
    /// <summary>
    /// 配置文本自定义格式字符串
    /// </summary>
    /// <param name="format">自定义格式字符串</param>
    internal static void ConfigTextCustomFormat(string format)
    {
        TextCustomFormat = format;
    }
}