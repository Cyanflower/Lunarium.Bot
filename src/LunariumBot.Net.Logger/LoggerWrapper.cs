using LunariumBot.Net.Logger.Interfaces;

namespace LunariumBot.Net.Logger;

/// <summary>
/// 一个日志包装器（装饰器），用于为一个已存在的 ILogger 实例添加固定的上下文信息。
/// 每次通过此包装器记录日志时，它都会自动附加这个上下文。
/// </summary>
internal class LoggerWrapper : ILogger
{
    // 引用被包装的日志实例或下一层包装器
    private readonly ILogger _logger;
    // 此包装器要附加的上下文
    private readonly string _context;

    /// <summary>
    /// 初始化一个新的 LoggerWrapper 实例。
    /// </summary>
    /// <param name="logger">要包装的 ILogger 实例。</param>
    /// <param name="context">要与此包装器关联的上下文信息。</param>
    public LoggerWrapper(ILogger logger, string context)
    {
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// 记录一条日志。它会将自己的上下文与传入的上下文组合，然后传递给被包装的日志记录器。
    /// </summary>
    public void Log(LogLevel level, string message = "", string context = "", Exception? ex = null, params object?[] propertyValues)
    {
        // 处理多重包装时的上下文路径，格式为 "外层上下文/内层上下文"
        var finalContext = string.IsNullOrEmpty(context) ? _context : $"{_context}.{context}";
        // 调用被包装的日志实例的 Log 方法
        _logger.Log(level, message, finalContext, ex, propertyValues);
    }
}