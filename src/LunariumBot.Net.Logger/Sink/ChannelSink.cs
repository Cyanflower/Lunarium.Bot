using System.Threading.Channels;

using LunariumBot.Net.Logger.Writer;

namespace LunariumBot.Net.Logger.Sink;

/// <summary>
/// 一个将日志写入到指定 ChannelWriter 的 Sink。
/// 这对于在不同模块或线程之间安全地传递日志非常有用，例如从后端服务传递到 UI 层。
/// </summary>
public class ChannelSink : ILogSink
{
    // 存放用于写入日志条目的 ChannelWriter
    private readonly ChannelWriter<string> _writer;
    private readonly bool _isColor;

    /// <summary>
    /// 初始化一个新的 ChannelSink 实例。
    /// </summary>
    /// <param name="writer">用于接收日志条目的 ChannelWriter。</param>
    public ChannelSink(ChannelWriter<string> writer, bool isColor)
    {
        _writer = writer;
        _isColor = isColor;
    }

    /// <summary>
    /// 将日志条目异步写入到 Channel 中。
    /// </summary>
    /// <param name="entry">要写入的日志条目。</param>
    public void Emit(LogEntry entry, bool toJson)
    {
        LogWriter logWriter;

        // JSON 优先级最高
        // 如果用户强行开启了 JSON，说明下游消费者期望的是 JSON 格式
        // 此时无论是否开启了颜色，都必须输出纯净的 JSON，否则下游解析会炸
        if (toJson)
        {
            logWriter = WriterPool.Get<LogJsonWriter>();
        }
        // 其次是带颜色的文本
        // 只有在不是 JSON 且明确要求颜色时，才使用 ColorWriter
        else if (_isColor)
        {
            logWriter = WriterPool.Get<LogColorTextWriter>();
        }
        // 最后是普通纯文本
        // 兜底方案
        else
        {
            logWriter = WriterPool.Get<LogTextWriter>();
        }

        try
        {
            logWriter.Render(entry);
            _writer.TryWrite(logWriter.ToString());
        }
        finally
        {
            logWriter.Return();
        }
    }

    /// <summary>
    /// 释放资源。对于 ChannelSink 而言，它不拥有 Channel 的生命周期，
    /// 因此 Dispose 方法为空。Channel 的关闭由创建它的那一方负责。
    /// </summary>
    public void Dispose() { }
}