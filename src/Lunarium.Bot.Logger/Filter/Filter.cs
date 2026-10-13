using System.Collections.Concurrent;

using Lunarium.Bot.Logger.Models;

namespace Lunarium.Bot.Logger.Filter;

/// <summary>
/// 日志过滤器，负责判断日志条目是否应该被输出。
/// 使用缓存机制提升性能，避免重复的前缀匹配计算。
/// 
/// <para><b>注意事项：</b></para>
/// <list type="bullet">
/// <item>Context 应该是相对静态的字符串（如类名、模块名），而非包含动态值的字符串</item>
/// <item>正确用法：<c>logger.ForContext("Order.Processor").Info("Processing {Id}", orderId)</c></item>
/// <item>错误用法：<c>logger.Info("Processing", $"Order.{orderId}")</c> ← 会导致缓存迅速膨胀并性能降级，且非结构化日志</item>
/// </list>
/// </summary>
internal class LoggerFilter
{
    // 缓存条数上限
    private const int CacheMaxCountLimit = 2048;

    // 缓存已经检查过的上下文及其结果
    // Key: Context, Value: 是否应该输出
    private readonly ConcurrentDictionary<string, bool> _contextCache = new();

    /// <summary>
    /// 判断日志条目是否应该被输出
    /// </summary>
    internal bool ShouldEmit(LogEntry entry, SinkConfiguration cfg)
    {
        // 1. 首先检查日志级别
        if (entry.LogLevel < cfg.LogMinLevel || entry.LogLevel > cfg.LogMaxLevel)
            return false;

        // 2. 检查缓存
        if (_contextCache.TryGetValue(entry.Context, out var cachedResult))
            return cachedResult;

        // 3. 执行实际的过滤逻辑
        var shouldEmit = CheckContextFilters(entry.Context, cfg);

        // 4. 更新缓存
        if (_contextCache.Count >= CacheMaxCountLimit)
        {
            _contextCache.Clear();
        }
        
        _contextCache[entry.Context] = shouldEmit;

        return shouldEmit;
    }

    /// <summary>
    /// 执行上下文过滤检查
    /// </summary>
    private bool CheckContextFilters(string context, SinkConfiguration cfg)
    {
        var hasIncludes = cfg.ContextFilterIncludes is { Count: > 0 };
        var hasExcludes = cfg.ContextFilterExcludes is { Count: > 0 };

        // 如果没有配置任何过滤规则,默认全部通过
        if (!hasIncludes && !hasExcludes)
            return true;

        // Include 检查:如果配置了 Include 规则,必须匹配其中之一
        if (hasIncludes)
        {
            var matchedInclude = cfg.ContextFilterIncludes!.Any(prefix =>
                context.StartsWith(prefix, cfg.ComparisonType));

            if (!matchedInclude)
                return false;
        }

        // Exclude 检查:如果匹配了 Exclude 规则,则排除
        if (hasExcludes)
        {
            var matchedExclude = cfg.ContextFilterExcludes!.Any(prefix =>
                context.StartsWith(prefix, cfg.ComparisonType));

            if (matchedExclude)
                return false;
        }

        return true;
    }
}