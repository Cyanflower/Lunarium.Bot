using Xunit;
using LunariumBot.Net.Logger;
using LunariumBot.Net.Logger.Filter;
using LunariumBot.Net.Logger.Models;
using LunariumBot.Net.Logger.Parser;
using System.Reflection;

namespace LunariumBot.Net.LoggerParser.Tests;

public class LoggerFilterCacheTests
{
    // 通过反射访问私有缓存字段
    private static readonly FieldInfo? _contextCacheField = typeof(LoggerFilter)
        .GetField("_contextCache", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo? _cacheMaxCountLimitField = typeof(LoggerFilter)
        .GetField("CacheMaxCountLimit", BindingFlags.NonPublic | BindingFlags.Static);

    private static int CacheMaxCountLimit => (int)(_cacheMaxCountLimitField?.GetValue(null) ?? 2048);

    private static int GetCacheCount(LoggerFilter filter)
    {
        var cache = _contextCacheField?.GetValue(filter);
        if (cache == null) return 0;

        var countProperty = cache.GetType().GetProperty("Count");
        return (int)(countProperty?.GetValue(cache) ?? 0);
    }

    private static LogEntry CreateLogEntry(string context, LogLevel level = LogLevel.Info)
    {
        return new LogEntry(
            loggerName: "TestLogger",
            timestamp: DateTimeOffset.Now,
            logLevel: level,
            message: "Test message",
            properties: Array.Empty<object>(),
            context: context,
            messageTemplate: LogParser.EmptyMessageTemplate,
            exception: null
        );
    }

    [Fact]
    public void ShouldEmit_SameContext_ReturnsCachedResult()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };
        var context = "TestContext";
        var entry = CreateLogEntry(context);

        // Act
        var firstCall = filter.ShouldEmit(entry, config);
        var secondCall = filter.ShouldEmit(entry, config);

        // Assert
        Assert.Equal(firstCall, secondCall);
        // 缓存应该只有一个条目
        Assert.Equal(1, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_DifferentContexts_EachGetsCached()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };
        var contexts = new[] { "Context.A", "Context.B", "Context.C" };

        // Act
        foreach (var context in contexts)
        {
            var entry = CreateLogEntry(context);
            _ = filter.ShouldEmit(entry, config);
            _ = filter.ShouldEmit(entry, config); // 第二次调用应该使用缓存
        }

        // Assert
        Assert.Equal(contexts.Length, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_WithIncludeFilter_CachesCorrectResult()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            ContextFilterIncludes = new List<string> { "Allowed" },
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };

        var allowedEntry = CreateLogEntry("Allowed.SubContext");
        var deniedEntry = CreateLogEntry("Denied.SubContext");

        // Act
        var allowedResult1 = filter.ShouldEmit(allowedEntry, config);
        var allowedResult2 = filter.ShouldEmit(allowedEntry, config);
        var deniedResult1 = filter.ShouldEmit(deniedEntry, config);
        var deniedResult2 = filter.ShouldEmit(deniedEntry, config);

