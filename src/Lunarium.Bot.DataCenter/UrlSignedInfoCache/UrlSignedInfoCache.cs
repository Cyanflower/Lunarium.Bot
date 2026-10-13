using System.Text;

using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.Logger;

namespace Lunarium.Bot.DataCenter.UrlSignedInfoCache;

public class UrlSignedInfoCacheService
{
    private readonly ILogger _logger;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly SortedSet<UrlSignedInfo> _cache = new();

    public UrlSignedInfoCacheService(ILogger logger)
    {
        _logger = logger.ForContext("UrlSignedInfoCacheService");
    }

    /// <summary>
    /// 每天凌晨全量同步
    /// </summary>
    public void DailySync(List<UrlSignedInfo> dbRows)
    {
        _lock.EnterWriteLock();
        try
        {
            _cache.Clear();
            foreach (var row in dbRows)
            {
                _cache.Add(row);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 核心调度算法：贪婪前瞻 + 紧急优先
    /// </summary>
    /// <param name="batchSize">本批次最大处理配额</param>
    /// <param name="criticalHours">紧急窗口: 小于此时间的必须处理</param>
    /// <param name="lookAheadHours">前瞻窗口: 闲时提前处理的范围</param>
    /// <returns>待刷新的列表</returns>
    public List<UrlSignedInfo> HourlyCheck(int batchSize = 1200, int criticalHours = 2, int lookAheadHours = 12)
    {
        var itemsToRefresh = new List<UrlSignedInfo>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // 阈值计算
        long criticalThreshold = now + (criticalHours * 3600);
        long lookAheadThreshold = now + (lookAheadHours * 3600);

        int criticalCount = 0;
        int lookAheadCount = 0;

        _lock.EnterReadLock();
        try
        {
            // SortedSet 天然按过期时间从小到大排序
            // 所以最先遍历到的就是马上要过期的
            foreach (var item in _cache)
            {
                // 如果当前项已经超出了最大前瞻范围, 后面的更不用看了
                if (item.ExpireTimestamp > lookAheadThreshold)
                {
                    break;
                }

                // 判断是否属于紧急任务
                bool isCritical = item.ExpireTimestamp <= criticalThreshold;

                if (isCritical)
                {
                    // 红线机制
                    // 只要是紧急任务, 无视 batchSize 限制, 必须全部返回
                    // 如果这里导致返回数量 > batchSize, 说明 batchSize 容量不足或者同时有大量 criticalHours 以内过期的 url
                    itemsToRefresh.Add(item);
                    criticalCount++;
                }
                else
                {
                    // 填仓机制
                    // 如果不是紧急任务, 但还在前瞻窗口内, 
                    // 只有在 batchSize 还有剩余时才添加
                    if (itemsToRefresh.Count < batchSize)
                    {
                        itemsToRefresh.Add(item);
                        lookAheadCount++;
                    }
                    else
                    {
                        // batchSize 已满, 且后续的都不是紧急任务
                        // 任务完成, 停止扫描
                        break;
                    }
                }
            }
            if (itemsToRefresh.Count > batchSize)
            {
                _logger.Error("UrlSignedInfoCacheService HourlyCheck: 本轮处理的紧急任务已经超过批次限制, 缓存总数: {_cache}, 限制: {batchSize}, 本轮实际批次大小: {itemsToRefreshCount}",
                    _cache.Count, batchSize, itemsToRefresh.Count);
            }
            else
            {
                _logger.Info("本轮处理的任务: 缓存总数: {_cache}, 限制: {batchSize}, 本轮实际批次大小: {itemsToRefreshCount}, 紧急窗口: {criticalCount}, 前瞻窗口: {lookAheadCount}",
                    _cache.Count, batchSize, itemsToRefresh.Count, criticalCount, lookAheadCount);
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return itemsToRefresh;
    }

    /// <summary>
    /// 批量更新缓存
    /// </summary>
    /// <param name="updates">元组列表：(旧对象, 新对象)</param>
    public void UpdateCache(IEnumerable<(UrlSignedInfo OldItem, UrlSignedInfo? NewItem)> updates)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var (oldItem, newItem) in updates)
            {
                // 1. 移除旧项
                // 必须先移除, 因为 SortedSet 的排序依赖 ExpireTimestamp, 
                // 直接修改属性会导致树结构破坏, 必须 Remove -> Add
                if (_cache.Contains(oldItem))
                {
                    _cache.Remove(oldItem);
                }

                // 2. 添加新项 (如果是 null 则仅移除, 比如消息已被物理删除)
                if (newItem != null)
                {
                    _cache.Add(newItem);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public string GetCacheStatistics(int batchSize = 1200, int criticalHours = 2, int lookAheadHours = 12)
    {
        _lock.EnterReadLock();
        try
        {
            if (_cache.Count == 0) return "📊 缓存为空";

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 时间阈值定义
            long tCritical = now + (criticalHours * 3600);
            long tSafe = now + (12 * 3600);
            long tLookAhead = now + (lookAheadHours * 3600);

            // 全局统计计数器
            int countCritical = 0; // < 2h
            int countWraning = 0;      // 2h - 24h
            int countSafe = 0;     // > 24h

            // 下次调度预测计数
            int nextTotal = 0;
            int nextCritical = 0; // 必须处理的
            int nextFill = 0;     // 填仓的

            foreach (var item in _cache)
            {
                // --- 全局分布统计 ---
                if (item.ExpireTimestamp <= tCritical)
                {
                    countCritical++;
                }
                else if (item.ExpireTimestamp <= tSafe)
                {
                    countWraning++;
                }
                else
                {
                    countSafe++;
                }

                // --- 模拟 HourlyCheck 调度逻辑 ---
                // 只有在前瞻窗口内的才有可能被处理
                if (item.ExpireTimestamp <= tLookAhead)
                {
                    bool isItemCritical = item.ExpireTimestamp <= tCritical;

                    if (isItemCritical)
                    {
                        // 紧急任务: 无视配额, 必须处理
                        nextCritical++;
                        nextTotal++;
                    }
                    else
                    {
                        // 非紧急任务: 有配额才处理
                        if (nextTotal < batchSize)
                        {
                            nextFill++;
                            nextTotal++;
                        }
                    }
                }
            }

            // 格式化输出
            var sb = new StringBuilder();
            sb.AppendLine("📊 [URL签名缓存状态]");
            sb.AppendLine($"----------------------------------------");
            sb.AppendLine($"📦 总缓存数: {_cache.Count:N0}");
            sb.AppendLine($"----------------------------------------");
            sb.AppendLine($"🕒 分布情况:");
            sb.AppendLine($"   🔴 临期 (< {criticalHours}h): {countCritical:N0}");
            sb.AppendLine($"   🟡 预警 (< 12h): {countWraning:N0}");
            sb.AppendLine($"   🟢 安全 (> 12h): {countSafe:N0}");
            sb.AppendLine($"----------------------------------------");
            sb.AppendLine($"🔮 下次处理情况预计:");
            sb.AppendLine($"   处理参数: Batch={batchSize}, Crit={criticalHours}h, LookAhead={lookAheadHours}h");
            sb.AppendLine($"   🚀 待处理: {nextTotal:N0} / {batchSize}");
            sb.AppendLine($"      ├─ 🔥 紧急处理: {nextCritical:N0}");
            sb.AppendLine($"      └─ 🛁 填充处理: {nextFill:N0}");

            // 添加负载警告
            if (nextTotal > batchSize)
            {
                sb.AppendLine($"   ⚠️ OVERLOAD WARNING: 下轮处理的紧急任务已经超过批次限制 {nextTotal - batchSize} 个!");
            }
            else if (nextTotal == 0)
            {
                sb.AppendLine($"   💤 System Idle: 没有URL签名在 {lookAheadHours}h 内过期");
            }

            return sb.ToString();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    /// <summary>
    /// [Debug] 获取缓存的全量快照
    /// </summary>
    public List<UrlSignedInfo> GetAllItemsSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            // 返回副本以避免外部操作影响内部集合或多线程枚举问题
            return _cache.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// [Debug] 根据 ChannelId 和 MessageId 查找缓存项
    /// 注意：由于 SortedSet 是按时间排序的，此操作复杂度为 O(N)
    /// </summary>
    public UrlSignedInfo? FindByIds(ulong channelId)
    {
        _lock.EnterReadLock();
        try
        {
            return _cache.FirstOrDefault(x => x.ChannelId == channelId);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}