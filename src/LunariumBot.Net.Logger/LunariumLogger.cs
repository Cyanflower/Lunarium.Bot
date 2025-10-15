using System.Threading.Channels;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using LunariumBot.Net.Logger.Interfaces;
using LunariumBot.Net.Logger.Extensions;

namespace LunariumBot.Net.Logger;

/// <summary>
/// LunariumLogger 的核心实现。
/// 它通过一个后台任务和 .NET Channel 异步处理日志消息，
/// 将日志分发到所有已配置的 Sink，从而避免阻塞调用方线程。
/// </summary>
public class LunariumLogger : ILogger, IAsyncDisposable
{
    // === 内部变量 ===
    // 传递日志消息的无界Channel，用于解耦日志写入和处理
    private readonly Channel<LogEntry> _queue = Channel.CreateUnbounded<LogEntry>();
    // 用于在关闭时通知后台任务停止
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    // 负责从 Channel 读取并处理日志的后台任务
    private readonly Task _processTask;

    // === 日志配置 ===
    // 日志记录器名称
    private readonly string _loggerName;
    // 所有输出目标的列表
    private readonly List<(ILogSink, SinkConfiguration)> _sinks;
    private static readonly Regex _propertyRegex = new(@"\{([a-zA-Z_.()][a-zA-Z0-9_.()]*)(?::([^}]+))?\}", RegexOptions.Compiled);

    /// <summary>
    /// 初始化一个新的 LunariumLogger 实例。
    /// </summary>
    /// <param name="sinks">日志要发送到的 Sink 列表。</param>
    /// <param name="loggerName">此日志记录器的名称。</param>
    /// <param name="logMinLevel">允许记录的最低级别。</param>
    /// <param name="callerMinLevel">记录调用者信息的最低级别。</param>
    public LunariumLogger(List<(ILogSink, SinkConfiguration)> sinks, string loggerName)
    {
        _sinks = sinks;
        _loggerName = loggerName;
        // 启动后台处理任务
        _processTask = Task.Run(ProcessQueueAsync);
    }

