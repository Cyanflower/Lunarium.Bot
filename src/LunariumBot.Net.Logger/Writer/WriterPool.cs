using System.Collections.Concurrent;

using LunariumBot.Net.Logger.Writer;

namespace LunariumBot.Net.Logger.Writer;

internal static class WriterPool
{
    public static int MaxBufferCapacity { get; set; } = 4 * 1024;

    public static T Get<T>() where T : LogWriter, new()
    {
        return Pool<T>.Get();
    }

    public static void Return<T>(T writer) where T : LogWriter, new()
    {
        Pool<T>.Return(writer);
    }

    private static class Pool<T> where T : LogWriter, new()
    {
        // Writer 对象池
        private static readonly ConcurrentBag<T> _pool = new();
        // 池内对象上限
        private const int PoolMaxSize = 100;

        /// <summary>
        /// 尝试获取一个池内对象, 池内为空则返回一个新对象
        /// </summary>
        /// <returns>T 池内对象</returns>
        internal static T Get()
        {
            // 尝试获取池内对象
            if (_pool.TryTake(out var writer))
            {
                return writer;
            }
            // 池为空则返回新对象
            return new T();
        }

        internal static void Return(T writer)
        {
            // 池内对象数量是否到达限制
            // 会有可能的临界时竞态条件, 但考虑到实现简单度, 维护直观, 和性能(比起复杂的多线程锁机制)
            // 并且明显可以容忍和接受其竞态后果(PoolSize只在某一段的短时间会大于100, 可能是101或102左右)
            // 这样就可以了
            if (_pool.Count >= PoolMaxSize)
            {
                // 直接返回, 不回池内
                return;
            }
            // 对象大小是否超限
            if (writer.TryReset(MaxBufferCapacity))
            {
                // 入池
                _pool.Add(writer);
            }
        }
    }
}