using LunariumBot.Net.Logger.Parser;

namespace LunariumBot.Net.Logger.Models;

/// <summary>
/// 表示单个日志事件的不可变数据结构。
/// 它包含了日志的所有相关信息。
/// </summary>
/// <param name="LoggerName">生成此日志的记录器名称。</param>
/// <param name="Timestamp">日志事件发生的时间戳。</param>
/// <param name="Level">日志的严重级别。</param>
/// <param name="MessageTemplate">日志消息模板。</param>
/// <param name="Properties">与日志事件关联的结构化属性。</param>
/// <param name="Context">日志的上下文信息（如来源模块）。</param>
/// <param name="Caller">调用日志方法的成员名称。</param>
/// <param name="Exception">与此日志关联的异常信息（如果有）。</param>

public class LogEntry
{
    // 只读属性，在构造函数中初始化后不能更改
    public string LoggerName { get; }
    public DateTimeOffset Timestamp { get; }
    public LogLevel LogLevel { get; }
    public string Message { get; }
    public object?[] Properties { get; }
    public string Context { get; }
    public Exception? Exception { get; }

    // 可读写属性，用于延迟解析和渲染
    public MessageTemplate MessageTemplate { get; private set; }

    // 完整的构造函数，用于初始化所有字段
    public LogEntry(
        string loggerName,
        DateTimeOffset timestamp,
        LogLevel logLevel,
        string message,
        object?[] properties,
        string context,
        MessageTemplate messageTemplate,
        Exception? exception = null)
    {
        LoggerName = loggerName;
        Timestamp = timestamp;
        LogLevel = logLevel;
        Message = message;
        Properties = properties;
        Context = context;
        MessageTemplate = messageTemplate;
        Exception = exception;
    }

    public void ParseMessage()
    {
        if (object.ReferenceEquals(MessageTemplate, LogParser.EmptyMessageTemplate))
        {
            MessageTemplate = LogParser.ParseMessage(Message);
        }
    }
}