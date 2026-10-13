using System.Threading.Channels;
using Lunarium.Bot.Logger.Wrapper;
using Lunarium.Bot.Logger.Sink;

namespace Lunarium.Bot.Logger;

/// <summary>
/// 为 ILogger 接口提供扩展方法，以方便地创建带有上下文的日志记录实例的包装器。
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// 创建一个新的日志记录器实例的包装器，该包装器会自动将指定的上下文信息附加到所有日志条目中。
    /// 这对于区分来自不同模块或类的日志非常有用。
    /// </summary>
    /// <param name="logger">要扩展的 ILogger 实例。</param>
    /// <param name="context">要附加到日志条目的上下文字符串。</param>
    /// <returns>一个包含了新上下文的 ILogger 实例，可用于后续日志记录。</returns>
    /// <exception cref="ArgumentNullException">当 logger 为 null 时抛出。</exception>
    public static ILogger ForContext(this ILogger logger, string context)
    {
        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        // 返回一个包装器，该包装器持有原始 logger 和新的上下文
        return new LoggerWrapper(logger, context);
    }

    /// <summary>
    /// 创建一个特定于类型的上下文日志记录实例的包装器。
    /// 此方法是 ForContext(string) 的一个便捷重载，它使用泛型类型 T 的全名作为上下文。
    /// </summary>
    /// <typeparam name="T">将用于创建上下文的类型。通常是当前所在的类。</typeparam>
    /// <param name="logger">要扩展的 ILogger 实例。</param>
    /// <returns>一个包含了类型上下文的 ILogger 实例。</returns>
    public static ILogger ForContext<T>(this ILogger logger)
    {
        return logger.ForContext(typeof(T).FullName ?? typeof(T).Name);
    }
}


/// <summary>
/// 为 LoggerBuilder 提供方便的扩展方法，用于快速添加各种日志输出目标 (Sink)。
/// </summary>
public static class LoggerBuilderExtensions
{
    /// <summary>
    /// 为日志构建器添加一个控制台输出目标 (Sink)。
    /// 日志会直接输出到应用程序的控制台。
    /// </summary>
    /// <param name="builder">要配置的 LoggerBuilder 实例。</param>
    /// <returns>返回配置后的 LoggerBuilder 实例，以便进行链式调用。</returns>
    public static LoggerBuilder AddConsoleSink(this LoggerBuilder builder, SinkConfiguration? sinkConfiguration = null)
    {
        return builder.AddSink(new ConsoleSink(), sinkConfiguration);
    }

    /// <summary>
    /// 为日志构建器添加一个按时间轮转的文件输出目标 (Sink)。
    /// 每天会生成一个新的日志文件。
    /// </summary>
    /// <param name="builder">要配置的 LoggerBuilder 实例。</param>
    /// <param name="logFilePath">日志文件的基础路径。文件名将根据日期自动生成，例如 'path/to/log.txt' 会变成 'path/to/log-2023-10-27.txt'。</param>
    /// <param name="maxFile">保留的最大日志文件数量。0 或负数表示不限制。</param>
    /// <returns>返回配置后的 LoggerBuilder 实例，以便进行链式调用。</returns>
    public static LoggerBuilder AddTimedRotatingFileSink(this LoggerBuilder builder, string logFilePath, int maxFile = 0, SinkConfiguration? sinkConfiguration = null)
    {
        return builder.AddSink(new TimedRotatingFileSink(logFilePath, maxFile), sinkConfiguration);
    }

    /// <summary>
    /// 为日志构建器添加一个按大小轮转的文件输出目标 (Sink)。
    /// 当日志文件达到指定大小时，会自动创建新的日志文件。
    /// </summary>
    /// <param name="builder">要配置的 LoggerBuilder 实例。</param>
    /// <param name="logFilePath">日志文件的基础路径。当文件达到大小限制时，会自动创建带时间戳的新文件。</param>
    /// <param name="maxFileSizeMB">单个日志文件的最大大小 (MB)。</param>
    /// <param name="maxFile">保留的最大日志文件数量。</param>
    /// <returns>返回配置后的 LoggerBuilder 实例，以便进行链式调用。</returns>
    public static LoggerBuilder AddSizedRotatingFileSink(this LoggerBuilder builder, string logFilePath, double maxFileSizeMB = 10, int maxFile = 10, SinkConfiguration? sinkConfiguration = null)
    {
        return builder.AddSink(new RotatingFileSink(logFilePath, maxFileSizeMB, maxFile), sinkConfiguration);
    }
    
    /// <summary>
    /// 为日志构建器添加一个 Channel 输出目标，用于将日志条目发送到 .NET Channel。
    /// 这对于将日志从一个模块传递到另一个模块（例如，传递给 UI 层）非常有用。
    /// </summary>
    /// <param name="builder">要配置的 LoggerBuilder 实例。</param>
    /// <param name="bridge">包含 ChannelWriter 的 LogChannelBridge 实例。</param>
    /// <returns>返回配置后的 LoggerBuilder 实例，以便进行链式调用。</returns>
    public static LoggerBuilder AddChannelSink(this LoggerBuilder builder, out ChannelReader<string> reader, int? channelCapacity = null, bool isColor = false, SinkConfiguration? sinkConfiguration = null)
    {
        var bridge = new LogChannelBridge(channelCapacity);
        reader = bridge.Reader;
        return builder.AddSink(new ChannelSink(bridge.Writer, isColor), sinkConfiguration);
    }
}