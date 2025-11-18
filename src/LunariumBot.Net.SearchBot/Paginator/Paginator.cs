using System.Collections.Concurrent;
using System.Reactive.Disposables;
using NanoidDotNet;

namespace LunariumBot.Net.SearchBot.Paginator;

public class Paginator : IDisposable
{
    const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    const int Expire = 1; // 小时
    private CancellationTokenSource _cts = new(); 
    private readonly ConcurrentDictionary<string, IPaginatedView> _paginatedViews = new();
    public Paginator()
    {
        _ = Task.Run(async () => { await CleanupLoopAsync(); });
    }
    
    public void RegisterView<T>(ulong userId, IEnumerable<T> data) where T : class
    {
        // 生成uuid, 867658156510150716 + abcde12345 (18/19位 Snowflake ID + 10位 NanoId, 占用 CustomId 28 ~ 29个字符)
        var key = $"{userId}-{Nanoid.Generate(Alphabet, 10)}";
        // 生成过期时间
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Expire);
        // 创建 IPaginatedView
        var view = PaginatedViewFactory.Create(data, expiresAt, key);
        // 添加到缓存
        _paginatedViews.TryAdd(key, view);
    }

    private async Task CleanupLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            // 每分钟检查一次
            await Task.Delay(TimeSpan.FromMinutes(1));

            if (_paginatedViews.IsEmpty) continue;

            var now = DateTimeOffset.UtcNow;
            var expiredKeys = new List<string>();

            foreach (var pair in _paginatedViews)
            {
                if (now >= pair.Value.ExpiresAt) expiredKeys.Add(pair.Key);
            }

            foreach (var key in expiredKeys)
            {
                _paginatedViews.TryRemove(key, out _);
            }
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
    }
}