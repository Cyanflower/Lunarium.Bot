using System.Collections.Concurrent;
using LunariumBot.Net.Logger;
using NanoidDotNet;

namespace LunariumBot.Net.ReleaseBot.InteractionsCache;

public class InteractionsCacheManager : IDisposable
{
    const int MaxRetry = 5;
    const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    const int ExpireHours = 1; // 小时
    private readonly ILogger _logger;
    // private int retryCount = 0; 
    private readonly PeriodicTimer _timer; 
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _cleanupTask;
    private volatile bool _isDisposed = false;
    private volatile bool _isBroken = false;
    private readonly ConcurrentDictionary<string, IInteractionsCache> _paginatedViews = new();
    public InteractionsCacheManager(ILogger logger)
    {
        _logger = logger.ForContext("InteractionsCacheManager");
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _cleanupTask = Task.Run(async () => { await CleanupLoopAsync(); });
    }

    public IInteractionsCache RegisterView<T>(ulong userId, T data) where T : class
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(InteractionsCacheManager));
        if (_isBroken) throw new InvalidOperationException("InteractionsCacheManager is out of service due to repeated failures.");

        // 生成 ID
        var key = $"{userId}-{Nanoid.Generate(Alphabet, 10)}";
        var expiresAt = DateTimeOffset.UtcNow.AddHours(ExpireHours);

        var view = InteractionsCacheFactory.Create(data, expiresAt, key);

        _paginatedViews.TryAdd(key, view);
        return view;
    }
    
    // 泛型 Get
    // TView 必须是 IInteractionsCache 的实现类
    public TView? GetView<TView>(string key) where TView : class, IInteractionsCache
    {
        // 复用上面的逻辑, 确保"续命"被执行
        var view = GetView(key);
        
        // 尝试转换, 如果类型不匹配, 例如 ID 存在但对应的是 SearchCache 而不是 FavCache,这里会返回 null
        return view as TView;
    }

    public IInteractionsCache? GetView(string key)
    {
        if (_isDisposed || _isBroken) return null;

        if (_paginatedViews.TryGetValue(key, out var cache))
        {
            // 只要用户还在访问(交互)，就给它续命 1 小时 (续命? 续命! +1h.jpg)
            cache.RefreshExpiration(TimeSpan.FromHours(ExpireHours));
            return cache;
        }
        return null;
    }
    
    public void TryRemove(string key)
    {
        _paginatedViews.TryRemove(key, out _);
    }

    private async Task CleanupLoopAsync()
    {
        var retryCount = 0;
        var token = _cts.Token;

        try
        {
            while (await _timer.WaitForNextTickAsync(token))
            {
                try
                {
                    if (_paginatedViews.IsEmpty) continue;

                    var now = DateTimeOffset.UtcNow;
                    
                    // 核心清理逻辑
                    foreach (var pair in _paginatedViews)
                    {
                        // 再次检查取消, 避免在繁重的循环中卡死
                        if (token.IsCancellationRequested) return;

                        if (now >= pair.Value.ExpiresAt)
                        {
                            _paginatedViews.TryRemove(pair.Key, out _);
                        }
                    }

                    // 如果成功执行完一轮, 重置重试计数器
                    retryCount = 0; 
                }
                catch (Exception ex)
                {
                    // 这里捕获的是 业务逻辑/集合操作 的异常，而不是 Timer 的异常
                    // 如果是正在关闭引发的异常，直接退出
                    if (token.IsCancellationRequested) return;

                    retryCount++;
                    _logger.Error(ex, "CleanupLoop Error: {retryCount}/{MaxRetry}", retryCount, MaxRetry);

                    if (retryCount >= MaxRetry)
                    {
                        _isBroken = true;
                        // 清空缓存
                        _paginatedViews.Clear();
                        _logger.Critical("InteractionsCacheManager Circuit Broken: Too many errors.");
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常的取消流程
        }
        catch (Exception ex)
        {
            // 这里捕获的是 Timer 本身可能的异常
            if (!_isDisposed)
            {
                _logger.Critical(ex, "InteractionsCacheManager Critical Timer Failure.");
                _isBroken = true;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}