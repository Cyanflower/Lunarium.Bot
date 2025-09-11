using System.Threading.Channels;

namespace LunariumBot.Net.Logger;

/// <summary>
/// 一个用于在 Logger 和外部消费者之间传递日志的桥接器。
/// 它封装了一个无界 Channel，并提供了对 Reader 和 Writer 的访问，
/// 确保了生产者和消费者可以安全地解耦。
/// </summary>
public sealed class LogChannelBridge
{
    private readonly Channel<LogEntry> _channel;

    /// <summary>
    /// 获取用于写入日志的 ChannelWriter。
    /// Logger 通过这个 Writer 将日志条目发送到 Channel。
    /// </summary>
    public ChannelWriter<LogEntry> Writer => _channel.Writer;

    /// <summary>
    /// 获取用于读取日志的 ChannelReader。
    /// 外部消费者（如 UI）通过这个 Reader 从 Channel 中异步读取日志条目。
    /// </summary>
    public ChannelReader<LogEntry> Reader => _channel.Reader;

    /// <summary>
    /// 私有构造函数，防止直接实例化。
    /// 使用 Create() 静态方法来创建实例。
    /// </summary>
    private LogChannelBridge()
    {
        _channel = Channel.CreateUnbounded<LogEntry>();
    }

    /// <summary>
    /// 创建一个新的日志通道桥接器实例。
    /// </summary>
    /// <returns>一个新的 LogChannelBridge 实例。</returns>
    public static LogChannelBridge Create() => new();
}