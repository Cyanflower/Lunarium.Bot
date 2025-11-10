using LunariumBot.Net.Logger.Sink;

namespace LunariumBot.Net.Logger;

/// <summary>
/// 一个用于配置和构建 LunariumLogger 实例的构建器。
/// 使用流式接口（fluent interface）来链式调用配置方法。
/// </summary>
public class LoggerBuilder
{
    // 持有所有已配置的日志输出目标
    private List<ConfiguredSink> _sinks = new();
    // 日志记录器的名称
    private string _loggerName = "LoggerName:Undefined";

    /// <summary>
    /// 初始化一个新的 LoggerBuilder 实例。
    /// </summary>
    public LoggerBuilder() { }

    /// <summary>
    /// 设置日志记录器的名称。
    /// </summary>
    /// <param name="loggerName">要设置的名称。</param>
    /// <returns>返回当前构建器实例，以便进行链式调用。</returns>
    public LoggerBuilder LoggerName(string loggerName)
    {
        _loggerName = loggerName;
        return this;
    }

    /// <summary>
    /// 向构建器添加一个日志输出目标（Sink）。
    /// </summary>
    /// <param name="sink">要添加的 ILogSink 实现。</param>
    /// <returns>返回当前构建器实例，以便进行链式调用。</returns>
    public LoggerBuilder AddSink(ILogSink sink, SinkConfiguration? cfg = null)
    {
        if (cfg == null) cfg = new SinkConfiguration();
        _sinks.Add(new ConfiguredSink(sink,cfg));
        return this;
    }

    /// <summary>
    /// 根据当前配置构建并返回一个新的 LunariumLogger 实例。
    /// </summary>
    /// <returns>配置完成的 LunariumLogger 实例。</returns>
    public Logger Build()
    {
        if (GlobalBuildLock.Completed)
        {
            throw new InvalidOperationException(
                "The Logger Instance Has Been Constructed. It should be a GLOBAL Singleton." +
                "Logger Instance Build can only be done once during application startup.");
        }
        // 如果用户没有配置全局设置，应用默认配置
        GlobalConfigurator.ApplyDefaultIfNotConfigured();
        try
        {
            var logger = new Logger(_sinks, _loggerName);
            // 成功 new 实例之后, 完成构建, 设置锁
            GlobalBuildLock.CompleteBuild();
            return logger;
        }
        catch (Exception ex)
        {
            InternalLogger.Error(ex, "LoggerBuilder: Build Failed");
            throw;
        }
    }
}