        // Assert
        Assert.True(allowedResult1);
        Assert.True(allowedResult2);
        Assert.False(deniedResult1);
        Assert.False(deniedResult2);
        Assert.Equal(2, GetCacheCount(filter)); // 两个不同的上下文
    }

    [Fact]
    public void ShouldEmit_WithExcludeFilter_CachesCorrectResult()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            ContextFilterExcludes = new List<string> { "Excluded" },
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };

        var allowedEntry = CreateLogEntry("Allowed.SubContext");
        var excludedEntry = CreateLogEntry("Excluded.SubContext");

        // Act
        var allowedResult1 = filter.ShouldEmit(allowedEntry, config);
        var allowedResult2 = filter.ShouldEmit(allowedEntry, config);
        var excludedResult1 = filter.ShouldEmit(excludedEntry, config);
        var excludedResult2 = filter.ShouldEmit(excludedEntry, config);

        // Assert
        Assert.True(allowedResult1);
        Assert.True(allowedResult2);
        Assert.False(excludedResult1);
        Assert.False(excludedResult2);
        Assert.Equal(2, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_WithIncludeAndExcludeFilters_CachesCorrectResult()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            ContextFilterIncludes = new List<string> { "System" },
            ContextFilterExcludes = new List<string> { "System.Internal" },
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };

        var allowedEntry = CreateLogEntry("System.Public");
        var excludedEntry = CreateLogEntry("System.Internal");
        var notIncludedEntry = CreateLogEntry("Application.Logic");

        // Act
        var allowedResult = filter.ShouldEmit(allowedEntry, config);
        var excludedResult = filter.ShouldEmit(excludedEntry, config);
        var notIncludedResult = filter.ShouldEmit(notIncludedEntry, config);

        // 多次调用验证缓存
        _ = filter.ShouldEmit(allowedEntry, config);
        _ = filter.ShouldEmit(excludedEntry, config);
        _ = filter.ShouldEmit(notIncludedEntry, config);

        // Assert
        Assert.True(allowedResult);
        Assert.False(excludedResult);
        Assert.False(notIncludedResult);
        Assert.Equal(3, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_CaseInsensitiveFilter_CachesCorrectly()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            ContextFilterIncludes = new List<string> { "system" },
            IgnoreFilterCase = true,
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };

        var entry1 = CreateLogEntry("System.Component");
        var entry2 = CreateLogEntry("SYSTEM.Component");
        var entry3 = CreateLogEntry("system.component");

        // Act
        var result1 = filter.ShouldEmit(entry1, config);
        var result2 = filter.ShouldEmit(entry2, config);
        var result3 = filter.ShouldEmit(entry3, config);

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
        // 每个不同大小写的上下文都会被独立缓存
        Assert.Equal(3, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_CacheReachesLimit_ClearsAndRebuilds()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };
        var limit = CacheMaxCountLimit;

        // Act - 填满缓存
        for (int i = 0; i < limit; i++)
        {
            var entry = CreateLogEntry($"Context.Number{i}");
            _ = filter.ShouldEmit(entry, config);
        }

        var cacheCountBeforeOverflow = GetCacheCount(filter);

        // 触发缓存清空
        var newEntry = CreateLogEntry("Context.NewAfterLimit");
        var resultBefore = filter.ShouldEmit(newEntry, config);

        var cacheCountAfterOverflow = GetCacheCount(filter);

        // 再次访问相同上下文
        var resultAfter = filter.ShouldEmit(newEntry, config);

        // Assert
        Assert.Equal(limit, cacheCountBeforeOverflow);
        Assert.True(cacheCountAfterOverflow < limit);
        Assert.Equal(resultBefore, resultAfter);
    }

    [Fact]
    public void ShouldEmit_AfterCacheClear_OldEntriesRecomputed()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };
        var limit = CacheMaxCountLimit;
        var firstContext = "First.Context";
        var firstEntry = CreateLogEntry(firstContext);

        // 先访问第一个上下文
        var firstResult = filter.ShouldEmit(firstEntry, config);

        // 填满缓存直到触发清空
        for (int i = 0; i < limit; i++)
        {
            var entry = CreateLogEntry($"Filler.Context{i}");
            _ = filter.ShouldEmit(entry, config);
        }

        // Act - 再次访问第一个上下文（缓存已清空，需要重新计算）
        var secondResult = filter.ShouldEmit(firstEntry, config);

        // Assert
        // 结果应该相同（逻辑一致）
        Assert.Equal(firstResult, secondResult);
        // 新缓存中应该包含这个上下文
        Assert.True(GetCacheCount(filter) > 0);
    }

    [Fact]
    public void ShouldEmit_LogLevelFiltering_NotCached()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Warning,
            LogMaxLevel = LogLevel.Critical
        };

        var context = "Test.Context";
        var debugEntry = CreateLogEntry(context, LogLevel.Debug);
        var warningEntry = CreateLogEntry(context, LogLevel.Warning);

        // Act
        var debugResult = filter.ShouldEmit(debugEntry, config);
        var warningResult = filter.ShouldEmit(warningEntry, config);

        // Assert
        Assert.False(debugResult); // Debug < Warning，被过滤
        Assert.True(warningResult); // Warning >= Warning，通过
        // 日志级别检查在缓存查找之前，但上下文会被缓存
        // Debug 也会尝试缓存（虽然结果是 false）
        Assert.Equal(1, GetCacheCount(filter)); // 同一个上下文，缓存一次
    }

    [Fact]
    public void ShouldEmit_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            ContextFilterIncludes = new List<string> { "Concurrent" },
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };
        var context = "Concurrent.Test";
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        // Act - 并发访问同一上下文
        Parallel.For(0, 100, _ =>
        {
            var entry = CreateLogEntry(context);
            results.Add(filter.ShouldEmit(entry, config));
        });

        // Assert
        // 所有结果应该一致
        Assert.True(results.All(r => r == true));
        // 缓存应该只有一个条目
        Assert.Equal(1, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_ConcurrentDifferentContexts_AllGetCached()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };
        var contextCount = 50;
        var accessesPerContext = 10;

        // Act - 并发访问多个不同上下文
        Parallel.For(0, contextCount * accessesPerContext, index =>
        {
            var contextIndex = index % contextCount;
            var entry = CreateLogEntry($"Concurrent.Context{contextIndex}");
            _ = filter.ShouldEmit(entry, config);
        });

        // Assert
        Assert.Equal(contextCount, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_NoFilters_AlwaysReturnsTrue()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
            // 没有 Include 或 Exclude 过滤器
        };

        // Act
        var contexts = new[] { "Any.Context", "Another.Context", "Third.Context" };
        var results = new List<bool>();

        foreach (var context in contexts)
        {
            var entry = CreateLogEntry(context);
            results.Add(filter.ShouldEmit(entry, config));
        }

        // Assert
        Assert.True(results.All(r => r == true));
        Assert.Equal(contexts.Length, GetCacheCount(filter));
    }

    [Fact]
    public void ShouldEmit_ComplexHierarchy_CachesIndependently()
    {
        // Arrange
        var filter = new LoggerFilter();
        var config = new SinkConfiguration
        {
            ContextFilterIncludes = new List<string> { "App.Services" },
            ContextFilterExcludes = new List<string> { "App.Services.Internal" },
            LogMinLevel = LogLevel.Debug,
            LogMaxLevel = LogLevel.Critical
        };

        var contexts = new[]
        {
            "App.Services.Public",
            "App.Services.Core",
            "App.Services.Internal.Private",
            "App.Controllers",
            "App.Services"
        };

        // Act
        var results = new Dictionary<string, bool>();
        foreach (var context in contexts)
        {
            var entry = CreateLogEntry(context);
            results[context] = filter.ShouldEmit(entry, config);
            // 第二次访问验证缓存
            _ = filter.ShouldEmit(entry, config);
        }

        // Assert
        Assert.True(results["App.Services.Public"]);
        Assert.True(results["App.Services.Core"]);
        Assert.False(results["App.Services.Internal.Private"]);
        Assert.False(results["App.Controllers"]);
        Assert.True(results["App.Services"]);
        Assert.Equal(contexts.Length, GetCacheCount(filter));
    }
}
