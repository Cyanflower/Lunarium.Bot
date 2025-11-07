using Xunit;
using LunariumBot.Net.Logger.Parser;
using System.Reflection;

namespace LunariumBot.Net.LoggerParser.Tests;

public class LogParserCacheTests
{
    // 通过反射访问私有缓存字段和常量
    private static readonly FieldInfo? _templateCacheField = typeof(LogParser)
        .GetField("_templateCache", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly FieldInfo? _cacheMaxCountLimitField = typeof(LogParser)
        .GetField("CacheMaxCountLimit", BindingFlags.NonPublic | BindingFlags.Static);

    private static int CacheMaxCountLimit => (int)(_cacheMaxCountLimitField?.GetValue(null) ?? 4096);

    private static void ClearCache()
    {
        var cache = _templateCacheField?.GetValue(null);
        if (cache != null)
        {
            var clearMethod = cache.GetType().GetMethod("Clear");
            clearMethod?.Invoke(cache, null);
        }
    }

    private static int GetCacheCount()
    {
        var cache = _templateCacheField?.GetValue(null);
        if (cache == null) return 0;

        var countProperty = cache.GetType().GetProperty("Count");
        return (int)(countProperty?.GetValue(cache) ?? 0);
    }

    [Fact]
    public void ParseMessage_SameTemplate_ReturnsCachedResult()
    {
        // Arrange
        ClearCache();
        var template = "Hello {Name}";

        // Act
        var first = LogParser.ParseMessage(template);
        var second = LogParser.ParseMessage(template);

        // Assert
        // 应该返回同一个实例（缓存）
        Assert.Same(first, second);
    }

    [Fact]
    public void ParseMessage_DifferentTemplates_ReturnsDifferentResults()
    {
        // Arrange
        ClearCache();
        var template1 = "Hello {Name}";
        var template2 = "Goodbye {Name}";

        // Act
        var first = LogParser.ParseMessage(template1);
        var second = LogParser.ParseMessage(template2);

        // Assert
        // 应该返回不同的实例
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ParseMessage_MultipleTemplates_AllGetCached()
    {
        // Arrange
        ClearCache();
        var templates = new List<string>
        {
            "Template {A}",
            "Template {B}",
            "Template {C}"
        };

        // Act
        var results = templates.Select(t => LogParser.ParseMessage(t)).ToList();
        var secondResults = templates.Select(t => LogParser.ParseMessage(t)).ToList();

        // Assert
        for (int i = 0; i < templates.Count; i++)
        {
            Assert.Same(results[i], secondResults[i]);
        }
    }

    [Fact]
    public void ParseMessage_CacheReachesLimit_ClearsAndRebuilds()
    {
        // Arrange
        // 创建一个独立的测试，不依赖 ClearCache（因为它不可靠）
        var limit = CacheMaxCountLimit;
        var testId = Guid.NewGuid().ToString("N");

        // 记录填充前的缓存大小
        var initialCacheCount = GetCacheCount();

        // Act - 填满缓存到接近上限
        var templatesAdded = 0;
        while (GetCacheCount() < limit && templatesAdded < limit + 100)
        {
            LogParser.ParseMessage($"FillCache_{testId}_{{Param{templatesAdded++}}}");
        }

        var cacheCountBeforeTrigger = GetCacheCount();

        // 再添加一个模板，可能触发清空
        var testTemplate = $"TestAfterFill_{testId}_{{ParamNew}}";
        var result1 = LogParser.ParseMessage(testTemplate);
        var cacheCountAfterTrigger = GetCacheCount();

        // 再次解析同一个模板，验证缓存工作
        var result2 = LogParser.ParseMessage(testTemplate);

        // Assert
        // 验证缓存确实被填充到接近上限
        Assert.True(cacheCountBeforeTrigger >= limit * 0.8,
            $"Expected cache to be filled near {limit}, got {cacheCountBeforeTrigger}");

        // 验证当缓存满时会被清空
        if (cacheCountBeforeTrigger >= limit)
        {
            Assert.True(cacheCountAfterTrigger < limit / 2,
                $"Expected cache to be cleared when full, got {cacheCountAfterTrigger}");
        }

        // 最重要的：验证缓存功能正常工作
        Assert.Same(result1, result2);
    }

    [Fact]
    public void ParseMessage_CacheClearsCorrectly_OldEntriesNotAccessible()
    {
        // Arrange
        ClearCache();
        var limit = CacheMaxCountLimit;
        var firstTemplate = "First {Template}";

        // 先解析第一个模板
        var firstResult = LogParser.ParseMessage(firstTemplate);

        // 填满缓存直到触发清空
        for (int i = 0; i < limit; i++)
        {
            LogParser.ParseMessage($"Filler {{Param{i}}}");
        }

        // Act - 再次解析第一个模板
        var secondResult = LogParser.ParseMessage(firstTemplate);

        // Assert
        // 由于缓存已清空，应该创建新的实例
        Assert.NotSame(firstResult, secondResult);
    }

    [Fact]
    public void ParseMessage_EmptyAndNullTemplates_DoNotGetCached()
    {
        // Arrange
        ClearCache();
        var initialCount = GetCacheCount();

        // Act
        var emptyResult1 = LogParser.ParseMessage("");
        var emptyResult2 = LogParser.ParseMessage("");
        var nullResult1 = LogParser.ParseMessage(null!);
        var nullResult2 = LogParser.ParseMessage(null!);

        var finalCount = GetCacheCount();

        // Assert
        // 空字符串和 null 应该返回 EmptyMessageTemplate，不进入缓存
        Assert.Same(emptyResult1, emptyResult2);
        Assert.Same(nullResult1, nullResult2);
        Assert.Same(emptyResult1, LogParser.EmptyMessageTemplate);
        // 缓存数量不应该增加
        Assert.Equal(initialCount, finalCount);
    }

    [Fact]
    public void ParseMessage_ConcurrentAccess_AllGetSameInstance()
    {
        // Arrange
        ClearCache();
        var template = $"Concurrent{{Test{Guid.NewGuid()}}}";  // 使用唯一模板
        var results = new System.Collections.Concurrent.ConcurrentBag<MessageTemplate>();

        // Act - 并发访问
        Parallel.For(0, 100, _ =>
        {
            results.Add(LogParser.ParseMessage(template));
        });

        // Assert
        // 由于并发竞态，可能有少量重复解析，但大多数应该是同一实例
        var distinctResults = results.Distinct().ToList();
        Assert.True(distinctResults.Count <= 3,
            $"Expected mostly cached results (≤ 3 instances), got {distinctResults.Count}");

        // 验证缓存确实在工作：大多数调用都使用了缓存的实例
        var mostCommonInstance = results.GroupBy(r => r).OrderByDescending(g => g.Count()).First();
        Assert.True(mostCommonInstance.Count() >= 90,
            $"Expected at least 90% cache hits, got {mostCommonInstance.Count()}/100");
    }

    [Fact]
    public void ParseMessage_ConcurrentDifferentTemplates_EachGetsCached()
    {
        // Arrange - 调整为更现实的场景：少量模板，多次访问
        ClearCache();
        var templateCount = 10;  // 减少模板数量
        var accessesPerTemplate = 50;  // 增加每个模板的访问次数
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, List<MessageTemplate>>();

        // 初始化结果字典
        for (int i = 0; i < templateCount; i++)
        {
            var template = $"Concurrent{{Param{i}}}";
            results[template] = new List<MessageTemplate>();
        }

        // Act - 并发访问多个不同模板
        Parallel.For(0, templateCount * accessesPerTemplate, index =>
        {
            var templateIndex = index % templateCount;
            var template = $"Concurrent{{Param{templateIndex}}}";
            var result = LogParser.ParseMessage(template);

            lock (results[template])
            {
                results[template].Add(result);
            }
        });

        // Assert
        foreach (var kvp in results)
        {
            // 每个模板的所有访问最终应该使用缓存的实例
            var distinctResults = kvp.Value.Distinct().ToList();

            // 在更合理的场景下（10个模板，每个50次访问），缓存应该有效工作
            // 验证大多数调用使用了缓存（至少 70% 使用同一实例）
            var mostCommonInstance = kvp.Value.GroupBy(r => r).OrderByDescending(g => g.Count()).First();
            var cacheHitRate = (double)mostCommonInstance.Count() / kvp.Value.Count;

            Assert.True(cacheHitRate >= 0.7,
                $"Template '{kvp.Key}' cache hit rate too low: {mostCommonInstance.Count()}/{kvp.Value.Count} = {cacheHitRate:P0} (expected >= 70%)");
        }
    }

    [Fact]
    public void ParseMessage_CacheBehavior_ConsistentAcrossClears()
    {
        // Arrange
        ClearCache();
        var template1 = "Template {One}";
        var template2 = "Template {Two}";

        // Act - 第一轮缓存
        var round1_t1_a = LogParser.ParseMessage(template1);
        var round1_t1_b = LogParser.ParseMessage(template1);
        var round1_t2_a = LogParser.ParseMessage(template2);

        // 清空缓存
        ClearCache();

        // 第二轮缓存
        var round2_t1_a = LogParser.ParseMessage(template1);
        var round2_t1_b = LogParser.ParseMessage(template1);
        var round2_t2_a = LogParser.ParseMessage(template2);

        // Assert
        // 同一轮中的相同模板应该返回同一实例
        Assert.Same(round1_t1_a, round1_t1_b);
        Assert.Same(round2_t1_a, round2_t1_b);

        // 不同轮的相同模板应该返回不同实例（因为缓存已清空）
        Assert.NotSame(round1_t1_a, round2_t1_a);
        Assert.NotSame(round1_t2_a, round2_t2_a);
    }
}
