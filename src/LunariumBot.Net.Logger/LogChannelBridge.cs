using System.Threading.Channels;

namespace LunariumBot.Net.Logger;

/// <summary>
/// 一个用于在 Logger 和外部消费者之间传递日志的桥接器。
/// 它封装了一个无界 Channel，并提供了对 Reader 和 Writer 的访问，
/// 确保了生产者和消费者可以安全地解耦。
/// </summary>
public sealed class LogChannelBridge
{
    private readonly Channel<string> _channel;
    
    /// <summary>
    /// 获取用于读取日志的 ChannelReader
    /// </summary>
    public ChannelReader<string> Reader => _channel.Reader;
    
    /// <summary>
    /// 获取用于写入日志的 ChannelWriter (仅供内部使用)
    /// </summary>
    internal ChannelWriter<string> Writer => _channel.Writer;

    /// <summary>
    /// 创建一个新的 Channel 桥接器
    /// </summary>
    /// <param name="capacity">
    /// Channel 的容量限制。如果为 null，则创建无界 Channel。
    /// 如果指定容量，当 Channel 满时，新日志会被丢弃。
    /// </param>
    public LogChannelBridge(int? capacity = null)
    {
        _channel = capacity.HasValue
            ? Channel.CreateBounded<string>(new BoundedChannelOptions(capacity.Value)
            {
                FullMode = BoundedChannelFullMode.DropWrite  // 满时丢弃新消息
            })
            : Channel.CreateUnbounded<string>();
    }
}