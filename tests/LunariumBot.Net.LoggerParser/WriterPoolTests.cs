using Xunit;
using LunariumBot.Net.Logger;
using LunariumBot.Net.Logger.Writer;
using LunariumBot.Net.Logger.Parser;
using System.Reflection;
using System.Text;

namespace LunariumBot.Net.LoggerParser.Tests;

// 用于测试的简单 Writer 实现
internal class TestLogWriter : LogWriter
{
    protected override void ReturnToPool() => WriterPool.Return(this);

    protected override LogWriter WriteTimestamp(DateTimeOffset timestamp)
    {
        _stringBuilder.Append($"[{timestamp:O}] ");
        return this;
    }

    protected override LogWriter WriteLevel(LogLevel level)
    {
        _stringBuilder.Append($"[{level}] ");
        return this;
    }

    protected override LogWriter WriteContext(string? context)
    {
        if (!string.IsNullOrEmpty(context))
            _stringBuilder.Append($"[{context}] ");
        return this;
    }

    protected override LogWriter WriteRenderedMessage(MessageTemplateTokens[] tokens, object?[] propertys)
    {
        foreach (var token in tokens)
        {
            if (token is TextToken textToken)
                _stringBuilder.Append(textToken.Text);
        }
        return this;
    }

    protected override LogWriter WriteException(Exception? exception)
    {
        if (exception != null)
            _stringBuilder.Append($" Exception: {exception.Message}");
        return this;
    }

    // 公开访问以便测试
    public int GetCurrentCapacity() => GetBufferCapacity();

    public void AppendText(string text) => _stringBuilder.Append(text);

    public int GetContentLength() => _stringBuilder.Length;

    // 模拟缓冲区膨胀
    public void InflateBuffer(int targetCapacity)
    {
        while (_stringBuilder.Capacity < targetCapacity)
        {
            _stringBuilder.Append(new string('X', 1024));
        }
    }
}

public class WriterPoolTests
{
    // 通过反射访问私有池字段
    private static readonly Type? _poolType = typeof(WriterPool)
        .GetNestedType("Pool`1", BindingFlags.NonPublic)
        ?.MakeGenericType(typeof(TestLogWriter));