    /// <summary>
    /// 在后台运行，持续从 Channel 中读取日志条目并分发给所有 Sink。
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var logEntry in _queue.Reader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                // 解包元组
                foreach (var (sink, cfg) in _sinks)
                {
                    try
                    {
                        // [Debug]
                        // Console.WriteLine($"{sink}收到新日志:");
                        // Console.WriteLine($"{logEntry.ToJson()}");

                         // 只要级别在范围之外，就跳过
                        if (logEntry.Level < cfg.LogMinLevel || logEntry.Level > cfg.LogMaxLevel)
                        {
                            continue;
                        }

                        var comparisonType = cfg.IgnoreSourceCase
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal;

                        // --- 规则 1: 检查 Includes 列表 ---
                        bool includeMatch = false;
                        // 如果 Includes 为空或未设置，我们默认它匹配所有
                        if (cfg.ContextFilterIncludes == null || !cfg.ContextFilterIncludes.Any()) 
                        {
                            includeMatch = true;
                        }
                        else
                        {
                            // 只要匹配列表中的任意一个即可
                            includeMatch = cfg.ContextFilterIncludes.Any(prefix => logEntry.Context.StartsWith(prefix, comparisonType));
                        }
                        
                        // 如果连包含规则都没匹配上，直接跳过这个 Sink
                        if (!includeMatch)
                        {
                            continue;
                        }

                        // [Debug]
                        // Console.WriteLine("includeMatch 通过");

                        // --- 规则 2: 检查 Excludes 列表 ---
                        bool excludeMatch = false;
                        if (cfg.ContextFilterExcludes != null && cfg.ContextFilterExcludes.Any())
                        {
                            // 只要匹配列表中的任意一个，就要被排除
                            excludeMatch = cfg.ContextFilterExcludes.Any(prefix => logEntry.Context.StartsWith(prefix, comparisonType));
                        }
                        
                        // 如果被排除规则匹配了，也跳过这个 Sink
                        if (excludeMatch)
                        {
                            continue;
                        }

                        // [Debug]
                        // Console.WriteLine("excludeMatch 通过");

                        sink.Emit(logEntry, cfg.ToJson, cfg.Indented);
                    }
                    catch (Exception ex)
                    {
                        // 错误处理
                        Console.BackgroundColor = ConsoleColor.Red;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"[LunariumLogger.InternalError] Sink Failed: {ex}");
                        Console.ResetColor();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 这是正常关闭时预期的异常，无需处理
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LunariumLogger.InternalError] {message}");
        }
    }

    /// <summary>
    /// 创建一个日志条目并将其放入队列中等待异步处理。
    /// </summary>
    public void Log(LogLevel level, string messageTemplate = "", string context = "", Exception? ex = null, params object?[] propertyValues)
    {
        try
        {
            // [Debug]
            // Console.WriteLine($"{_loggerName}: Log 被调用");
            // 如果日志记录器正在关闭，则不再接受新的日志
            if (_cancellationTokenSource.IsCancellationRequested) return;
    
            var properties = CaptureProperties(messageTemplate, propertyValues);
    
            var entry = new LogEntry(
                LoggerName: _loggerName,
                Timestamp: DateTime.Now,
                Level: level,
                MessageTemplate: messageTemplate,
                Properties: properties,
                Context: context,
                Exception: ex
            );
    
            // 尝试将日志条目写入 Channel
            _queue.Writer.TryWrite(entry);
        }
        catch (Exception e)
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"[LunariumLogger.InternalError] Log Failed: {e}");
            Console.ResetColor();
        }
    }
    
    /// <summary>
    /// 从消息模板和属性值数组中捕获属性字典。
    /// 这就是你刚才指出的那个方法！
    /// </summary>
    /// <param name="messageTemplate">消息模板，如 "User {Name}"</param>
    /// <param name="propertyValues">值的数组，如 ["Alice"]</param>
    /// <returns>一个只读的属性字典</returns>
    public static IReadOnlyDictionary<string, object?> CaptureProperties(string messageTemplate, object?[] propertyValues)
    {
        var properties = new Dictionary<string, object?>();
        
        // 在模板中找到所有匹配的占位符
        var matches = _propertyRegex.Matches(messageTemplate);

        // 遍历所有找到的占位符
        for (var i = 0; i < matches.Count; i++)
        {
            // 确保有对应的值，防止数组越界
            // (比如模板里有2个占位符，但只传了1个值)
            if (i < propertyValues.Length)
            {
                // 从匹配结果中提取出属性名 (例如，从 "{Name}" 中提取 "Name")
                var propertyName = matches[i].Groups[1].Value;

                // 防止模板中有重复的占位符名导致字典出错
                // (例如 "User {Name} is also known as {Name}")
                // 我们只捕获第一个。
                if (!properties.ContainsKey(propertyName))
                {
                    properties.Add(propertyName, propertyValues[i]);
                }
            }
            else
            {
                // 如果值的数量不够，我们就停止捕获
                break; 
            }
        }
        
        return properties;
    }
    
    /// <summary>
    /// 关闭日志记录器。
    /// 它会停止接受新的日志，等待所有已在队列中的日志处理完毕，然后释放所有 Sink 的资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.Complete();
        _cancellationTokenSource.Cancel();
        
        try
        {
            await _processTask;
        }
        catch (Exception ex)
        {
            // 记录任务异常，但继续清理
            SafeWriteError($"ProcessTask ended with error: {ex.Message}");
        }
        
        // 即使任务失败，也要释放Sinks
        foreach (var (sink, _) in _sinks)
        {
            try
            {
                sink.Dispose();
            }
            catch (Exception ex)
            {
                SafeWriteError($"Sink disposal failed: {ex.Message}");
            }
        }
        
        _cancellationTokenSource.Dispose();
    }
}