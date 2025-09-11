using LunariumBot.Net.Logger.Interfaces;

namespace LunariumBot.Net.Logger;

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