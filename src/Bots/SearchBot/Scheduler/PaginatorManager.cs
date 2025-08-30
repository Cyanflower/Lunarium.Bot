using System.Collections.Concurrent;

using Discord;

using SearchBot.Model;

namespace SearchBot.Scheduler;

public class PaginatorManager
{
    private readonly ConcurrentDictionary<string, PaginatorState> _activePaginators = new();

    public PaginatorManager()
    {
        // 启动一个后台清理任务
        _ = CleanupLoopAsync();
    }

    /// <summary>
    /// 注册结果 embed 列表
    /// </summary>
    /// <param name="uuid">区分列表的唯一id, 必须为用户的id+时间戳的字符串(DateTimeOffset.UtcNow.ToUnixTimeSeconds())</param>
    /// <param name="pages">embed 列表, 为List<List<Embed>>结构, 内部List长度为小于等于10</param>
    public void Register(string uuid, List<List<Embed>> pages)
    {
        var state = new PaginatorState()
        {
            Pages = pages,
            ExpiryTime = DateTime.UtcNow.AddMinutes(15)
        };
        _activePaginators.TryAdd(uuid, state);
    }
    
    /// <summary>
    /// 获取结果 embed 列表的状态信息
    /// </summary>
    /// <param name="uuid"></param>
    /// <returns></returns>
    public PaginatorState? GetState(string uuid)
    {
        _activePaginators.TryGetValue(uuid, out var state);
        return state;
    }

    /// <summary>
    /// 定时清理任务
    /// </summary>
    /// <returns></returns>
    private async Task CleanupLoopAsync()
    {
        while (true)
        {
            // 每分钟检查一次
            await Task.Delay(TimeSpan.FromMinutes(1));

            if (_activePaginators.IsEmpty) continue;

            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();

            // 找出所有已过期的键
            foreach (var pair in _activePaginators)
            {
                if (now >= pair.Value.ExpiryTime)
                {
                    expiredKeys.Add(pair.Key);
                }
            }

            // 批量移除
            foreach (var key in expiredKeys)
            {
                _activePaginators.TryRemove(key, out _);
            }
        }
    }
}