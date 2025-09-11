// 新建文件：src/LunariumBot.Net.Logger/Extensions/MicrosoftLoggingBridge.cs
using Microsoft.Extensions.Logging;
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.Logger.Extensions;

/// <summary>
/// 将 Microsoft.Extensions.Logging 桥接到 LunariumLogger 的适配器
/// </summary>
public class LunariumLoggerProvider : ILoggerProvider
{
    private readonly LunariumBot.Net.Logger.ILogger _lunariumLogger;

    public LunariumLoggerProvider(LunariumBot.Net.Logger.ILogger lunariumLogger)
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
    private readonly LunariumBot.Net.Logger.ILogger _lunariumLogger;

    public LunariumMsLoggerAdapter(LunariumBot.Net.Logger.ILogger lunariumLogger)
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

    private static LunariumBot.Net.Logger.LogLevel ConvertLogLevel(Microsoft.Extensions.Logging.LogLevel msLogLevel)
    {
        return msLogLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Trace => LunariumBot.Net.Logger.LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Debug => LunariumBot.Net.Logger.LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => LunariumBot.Net.Logger.LogLevel.Info,
            Microsoft.Extensions.Logging.LogLevel.Warning => LunariumBot.Net.Logger.LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => LunariumBot.Net.Logger.LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => LunariumBot.Net.Logger.LogLevel.Critical,
            _ => LunariumBot.Net.Logger.LogLevel.Info
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
        LunariumBot.Net.Logger.ILogger lunariumLogger)
    {
        builder.ClearProviders(); // 清除所有默认提供程序
        builder.AddProvider(new LunariumLoggerProvider(lunariumLogger));
        return builder;
    }
}