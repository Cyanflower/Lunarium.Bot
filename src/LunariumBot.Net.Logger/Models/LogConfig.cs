using LunariumBot.Net.Logger.Sink;
using LunariumBot.Net.Logger.Filter;

namespace LunariumBot.Net.Logger.Models;

/// <summary>
/// 一个用于存储和传递日志记录器配置的数据容器类。
/// </summary>
internal record LumLogConfig
{
    /// <summary>
    /// 日志要输出到的 Sink 列表。
    /// </summary>
    internal List<ConfiguredSink> Sinks { get; set; } = new();
    /// <summary>
    /// 日志记录器的名称。
    /// </summary>
    internal string LoggerName { get; set; } = "";
}

/// <summary>
/// 将一个 ILogSink 与其特定的 SinkConfiguration 绑定在一起。
/// 这个结构体封装了一个配置好的、准备就绪的日志输出目标。
/// </summary>
/// <param name="Sink">日志输出目标实例。</param>
/// <param name="Configuration">与此 Sink 关联的配置。</param>
internal readonly record struct ConfiguredSink
{
    internal ILogSink Sink { get; }
    internal SinkConfiguration Configuration { get; }
    internal LoggerFilter LoggerFilter { get; }

    internal ConfiguredSink(ILogSink sink, SinkConfiguration configuration)
    {
        Sink = sink;
        Configuration = configuration;
        LoggerFilter = new LoggerFilter();
    }

    internal ConfiguredSink(ILogSink sink)
    {
        Sink = sink;
        Configuration = new SinkConfiguration();
        LoggerFilter = new LoggerFilter();
    }

    internal void Deconstruct(out ILogSink sink, out SinkConfiguration configuration)
    {
        sink = Sink;
        configuration = Configuration;
    }
    
    internal void Deconstruct(out ILogSink sink, out SinkConfiguration configuration, out LoggerFilter loggerFilter)
    {
        sink = Sink;
        configuration = Configuration;
        loggerFilter = LoggerFilter;
    }
}
