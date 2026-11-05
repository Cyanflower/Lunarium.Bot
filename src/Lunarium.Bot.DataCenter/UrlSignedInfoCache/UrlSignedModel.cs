
using System.Text;

using Lunarium.Bot.DataCenter.Models;

namespace Lunarium.Bot.DataCenter.UrlSignedInfoCache;

public class HourlyBucket : IEquatable<HourlyBucket>
{
    public int Index { get; }
    public HashSet<UrlSignedInfo> SignedUrls { get; init; } = new HashSet<UrlSignedInfo>();
    public int ExpectLimit { get; set; } = 0;
    public int Count => SignedUrls.Count;

    /// <summary>
    /// 本轮计划中是否已经被领取执行过（含漏跑追补合并）。
    /// </summary>
    public bool HasRun { get; private set; }

    public void MarkRun() => HasRun = true;
    public int OutLimitCount
    {
        get
        {
            return SignedUrls.Count <= ExpectLimit ? 0 : SignedUrls.Count - ExpectLimit;
        }
    }

    public HourlyBucket(int index)
    {
        Index = index;
    }

    public void AddSignedUrl(UrlSignedInfo urlSignedInfo)
    {
        SignedUrls.Add(urlSignedInfo);
    }

    public override int GetHashCode()
    {
        return Index.GetHashCode();
    }

    public bool Equals(HourlyBucket? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Index == other.Index;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as HourlyBucket);
    }

    public static bool operator ==(HourlyBucket? left, HourlyBucket? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(HourlyBucket? left, HourlyBucket? right)
    {
        return !Equals(left, right);
    }

    public HourlyBucketSerializedObject GetSerialize()
    {
        List<UrlSignedSerializedObject> urlSigneds = new();
        foreach (UrlSignedInfo url in SignedUrls)
        {
            urlSigneds.Add(new()
            {
                Url = url.Url,
                MessageId = url.MessageId,
                ChannelId = url.ChannelId,
                ExpireTimestamp = url.ExpireTimestamp,
                ExpireDateIso = DateTimeOffset.FromUnixTimeSeconds(url.ExpireTimestamp).ToString("o")
            });
        }
        return new HourlyBucketSerializedObject(){ BucketIndex = Index, UrlSignedSerializeds = urlSigneds};
    }

    public void Clear()
    {
        SignedUrls.Clear();
        HasRun = false;
    }
}

public class UrlSignedBuckets
{
    private readonly List<HourlyBucket> _buckets;
    public int Count => _buckets.Count;
    public int ExecutedCount => _buckets.Count(b => b.HasRun);
    public int PlannedCount => _buckets.Sum(b => b.Count);
    public int DistinctCount => _buckets.SelectMany(b => b.SignedUrls).Distinct().Count();

