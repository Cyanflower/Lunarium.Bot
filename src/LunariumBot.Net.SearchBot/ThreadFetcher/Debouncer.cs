namespace LunariumBot.Net.SearchBot.ThreadFetcher;

/// <summary>
/// 提供防抖功能。在连续的调用中，只有当调用停止一段时间后，才会执行指定的操作。
/// </summary>
public class Debouncer
{
    private readonly Func<Task> _action;
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _lock = new object();

    /// <summary>
    /// 初始化一个新的 Debouncer 实例。
    /// </summary>
    /// <param name="action">防抖延迟后要异步执行的操作。</param>
    /// <param name="delay">防抖延迟时间。</param>
    public Debouncer(Func<Task> action, TimeSpan delay)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _delay = delay;
    }

    /// <summary>
    /// 触发防抖器。每次调用都会重置延迟计时器。
    /// </summary>
    public void Trigger()
    {
        lock (_lock)
        {
            // 如果已有计时器在跑，取消它
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // 创建一个新的计时器
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            Task.Delay(_delay, token).ContinueWith(async t =>
            {
                // 只有当任务没有被取消时才执行
                if (!t.IsCanceled)
                {
                    await _action();
                }
            }, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// 立即取消任何待处理的防抖操作。
    /// </summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}