using Microsoft.Extensions.Logging;
using Lunarium.Bot.Logger;
using Lunarium.Bot.Logger.Models;

namespace Lunarium.Bot.Logger.Extensions;

/// <summary>
/// 将 Microsoft.Extensions.Logging 桥接到 LunariumLogger 的适配器
/// </summary>
public class LunariumLoggerProvider : ILoggerProvider
{
    private readonly Lunarium.Bot.Logger.ILogger _lunariumLogger;

    public LunariumLoggerProvider(Lunarium.Bot.Logger.ILogger lunariumLogger)
    {
        _lunariumLogger = lunariumLogger;
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new LunariumMsLoggerAdapter(_lunariumLogger.ForContext(categoryName));
    }

    public void Dispose()
    {
        // LunariumLogger 的生命周期由外部管理
    }
}

/// <summary>
/// 将 Microsoft.Extensions.Logging.ILogger 适配到 LunariumLogger 的适配器
/// </summary>
internal class LunariumMsLoggerAdapter : Microsoft.Extensions.Logging.ILogger
{
    private readonly Lunarium.Bot.Logger.ILogger _lunariumLogger;

    public LunariumMsLoggerAdapter(Lunarium.Bot.Logger.ILogger lunariumLogger)
    {
        _lunariumLogger = lunariumLogger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        // 这里可以根据需要实现更复杂的逻辑
        return true;
    }

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var level = ConvertLogLevel(logLevel);
        
        // 使用 eventId.Name 或 eventId.Id 作为额外的上下文
        var context = !string.IsNullOrEmpty(eventId.Name) ? eventId.Name : eventId.Id.ToString();
        
        _lunariumLogger.Log(level, message, context, exception);
    }

    private static Lunarium.Bot.Logger.LogLevel ConvertLogLevel(Microsoft.Extensions.Logging.LogLevel msLogLevel)
    {
        return msLogLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => Lunarium.Bot.Logger.LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Debug => Lunarium.Bot.Logger.LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => Lunarium.Bot.Logger.LogLevel.Info,
            Microsoft.Extensions.Logging.LogLevel.Warning => Lunarium.Bot.Logger.LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => Lunarium.Bot.Logger.LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => Lunarium.Bot.Logger.LogLevel.Critical,
            _ => Lunarium.Bot.Logger.LogLevel.Info
        };
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

/// <summary>
/// 扩展方法，方便注册自定义日志提供程序
/// </summary>
public static class LunariumLoggerExtensions
{
    public static ILoggingBuilder AddLunariumLogger(
        this ILoggingBuilder builder,
        Lunarium.Bot.Logger.ILogger lunariumLogger)
    {
        builder.ClearProviders(); // 清除所有默认提供程序
        builder.AddProvider(new LunariumLoggerProvider(lunariumLogger));
        return builder;
    }
}

public static class LunariumLoggerConversionExtensions
{
    /// <summary>
    /// 将 LunariumLogger 转换为 Microsoft.Extensions.Logging.ILogger
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger ToMicrosoftLogger(
        this Lunarium.Bot.Logger.ILogger logger, 
        string categoryName)
    {
        return new LunariumMsLoggerAdapter(logger.ForContext(categoryName));
    }
}