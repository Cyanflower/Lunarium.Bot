using System.Text;

using Lunarium.Bot.DataCenter.Models;
using Lunarium.Logging;

namespace Lunarium.Bot.DataCenter.UrlSignedInfoCache;

public class UrlSignedInfoCacheService
{
    private readonly ILogger _logger;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly UrlSignedBuckets _buckets = new(bucketCount: 24);

    /// <summary>
    /// 本轮计划建立完成的时刻，桶索引 = 距该时刻的完整小时数。
    /// </summary>
    private DateTimeOffset _planTime = DateTimeOffset.UtcNow;

    public UrlSignedInfoCacheService(ILogger logger)
    {
        _logger = logger.ForContext("UrlSignedInfoCacheService");
    }

    /// <summary>
    /// 每天凌晨全量同步
    /// </summary>
    public void DailySync(List<UrlSignedInfo> dbRows)
    {
        // 排序是纯 CPU 操作，放在锁外，减少写锁持有时间
        var sort = dbRows.OrderBy(x => x.ExpireTimestamp).ToList();

        _lock.EnterWriteLock();
        try
        {
            _buckets.Clear();
            _buckets.SetExpectLimit(dbRows.Count);

            foreach (UrlSignedInfo urlSigned in sort)
            {
                _buckets.AddSignedUrl(urlSigned);
            }

            // 重建完成后再定锚点：重建耗时不应计入新计划的已过小时数
            _planTime = DateTimeOffset.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 领取 0..当前小时 之间所有尚未执行的桶（正常只有一个；漏跑时合并追补），
    /// 并把它们标记为已执行。桶索引由计划建立时刻起算的小时数决定。
    /// </summary>
    public UrlCheckResult HourlyCheck()
    {
        _lock.EnterWriteLock();   // 需要修改 HasRun
        try
        {
            int current = GetCurrentBucketIndex();
            List<HourlyBucket> due = _buckets.CollectDueBuckets(current);

            if (due.Count == 0)
            {
                return new()
                {
                    UrlSigneds = new(),
                    BucketIndices = new()
                };
            }

            // wrap 条目会同时位于 bucket 0 和倒数第二个桶；同一次合并里按引用去重
            List<UrlSignedInfo> merged = new();
            HashSet<UrlSignedInfo> seen = new();
            foreach (HourlyBucket bucket in due)
            {
                foreach (UrlSignedInfo item in bucket.SignedUrls)
                {
                    if (seen.Add(item)) merged.Add(item);
                }
            }

            if (due.Count > 1)
            {
                _logger.Info(
                    "URL 桶追补: 本次合并 {BucketCount} 个桶 [{Buckets}]，共 {Count} 条",
                    due.Count,
                    string.Join(",", due.Select(b => b.Index)),
                    merged.Count);
            }

            return new()
            {
                UrlSigneds = merged,
                OutLimitCount = due.Sum(b => b.OutLimitCount),
                BucketIndices = due.Select(b => b.Index).ToList(),
                IsWrap = due.Any(b => _buckets.IsWrapBucket(b.Index))
            };
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 计划建立后已经过去的完整小时数，钳制在桶范围内。
    /// </summary>
    private int GetCurrentBucketIndex()
    {
        double elapsedHours = (DateTimeOffset.UtcNow - _planTime).TotalHours;
        return (int)Math.Clamp(Math.Floor(elapsedHours), 0, _buckets.Count - 1);
    }

    /// <summary>
    /// 把本轮处理失败的条目塞进下一个尚未执行的桶，下一小时重试。
    /// 若已经没有未执行的桶，则丢弃；这些条目不会被写 null，下一次 DailySync 会把它们重新排入计划。
    /// </summary>
    public void RequeueToNextBucket(IReadOnlyCollection<UrlSignedInfo> items)
    {
        if (items.Count == 0) return;

        _lock.EnterWriteLock();
        try
        {
            int current = GetCurrentBucketIndex();
            HourlyBucket? bucket = _buckets.GetNextUnrunBucket(current);
            if (bucket is null)
            {
                _logger.Error(
                    "URL 重试队列已无未执行的桶，本轮 {Count} 条无法回插，等待下一次 DailySync 重排",
                    items.Count);
                return;
            }

            foreach (UrlSignedInfo item in items)
            {
                bucket.AddSignedUrl(item);
            }

            _logger.Info(
                "URL 处理失败重试: {Count} 条已排入下一个未执行的桶 (Bucket={BucketIndex})",
                items.Count,
                bucket.Index);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 生成当前桶计划的统计文本（用于每日同步日志）。
    /// </summary>
    public string GetPlanStatistics()
    {
        _lock.EnterReadLock();
        try
        {
            double elapsedHours = Math.Max(0, (DateTimeOffset.UtcNow - _planTime).TotalHours);
            int current = (int)Math.Clamp(Math.Floor(elapsedHours), 0, _buckets.Count - 1);

            StringBuilder sb = new();
            sb.AppendLine($"🕐 计划锚点: {_planTime.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}，已过 {elapsedHours:F1}h，当前桶 {current}");
            sb.Append(_buckets.GetStatistics());
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
    public List<HourlyBucketSerializedObject> GetAllItemsSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            // 返回副本以避免外部操作影响内部集合或多线程枚举问题
            return _buckets.GetSerialize();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}