    public UrlSignedBuckets(int bucketCount)
    {
        // 至少需要 3 个桶：0 用于接缝立即刷新，Count-2 用于回绕，中间至少留 1 个正常桶
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 3);
        _buckets = new List<HourlyBucket>(bucketCount);
        for (int i = 0; i < bucketCount; i++)
        {
            _buckets.Add(new HourlyBucket(i));
        }
    }

    /// <summary>
    /// 是否为接缝桶：bucket 0（立即续命）和倒数第二个桶（回绕重置相位）。
    /// </summary>
    public bool IsWrapBucket(int index) => index == 0 || index == _buckets.Count - 2;

    public void SetExpectLimit(int totalUrl)
    {
        // 如果 totalUrl <= 0，限额通常设为 0（按业务需要调整）
        if (totalUrl <= 0)
        {
            foreach (var bucket in _buckets) bucket.ExpectLimit = 0;
            return;
        }

        // 稳定状态下条目大约在 Count-2 个桶里循环（每轮向前漂移 2 格），按这个数估算单桶水位。
        // 0 和 Count-2 这两个接缝桶会额外承接同一批条目，目标水位翻倍，避免每天误报 OutLimitCount。
        double average = (double)totalUrl / Math.Max(1, _buckets.Count - 2);

        // 向上取整到 100 的倍数
        // 例如：125 / 100.0 = 1.25 -> Ceiling 变成 2 -> 乘 100 变成 200
        int limit = (int)Math.Ceiling(average / 100.0) * 100;

        for (int i = 0; i < _buckets.Count; i++)
        {
            _buckets[i].ExpectLimit = IsWrapBucket(i) ? limit * 2 : limit;
        }
    }

    public HourlyBucket GetHourlyBucket(int Index)
    {
        return _buckets[Index];
    }

    /// <summary>
    /// 取出并标记所有 index ≤ currentIndex 且尚未执行的桶，按索引升序返回。
    /// 调用方必须持有写锁。
    /// </summary>
    public List<HourlyBucket> CollectDueBuckets(int currentIndex)
    {
        List<HourlyBucket> due = new();
        int max = Math.Min(currentIndex, _buckets.Count - 1);

        for (int i = 0; i <= max; i++)
        {
            HourlyBucket bucket = _buckets[i];
            if (bucket.HasRun) continue;

            bucket.MarkRun();
            due.Add(bucket);
        }

        return due;
    }

    /// <summary>
    /// 找到 afterIndex 之后第一个尚未执行的桶；没有则返回 null。
    /// 调用方必须持有写锁。
    /// </summary>
    public HourlyBucket? GetNextUnrunBucket(int afterIndex)
    {
        for (int i = Math.Max(0, afterIndex + 1); i < _buckets.Count; i++)
        {
            if (!_buckets[i].HasRun) return _buckets[i];
        }

        return null;
    }

    public HourlyBucket GetLeastLoadedBucket(int? cutoff = null)
    {
        // 1. 参数合法性校验：cutoff 不能为负数
        if (cutoff < 0)
            throw new ArgumentOutOfRangeException(nameof(cutoff), "Cutoff index cannot be negative.");

        // 2. 计算循环上限索引（包含该索引）
        // - 如果未指定 cutoff (null)，默认扫描全部桶，上限为 Buckets.Count - 1
        // - 如果 cutoff 超过了数组最大长度，用 Math.Min 自动兜底截断，防止 IndexOutOfRangeException
        int maxIndex = cutoff.HasValue
            ? Math.Min(cutoff.Value, _buckets.Count - 1)
            : _buckets.Count - 1;

        // 3. 打擂台算法：初始化第 0 个桶为基准擂主
        HourlyBucket leastBucket = _buckets[0];
        int minCount = leastBucket.Count;

        // 4. 只循环到 maxIndex（注意这里是 <= maxIndex）
        for (int i = 1; i <= maxIndex; i++)
        {
            int currentCount = _buckets[i].Count;
            // 如果是 <，当遇到数量一样时，不更新，保留前面的（索引小的）
            // 如果改成 <=，当遇到数量一样时，就会更新为后面的（索引大的）
            if (currentCount <= minCount)
            {
                minCount = currentCount;
                leastBucket = _buckets[i];
            }
        }

        return leastBucket;
    }

    public void AddSignedUrl(UrlSignedInfo urlSigned)
    {
        // GetRemainingHours 已向下取整，cutoff <= 0 等价于剩余不足 3 小时
        int cutoff = urlSigned.GetRemainingHours() - 2;

        // 濒危的链接会同时进入 bucket 0 和倒数第二个桶，这两个桶的 Count 会提前上升。
        // 后面的正常链接走 GetLeastLoadedBucket 时会自然避开它们，填补到其他较空的桶。
        if (cutoff <= 0)
        {
            // 1) 放进 bucket 0：本次检查立即续命
            GetHourlyBucket(0).AddSignedUrl(urlSigned);

            // 2) 再放进倒数第二个桶：约 22 小时后重置一次相位，避免下一次同步时落在零余量上
            GetHourlyBucket(_buckets.Count - 2).AddSignedUrl(urlSigned);
        }
        else
        {
            // 寿命充裕的正常链接：按原本的打擂台算法挑一个负载最低的桶
            GetLeastLoadedBucket(cutoff).AddSignedUrl(urlSigned);
        }
    }

    public List<HourlyBucketSerializedObject> GetSerialize()
    {
        List<HourlyBucketSerializedObject> list = new();
        foreach (var bucket in _buckets)
        {
            list.Add(bucket.GetSerialize());
        }
        return list;
    }

    /// <summary>
    /// 生成计划概览（纯读取，调用方需持有读锁）。
    /// </summary>
    public string GetStatistics()
    {
        StringBuilder sb = new();
        sb.AppendLine("📊 [URL签名桶计划]");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"📦 唯一条目: {DistinctCount:N0}，计划执行: {PlannedCount:N0} 次（含接缝重复）");
        sb.AppendLine($"🪣 桶: {Count}，已执行: {ExecutedCount}，未执行: {Count - ExecutedCount}");
        sb.AppendLine($"🔁 接缝桶: bucket 0 = {_buckets[0].Count:N0}，bucket {Count - 2} = {_buckets[Count - 2].Count:N0}");
        sb.AppendLine("----------------------------------------");

        List<HourlyBucket> nonEmpty = _buckets.Where(b => b.Count > 0).ToList();
        if (nonEmpty.Count == 0)
        {
            sb.AppendLine("💤 计划为空");
            return sb.ToString();
        }

        HourlyBucket fullest = nonEmpty.MaxBy(b => b.Count)!;
        HourlyBucket emptiest = nonEmpty.MinBy(b => b.Count)!;
        sb.AppendLine($"📈 最满: bucket {fullest.Index} = {fullest.Count:N0} / {fullest.ExpectLimit:N0}");
        sb.AppendLine($"📉 最空(非空): bucket {emptiest.Index} = {emptiest.Count:N0}");

        List<HourlyBucket> over = _buckets.Where(b => b.OutLimitCount > 0).ToList();
        if (over.Count > 0)
        {
            sb.AppendLine($"⚠️ 超计划水位: {over.Count} 个桶，共 {over.Sum(b => b.OutLimitCount):N0} 条");
            sb.AppendLine("   " + string.Join("，", over.Select(b => $"bucket {b.Index}: +{b.OutLimitCount:N0}")));
        }
        else
        {
            sb.AppendLine("✅ 所有桶均未超过计划水位");
        }

        sb.AppendLine("----------------------------------------");
        sb.AppendLine("每桶负载（✔=已执行）:");
        sb.AppendLine(string.Join("  ", _buckets.Select(b => $"[{b.Index:D2}]{(b.HasRun ? "✔" : "·")}{b.Count}")));

        return sb.ToString();
    }

    public void Clear()
    {
        foreach (HourlyBucket bucket in _buckets)
        {
            bucket.Clear();
        }
    }
}

public record UrlSignedSerializedObject
{
    public string? Url { get; init; }
    public ulong ChannelId { get; init; }
    public ulong MessageId { get; init; }
    public long ExpireTimestamp { get; init; }
    public string? ExpireDateIso { get; init; }
}

public record HourlyBucketSerializedObject
{
    public int BucketIndex { get; init; }
    public List<UrlSignedSerializedObject> UrlSignedSerializeds { get; init; } = new();
}

public record UrlCheckResult
{
    public required List<UrlSignedInfo> UrlSigneds { get; init; }
    public int OutLimitCount { get; init; }

    /// <summary>
    /// 本次实际执行的桶索引。正常只有一个；漏跑追补时会有多个。
    /// </summary>
    public List<int> BucketIndices { get; init; } = new();

    /// <summary>
    /// 本次执行的桶中是否包含接缝桶（bucket 0 或 Count-2）。
    /// </summary>
    public bool IsWrap { get; init; }
}