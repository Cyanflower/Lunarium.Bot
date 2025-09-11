using System.Threading.Channels;

using LunariumBot.Net.Logger.Interfaces;

namespace LunariumBot.Net.Logger.Sink;

/// <summary>
/// 一个将日志写入到指定 ChannelWriter 的 Sink。
/// 这对于在不同模块或线程之间安全地传递日志非常有用，例如从后端服务传递到 UI 层。
/// </summary>
public class ChannelSink : ILogSink
{
    // 存放用于写入日志条目的 ChannelWriter
    private readonly ChannelWriter<LogEntry> _writer;

    /// <summary>
    /// 初始化一个新的 ChannelSink 实例。
    /// </summary>
    /// <param name="writer">用于接收日志条目的 ChannelWriter。</param>
    public ChannelSink(ChannelWriter<LogEntry> writer)
    {
        _writer = writer;
    }

    /// <summary>
    /// 将日志条目异步写入到 Channel 中。
    /// </summary>
    /// <param name="entry">要写入的日志条目。</param>
    public void Emit(LogEntry entry, bool toJson, bool indented)
    {
        // 尝试写入，如果 Channel 已关闭或发生其他问题，它会无声地失败，不会阻塞或抛出异常。
        _writer.TryWrite(entry);
    }

    /// <summary>
    /// 释放资源。对于 ChannelSink 而言，它不拥有 Channel 的生命周期，
    /// 因此 Dispose 方法为空。Channel 的关闭由创建它的那一方负责。
    /// </summary>
    public void Dispose() { }
}

/*
通过 Channel 向主程序的Tui发送日志
channel建立在bot程序集上, bot负责初始化自己的日志模块, 然后自行配置输出到文件, 然后bot自己创建一个channel实例, 
传给LoggerBuilder的一个单独的例如TuiSkin然后-> 日志实例, 这个Skin就直接往这个channel里写消息, 
然后作为运行时动态加载的接口一部分, 主进程和Bot直接对接这个channel
*/