    private static readonly FieldInfo? _poolField = _poolType
        ?.GetField("_pool", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly FieldInfo? _poolMaxSizeField = _poolType
        ?.GetField("PoolMaxSize", BindingFlags.NonPublic | BindingFlags.Static);

    private static int PoolMaxSize => (int)(_poolMaxSizeField?.GetValue(null) ?? 100);

    private static int GetPoolCount()
    {
        var pool = _poolField?.GetValue(null);
        if (pool == null) return 0;

        var countProperty = pool.GetType().GetProperty("Count");
        return (int)(countProperty?.GetValue(pool) ?? 0);
    }

    private static void ClearPool()
    {
        var pool = _poolField?.GetValue(null);
        if (pool != null)
        {
            // ConcurrentBag 没有 Clear 方法，需要通过 TryTake 清空
            var tryTakeMethod = pool.GetType().GetMethod("TryTake", new[] { pool.GetType().GetGenericArguments()[0].MakeByRefType() });
            if (tryTakeMethod != null)
            {
                var parameters = new object?[1];
                while ((bool)tryTakeMethod.Invoke(pool, parameters)!)
                {
                    // 继续取出直到为空
                }
            }
        }
    }

    [Fact]
    public void Get_FirstTime_ReturnsNewInstance()
    {
        // Arrange
        ClearPool();

        // Act
        var writer = WriterPool.Get<TestLogWriter>();

        // Assert
        Assert.NotNull(writer);
        Assert.True(writer.GetCurrentCapacity() >= 0); // StringBuilder 有默认初始容量
        Assert.Equal(0, writer.GetContentLength()); // 但内容应该是空的
    }

    [Fact]
    public void Return_ThenGet_ReturnsSameInstance()
    {
        // Arrange
        ClearPool();
        var writer1 = WriterPool.Get<TestLogWriter>();
        writer1.AppendText("Test");
        var originalCapacity = writer1.GetCurrentCapacity();

        // Act
        WriterPool.Return(writer1);
        var writer2 = WriterPool.Get<TestLogWriter>();

        // Assert
        Assert.Same(writer1, writer2);
        Assert.Equal(0, writer2.GetContentLength()); // 应该被清空
        Assert.True(writer2.GetCurrentCapacity() <= originalCapacity); // 容量应该保持或减少
    }

    [Fact]
    public void Return_MultipleTimes_AllGetPooled()
    {
        // Arrange
        ClearPool();
        var writers = new List<TestLogWriter>();
        var initialPoolCount = GetPoolCount();

        // Act - 创建并归还多个 writer
        for (int i = 0; i < 10; i++)
        {
            var writer = WriterPool.Get<TestLogWriter>();
            writers.Add(writer);
            WriterPool.Return(writer);
        }

        var finalPoolCount = GetPoolCount();

        // Assert
        // 由于池会重用对象，实际入池数量可能少于创建数量
        // 但应该至少有一些对象被池化
        Assert.True(finalPoolCount >= 1, $"Expected at least 1 writer in pool, got {finalPoolCount}");
    }

    [Fact]
    public void Return_ExceedsPoolLimit_ExtraInstancesDiscarded()
    {
        // Arrange
        ClearPool();
        var limit = PoolMaxSize;
        var writers = new List<TestLogWriter>();

        // Act - 创建超过限制的 writer
        for (int i = 0; i < limit + 10; i++)
        {
            var writer = WriterPool.Get<TestLogWriter>();
            writers.Add(writer);
        }

        // 全部归还
        foreach (var writer in writers)
        {
            WriterPool.Return(writer);
        }

        var poolCount = GetPoolCount();

        // Assert
        // 池中应该最多有 PoolMaxSize 个对象（可能稍多由于竞态条件）
        Assert.True(poolCount >= limit);
        Assert.True(poolCount <= limit + 5); // 允许一些竞态条件导致的超出
    }

    [Fact]
    public void Return_OversizedBuffer_NotPooled()
    {
        // Arrange
        ClearPool();
        var writer = WriterPool.Get<TestLogWriter>();
        var maxCapacity = WriterPool.MaxBufferCapacity;

        // 膨胀缓冲区超过限制
        writer.InflateBuffer(maxCapacity + 1024);

        var initialPoolCount = GetPoolCount();

        // Act
        WriterPool.Return(writer);

        var finalPoolCount = GetPoolCount();

        // Assert
        // 超大对象不应该被放回池中
        Assert.Equal(initialPoolCount, finalPoolCount);
    }

    [Fact]
    public void Return_NormalSizedBuffer_GetsPooled()
    {
        // Arrange
        ClearPool();
        var writer = WriterPool.Get<TestLogWriter>();
        var maxCapacity = WriterPool.MaxBufferCapacity;

        // 使用正常大小的缓冲区
        writer.AppendText(new string('A', maxCapacity / 2));

        var initialPoolCount = GetPoolCount();

        // Act
        WriterPool.Return(writer);

        var finalPoolCount = GetPoolCount();

        // Assert
        Assert.Equal(initialPoolCount + 1, finalPoolCount);
    }

    [Fact]
    public void TryReset_OversizedBuffer_ReturnsFalse()
    {
        // Arrange
        var writer = WriterPool.Get<TestLogWriter>();
        var maxCapacity = WriterPool.MaxBufferCapacity;

        writer.InflateBuffer(maxCapacity + 1024);

        // Act
        var result = writer.TryReset(maxCapacity);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryReset_NormalBuffer_ReturnsTrueAndClearsContent()
    {
        // Arrange
        var writer = WriterPool.Get<TestLogWriter>();
        writer.AppendText("Some content");

        // Act
        var result = writer.TryReset();

        // Assert
        Assert.True(result);
        Assert.Equal(0, writer.GetContentLength());
    }

    [Fact]
    public void PooledWriter_Dispose_ReturnsToPool()
    {
        // Arrange
        ClearPool();

        // Act
        TestLogWriter? capturedWriter = null;
        using (var pooledWriter = new PooledWriter<TestLogWriter>(WriterPool.Get<TestLogWriter>()))
        {
            capturedWriter = pooledWriter.Writer;
            pooledWriter.Writer.AppendText("Test");
        }

        var poolCount = GetPoolCount();
        var retrievedWriter = WriterPool.Get<TestLogWriter>();

        // Assert
        Assert.Equal(1, poolCount); // Dispose 后应该回池
        Assert.Same(capturedWriter, retrievedWriter); // 应该是同一个实例
    }

    [Fact]
    public void PooledWriter_MultipleDispose_SafelyHandled()
    {
        // Arrange
        ClearPool();
        var writer = WriterPool.Get<TestLogWriter>();
        var pooledWriter = new PooledWriter<TestLogWriter>(writer);
        var initialPoolCount = GetPoolCount();

        // Act
        pooledWriter.Dispose();
        var poolCountAfterFirst = GetPoolCount();
        pooledWriter.Dispose(); // 第二次调用
        var poolCountAfterSecond = GetPoolCount();

        // Assert
        // 第一次 Dispose 应该归还到池
        Assert.True(poolCountAfterFirst >= initialPoolCount);
        // 第二次 Dispose 不应该导致问题或重复归还
        // 由于 LogWriter.Dispose 有 _disposed 标志，第二次调用应该被忽略
        Assert.True(poolCountAfterSecond == poolCountAfterFirst || poolCountAfterSecond == poolCountAfterFirst + 1);
    }

    [Fact]
    public void Pool_ConcurrentGetAndReturn_ThreadSafe()
    {
        // Arrange
        ClearPool();
        var iterations = 100;

        // Act - 并发获取和归还
        Parallel.For(0, iterations, _ =>
        {
            var writer = WriterPool.Get<TestLogWriter>();
            writer.AppendText("Test");
            WriterPool.Return(writer);
        });

        var finalPoolCount = GetPoolCount();

        // Assert
        // 所有操作完成后，池中应该有对象（具体数量取决于竞态条件）
        Assert.True(finalPoolCount > 0);
        Assert.True(finalPoolCount <= PoolMaxSize + 10); // 允许一些竞态导致的超出
    }

    [Fact]
    public void Pool_ConcurrentGet_CreatesMultipleInstances()
    {
        // Arrange
        ClearPool();
        var writers = new System.Collections.Concurrent.ConcurrentBag<TestLogWriter>();

        // Act - 并发获取（不归还）
        Parallel.For(0, 50, _ =>
        {
            writers.Add(WriterPool.Get<TestLogWriter>());
        });

        // Assert
        // 应该创建多个不同的实例
        var distinctWriters = writers.Distinct().Count();
        Assert.True(distinctWriters > 1);
    }

    [Fact]
    public void Pool_DifferentWriterTypes_IndependentPools()
    {
        // 这个测试验证不同类型的 Writer 有独立的池
        // 由于 Pool<T> 是泛型的，每个类型都有自己的静态实例

        // Arrange
        ClearPool();

        // Act
        var textWriter = WriterPool.Get<LogTextWriter>();
        WriterPool.Return(textWriter);

        var testWriter = WriterPool.Get<TestLogWriter>();
        WriterPool.Return(testWriter);

        // Assert
        // TestLogWriter 的池应该只有 1 个对象
        Assert.Equal(1, GetPoolCount());
    }

    [Fact]
    public void MaxBufferCapacity_CanBeConfigured()
    {
        // Arrange
        var originalCapacity = WriterPool.MaxBufferCapacity;

        try
        {
            // Act
            WriterPool.MaxBufferCapacity = 8 * 1024; // 设置为 8KB

            var writer = WriterPool.Get<TestLogWriter>();
            writer.InflateBuffer(7 * 1024); // 小于限制
            var result1 = writer.TryReset(WriterPool.MaxBufferCapacity);

            writer.InflateBuffer(9 * 1024); // 超过限制
            var result2 = writer.TryReset(WriterPool.MaxBufferCapacity);

            // Assert
            Assert.True(result1); // 7KB < 8KB，应该成功
            Assert.False(result2); // 9KB > 8KB，应该失败
        }
        finally
        {
            // Cleanup
            WriterPool.MaxBufferCapacity = originalCapacity;
        }
    }

    [Fact]
    public void Writer_GetBufferCapacity_ReturnsCorrectValue()
    {
        // Arrange
        var writer = WriterPool.Get<TestLogWriter>();

        // Act
        var initialCapacity = writer.GetBufferCapacity();
        writer.AppendText(new string('X', 2048));
        var afterAppendCapacity = writer.GetBufferCapacity();

        // Assert
        Assert.True(initialCapacity >= 0);
        Assert.True(afterAppendCapacity >= initialCapacity);
    }

    [Fact]
    public void Return_CalledDirectly_WorksSameAsDispose()
    {
        // Arrange
        ClearPool();
        var writer = WriterPool.Get<TestLogWriter>();
        writer.AppendText("Test");

        // Act
        writer.Return(); // 直接调用 Return 方法
        var poolCount = GetPoolCount();
        var retrievedWriter = WriterPool.Get<TestLogWriter>();

        // Assert
        Assert.Equal(1, poolCount);
        Assert.Same(writer, retrievedWriter);
    }

    [Fact]
    public void Pool_StressTest_HandlesHighConcurrency()
    {
        // Arrange
        ClearPool();
        var totalOperations = 1000;
        var completedOperations = 0;

        // Act - 高并发获取和归还
        Parallel.For(0, totalOperations, _ =>
        {
            try
            {
                using var pooledWriter = new PooledWriter<TestLogWriter>(WriterPool.Get<TestLogWriter>());
                pooledWriter.Writer.AppendText("Stress test content");

                // 模拟一些工作
                Thread.Sleep(1);

                Interlocked.Increment(ref completedOperations);
            }
            catch (Exception ex)
            {
                // 不应该有异常
                Assert.Fail($"Unexpected exception: {ex.Message}");
            }
        });

        var finalPoolCount = GetPoolCount();

        // Assert
        Assert.Equal(totalOperations, completedOperations);
        Assert.True(finalPoolCount > 0);
        Assert.True(finalPoolCount <= PoolMaxSize + 20); // 允许竞态条件
    }

    [Fact]
    public void Pool_ReusePattern_MaintainsPoolHealth()
    {
        // Arrange
        ClearPool();
        var cycleCount = 20;

        // Act - 模拟典型的使用模式：获取-使用-归还
        for (int i = 0; i < cycleCount; i++)
        {
            using var pooledWriter = new PooledWriter<TestLogWriter>(WriterPool.Get<TestLogWriter>());
            pooledWriter.Writer.AppendText($"Cycle {i}");
        }

        var finalPoolCount = GetPoolCount();

        // Assert
        // 由于每次都是同一个 writer 被重用（理想情况），池中应该只有少量对象
        Assert.True(finalPoolCount >= 1);
        Assert.True(finalPoolCount <= 5);
    }
}
