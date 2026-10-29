using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.RateLimiting;

namespace Lunarium.Bot.Framework.RateLimiting;

public class RateLimitedExecutor : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly int _concurrency;
    private readonly Task _loopTask;
    private readonly Channel<Work> _channel;
    private readonly TokenBucketRateLimiter _tokenBucket;

    /// <summary>
    /// 已入队/在飞、尚未向调用方交付结果的 TCS。
    /// <para>
    /// 正常执行时由 <see cref="ExecuteLoopAsync"/> 在 finally 中移除；
    /// Dispose 超时或循环提前结束时用它统一取消，避免调用方永久挂起。
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<TaskCompletionSource, byte> _outstanding = new();
    private readonly Action<Exception>? _onError;
    private int _disposed;

    public RateLimitedExecutor(int concurrency, int channelSize, TokenBucketRateLimiter tokenBucketRateLimiter, Action<Exception>? onError = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelSize);
        ArgumentNullException.ThrowIfNull(tokenBucketRateLimiter);
        _channel = Channel.CreateBounded<Work>(new BoundedChannelOptions(channelSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _tokenBucket = tokenBucketRateLimiter;
        _concurrency = concurrency;
        _onError = onError;
        _loopTask = Task.Run(() => ExecuteLoopAsync(_cts.Token));

        // 循环异常（非取消）通过 onError 暴露，不依赖调用方是否 Dispose
        _ = _loopTask.ContinueWith(
            t => OnError(t.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // 每秒完全均匀。
    // Discord 全局上限是 50/s；默认 40 留 20% 余量，给状态消息、重试和 timer 抖动。
    public static RateLimitedExecutor CreateSmoothPerRequest(int concurrency = 1, double qps = 40.0, int channelSize = 1000, Action<Exception>? onError = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelSize);

        var tokenBucket = RateLimiterConfigure(batchSize: 1, Math.Max(1.0, 1000.0 / qps), concurrency * 2);
        return new(concurrency, channelSize, tokenBucket, onError);
    }

    // 每秒完全均匀(批)
    public static RateLimitedExecutor CreateBatchedUniformQps(int concurrency = 1, int batchSize = 1, double totalQps = 40.0, int channelSize = 1000, Action<Exception>? onError = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalQps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelSize);

        var tokenBucket = RateLimiterConfigure(batchSize: batchSize, Math.Max(1.0, 1000.0 * batchSize / totalQps), concurrency * 2);
        return new(concurrency, channelSize, tokenBucket, onError);
    }

    // 每秒批之间完全均匀(批)
    public static RateLimitedExecutor CreateUniformBatchFrequency(int concurrency = 1, int batchSize = 1, double batchesPerSec = 40.0, int channelSize = 1000, Action<Exception>? onError = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchesPerSec);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelSize);

        var tokenBucket = RateLimiterConfigure(batchSize: batchSize, Math.Max(1.0, 1000 / batchesPerSec), concurrency * 2);
        return new(concurrency, channelSize, tokenBucket, onError);
    }
    
    private Task ExecuteLoopAsync(CancellationToken ct)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _concurrency,
            CancellationToken = ct,
        };

        return Parallel.ForEachAsync(_channel.Reader.ReadAllAsync(ct), options, async (work, token) =>
        {
            try
            {
                using var lease = await _tokenBucket.AcquireAsync(1, token);

                if (!lease.IsAcquired)
                {
                    var rejected = new RateLimitRejectedException("令牌桶排队队列已满，请求被拒绝。");
                    work.Tcs.TrySetException(rejected);
                    OnError(rejected);
                    return;
                }

                await ExecuteAsync(work);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                work.Tcs.TrySetCanceled(token);
            }
            catch (Exception ex)
            {
                work.Tcs.TrySetException(ex);
            }
            finally
            {
                _outstanding.TryRemove(work.Tcs, out _);
            }
        });
    }

    public async Task WriteAndWaitAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        var item = new Work(work, ct, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        await EnqueueAsync(item, ct);
        await item.Tcs.Task;
    }

    /// <summary>
    /// 提交一个有返回值的工作并等待结果。
    /// <para>
    /// 先等基类 <see cref="Work.Tcs"/>（标记执行结束/取消/拒绝），再取 <see cref="Work{T}.Result"/>；
    /// 这样“还没执行就被取消/拒绝”时不会去等一个永远不会完成的返回值 TCS。
    /// </para>
    /// </summary>
    public async Task<T> WriteAndWaitAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);

        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new Work<T>(work, ct, result);

        await EnqueueAsync(item, ct);
        await item.Tcs.Task;
        return await result.Task;
    }

    private async ValueTask EnqueueAsync(Work item, CancellationToken ct)
    {
        // 先登记再入队：Dispose 时才能把所有还没交付结果的调用方一并发完/取消，
        // 包括“已入队但没被执行”“被 Parallel.ForEachAsync 取出但因取消没执行”的情况。
        _outstanding.TryAdd(item.Tcs, 0);

        try
        {
            await _channel.Writer.WriteAsync(item, ct);
        }
        catch
        {
            _outstanding.TryRemove(item.Tcs, out _);
            throw;
        }
    }

    private async Task ExecuteAsync(Work work)
    {
        try
        {
            if (work.Ct.IsCancellationRequested)
            {
                work.Tcs.TrySetCanceled(work.Ct);
                return;
            }

            // 链接调用方 token 与 executor 生命周期：Dispose 超时时能取消在飞任务
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(work.Ct, _cts.Token);

            await work.Func(linked.Token);
            work.Tcs.TrySetResult();
        }
        catch (OperationCanceledException) when (work.Ct.IsCancellationRequested)
        {
            work.Tcs.TrySetCanceled(work.Ct);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            work.Tcs.TrySetCanceled(_cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            // 不是调用方/生命周期取消：按异常回传，别伪装成 canceled
            work.Tcs.TrySetException(ex);
        }
        catch (Exception ex)
        {
            work.Tcs.TrySetException(ex);
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync(TimeSpan.FromSeconds(30));

    /// <summary>
    /// 停止接收新任务，先尝试在 <paramref name="drainTimeout"/> 内排空；
    /// 超时则取消在飞任务并清理未执行任务，避免调用方永久挂起。
    /// </summary>
    /// <param name="drainTimeout">等待排空的最长时间，必须大于等于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="drainTimeout"/> 为负数。</exception>
    public async ValueTask DisposeAsync(TimeSpan drainTimeout)
    {
        // 已经 Dispose 过：直接返回（即使这次传了非法参数，也遵循幂等语义）
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (drainTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drainTimeout), drainTimeout, "drainTimeout 不能为负数。");
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();

        try
        {
            try
            {
                await _loopTask.WaitAsync(drainTimeout);
            }
            catch (TimeoutException)
            {
                var pending = _outstanding.Count;

                // 排队/在飞任务在超时内没跑完：取消生命周期 token，停止取新任务
                _cts.Cancel();
                CancelOutstanding();

                if (pending > 0 && _onError is not null)
                {
                    OnError(new TimeoutException(
                        $"RateLimitedExecutor 在 {drainTimeout} 内未排空，已取消 {pending} 个未完成 Work。"));
                }

                // 给响应取消的任务一点收尾时间
                try
                {
                    await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // 循环已结束或已被取消：仍未交付结果的 Work 一律取消
            CancelOutstanding();

            if (_loopTask.IsCompleted)
            {
                _tokenBucket.Dispose();
                _cts.Dispose();
            }
            else
            {
                // 有 Work 不响应取消，循环还没退出：等它真正结束后再释放，
                // 避免在飞任务访问已释放的 token bucket / CTS。
                _ = _loopTask.ContinueWith(
                    _ =>
                    {
                        CancelOutstanding();
                        _tokenBucket.Dispose();
                        _cts.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// 取消所有尚未向调用方交付结果的 Work。
    /// <para>
    /// 覆盖三种情况：还在 Channel 里排队的、被 <see cref="Parallel.ForEachAsync{TSource}"/>
    /// 取出但因取消没有执行的、以及调用方还在等 <see cref="Work{T}.Result"/> 的。
    /// </para>
    /// </summary>
    private void CancelOutstanding()
    {
        foreach (var tcs in _outstanding.Keys)
        {
            tcs.TrySetCanceled();
        }

        _outstanding.Clear();
    }

    /// <summary>
    /// 安全地调用异常回调：回调自身抛异常时静默吞掉，避免击穿调度循环。
    /// <para>
    /// 刻意使用 <see cref="Action{T}"/> 而不是 <c>ILogger</c>，保持本类型不依赖日志库，
    /// 便于日后整体搬到 <c>Lunarium.Common</c>。
    /// </para>
    /// </summary>
    private void OnError(Exception ex)
    {
        try
        {
            _onError?.Invoke(ex);
        }
        catch (Exception callbackEx)
        {
            System.Diagnostics.Debug.WriteLine($"[RateLimitedExecutor] onError 回调异常: {callbackEx}");
        }
    }
    
    private static TokenBucketRateLimiter RateLimiterConfigure(int batchSize, double recoveryInterval, int queueLimit)
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            // 每个恢复周期补充 batchSize 个 token（恢复间隔由调用方按 qps 计算）
            TokensPerPeriod = batchSize,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(recoveryInterval),

            // Token 上限
            TokenLimit = batchSize,

            // 队列设置
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,

            // 自动补充: 开启
            AutoReplenishment = true
        });
    }
}
