
using System.Collections.Concurrent;
using LunariumBot.Net.SearchBot.Models;

namespace LunariumBot.Net.SearchBot.SearchCache;

public class SearchCacheService
{
    // 弱引用对象缓存池
    private readonly ConcurrentDictionary<ulong, WeakReference<PostDisplayModel>> _cache = new();
    public Dictionary<ulong, TagData> TagCache => new(_tagCache);
    public Dictionary<ulong, ChannelData> ChannelCache => new(_channelCache);
    private ConcurrentDictionary<ulong, TagData> _tagCache = new();
    private ConcurrentDictionary<ulong, ChannelData> _channelCache = new();
    /// <summary>
    /// 将对象对象到缓存池
    /// </summary>
    /// <param name="items"></param>
    /// <returns></returns>
    public List<PostDisplayModel> CacheItems(IReadOnlyList<PostDisplayModel> items)
    {
        var results = new List<PostDisplayModel>(items.Count);

        foreach (var item in items)
        {
            results.Add(ProcessItem(item));
        }

        return results;
    }

    public void UpdateChannelData(List<ChannelData> channelDatas)
    {
        _channelCache = new();
        foreach (var channel in channelDatas)
        {
            _channelCache[channel.ChannelId] = channel;
        }
    }

    public void UpdateTagData(List<TagData> tagDatas)
    {
        _tagCache = new();
        foreach (var tag in tagDatas)
        {
            _tagCache[tag.TagId] = tag;
        }
    }

    /// <summary>
    /// 处理对象
    /// </summary>
    /// <param name="newItem"></param>
    /// <returns></returns>
    private PostDisplayModel ProcessItem(PostDisplayModel newItem)
    {
        // 自旋重试, 应对高并发极小概率的竞争
        while (true)
        {
            // 尝试获取现有的 WeakReference
            if (_cache.TryGetValue(newItem.ThreadId, out var weakRef))
            {
                // 尝试从 WeakReference 拿到活的对象
                if (weakRef.TryGetTarget(out var liveItem))
                {
                    // 命中缓存
                    // 为了保持引用一致, 这里不直接替换, 直接更新对象内部数据内容
                    // 这样所有持有 liveItem 的分页器都能看到更新
                    // 也确保了不会有脱离了缓存列表但还因为分页器持有旧引用导致存活的孤立对象
                    liveItem.Update(newItem);
                    return liveItem;
                }
                else
                {
                    // 引用还在字典里, 但对象已经死(被GC)了
                    // 尝试把这个死引用替换成新的
                    var newWeakRef = new WeakReference<PostDisplayModel>(newItem);
                    // 只有当字典里的值还是刚才那个 weakRef 时才替换
                    if (_cache.TryUpdate(newItem.ThreadId, newWeakRef, weakRef))
                    {
                        return newItem;
                    }
                    // 替换失败(说明有别的线程抢先更新了), continue 重试
                    continue;
                }
            }
            else
            {
                // 字典里完全没有, 尝试新增
                var newWeakRef = new WeakReference<PostDisplayModel>(newItem);
                if (_cache.TryAdd(newItem.ThreadId, newWeakRef))
                {
                    return newItem;
                }
                // 新增失败(说明有别的线程抢先新增了), continue 重试去 Get
                continue;
            }
        }
    }
}