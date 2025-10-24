using LunariumBot.Net.Logger.Interfaces;

namespace LunariumBot.Net.Logger;

/// <summary>
/// 一个用于配置和构建 LunariumLogger 实例的构建器。
/// 使用流式接口（fluent interface）来链式调用配置方法。
/// </summary>
public class LoggerBuilder
{
    // 持有所有已配置的日志输出目标
    internal List<(ILogSink, SinkConfiguration)> _sinks = new();
    // 日志记录器的名称
    internal string _loggerName = "LoggerName:Undefined";

    /// <summary>
    /// 初始化一个新的 LoggerBuilder 实例。
    /// </summary>
    /// <param name="config">一个可选的配置对象，用于预先填充构建器的设置。</param>
    public LoggerBuilder(LumLogConfig? config = null)
    {
        if (config != null)
        {
            _sinks = config.Sinks;
            _loggerName = config.LoggerName;
        }
    }

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
        _sinks.Add((sink, cfg));
        return this;
    }

    /// <summary>
    /// 根据当前配置构建并返回一个新的 LunariumLogger 实例。
    /// </summary>
    /// <returns>配置完成的 LunariumLogger 实例。</returns>
    public Logger Build()
    {
        return new Logger(_sinks, _loggerName);
    }

    /// <summary>
    /// (静态工厂方法) 根据一个 LumLogConfig 配置对象直接创建一个 LunariumLogger 实例。
    /// </summary>
    /// <param name="config">包含所有配置信息的对象。</param>
    /// <returns>一个配置好的 LunariumLogger 实例。</returns>
    public static Logger LoggerBuilderByConfig(LumLogConfig config)
    {
        LoggerBuilder loggerBuilder = new(config);

        return loggerBuilder.Build();
    }
}