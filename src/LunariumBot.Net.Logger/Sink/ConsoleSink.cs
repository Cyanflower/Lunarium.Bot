using LunariumBot.Net.Logger.Writer;

namespace LunariumBot.Net.Logger.Sink;

/// <summary>
/// 一个将日志条目输出到标准控制台的 Sink。
/// 它会使用 ANSI 颜色代码来格式化不同级别的日志，使其更易于阅读。
/// 它支持纯文本和 JSON 两种输出模式，并对结构化数据进行智能染色。
/// </summary>
public class ConsoleSink : ILogSink
{
    // 线程安全锁，以防止多条日志消息的输出在控制台中交错
    private readonly object _lock = new();

    /// <summary>
    /// 格式化日志条目并将其写入控制台。
    /// </summary>
    /// <param name="entry">要输出的日志条目。</param>
    /// <param name="toJson">是否以 JSON 格式输出。</param>
    public void Emit(LogEntry entry, bool toJson)
    {
        // 根据配置选择格式化器
        LogWriter logWriter;
        if (toJson)
        {
            logWriter = WriterPool.Get<LogJsonWriter>();
        }
        else if (Console.IsOutputRedirected)
        {
            logWriter = WriterPool.Get<LogTextWriter>();
        }
        else
        {
            logWriter = WriterPool.Get<LogColorTextWriter>();
        }

        try
        {
            // 渲染日志
            logWriter.Render(entry);

            // 选择输出流
            TextWriter output = entry.LogLevel >= LogLevel.Error 
                ? Console.Error 
                : Console.Out;

            // 4. 输出
            lock (_lock)
            {
                logWriter.FlushTo(output);
            }
        }
        finally
        {
            // 5. 归还对象池
            logWriter.Return();
        }
    }
    
    /// <summary>
    /// 释放资源。控制台输出不需要特殊的资源清理操作。
    /// </summary>
    public void Dispose() { } // 控制台不需要释放资源
}
