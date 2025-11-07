using LunariumBot.Net.Logger.Models;

namespace LunariumBot.Net.Logger.Sink;

/// <summary>
/// 定义了日志输出目标（Sink）的通用接口。
/// 所有日志输出目标（如控制台、文件等）都必须实现此接口。
/// 它定义了如何发出日志条目以及如何释放相关资源。
/// </summary>
public interface ILogSink : IDisposable
{
    /// <summary>
    /// 将单个日志条目发送或写入到最终的目标。
    /// </summary>
    /// <param name="entry">要发出的日志条目。</param>
    void Emit(LogEntry entry, bool toJson);
}
