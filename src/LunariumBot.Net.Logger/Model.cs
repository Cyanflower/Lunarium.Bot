using LunariumBot.Net.Logger.Interfaces;

namespace LunariumBot.Net.Logger;

/// <summary>
/// 定义日志的严重性级别。
/// 级别从 Debug (最低) 到 Critical (最高)。
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// 表示单个日志事件的不可变数据结构。
/// 它包含了日志的所有相关信息。
/// </summary>
/// <param name="LoggerName">生成此日志的记录器名称。</param>
/// <param name="Timestamp">日志事件发生的时间戳。</param>
/// <param name="Level">日志的严重级别。</param>
/// <param name="MessageTemplate">日志消息的模板，例如 "User {Username} logged in"。</param>
/// <param name="Properties">与日志事件关联的结构化属性。</param>
/// <param name="Context">日志的上下文信息（如来源模块）。</param>
/// <param name="Caller">调用日志方法的成员名称。</param>
/// <param name="Exception">与此日志关联的异常信息（如果有）。</param>
public readonly record struct LogEntry(
    string LoggerName,
    DateTime Timestamp,
    LogLevel Level,
    string MessageTemplate,
    IReadOnlyDictionary<string, object?> Properties,
    string Context,
    Exception? Exception = null
);

/// <summary>
/// 一个用于存储和传递日志记录器配置的数据容器类。
/// </summary>
public class LumLogConfig
{
    /// <summary>
    /// 日志要输出到的 Sink 列表。
    /// </summary>
    public List<(ILogSink, SinkConfiguration)> Sinks { get; set; }  = new();
    /// <summary>
    /// 日志记录器的名称。
    /// </summary>
    public string LoggerName { get; set; }  = "";
}

/// <summary>
/// 封装单个日志输出目标 (Sink) 的相关配置
/// </summary>
public class SinkConfiguration
{
    // 使用 Json 格式输出日志
    public bool ToJson { get; set; } = false;

    // 是否使用缩进格式化 Json 输出
    public bool Indented { get; set; } = false;

    /// <summary>
    /// 上下文前缀过滤器:
    /// 包含规则列表。
    /// 只有当日志上下文以列表中的任意一个前缀开头时，才算匹配。
    /// 如果此列表为 null 或为空，则视为匹配所有日志。
    /// </summary>
    public List<string>? ContextFilterIncludes { get; init; }

    /// <summary>
    /// 排除规则列表。
    /// 如果日志上下文以列表中的任意一个前缀开头，则会被明确排除。
    /// </summary>
    public List<string>? ContextFilterExcludes { get; init; }

    /// <summary>
    /// 匹配上下文时是否忽略大小写。
    /// </summary>
    public bool IgnoreSourceCase { get; init; } = false;

    /// <summary>
    /// 允许记录的最低日志级别。
    /// </summary>
    public LogLevel LogMinLevel { get; set; } = LogLevel.Info;
    
    /// <summary>
    /// 允许记录的最高日志级别。
    /// </summary>
    public LogLevel LogMaxLevel { get; set; } = LogLevel.Critical;
}