using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Archive.Notifier;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Logging;

namespace Lunarium.Bot.Archive.Archiver;

/// <summary>
/// 归档调度器：管理 NotifierManager 生命周期，并按 IntervalMin 触发 Archiver。
/// <para>
/// 启动后先延迟 5 秒跑第一轮（让 Ready 的其他任务先完成），之后按 PeriodicTimer 周期执行。
/// 定时与手动归档共用一把锁，避免重叠。
/// </para>
/// </summary>
public sealed class ArchiverManager : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly Archiver _archiver;
    private readonly NotifierManager _notifier;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Task? _archiveLoop;
    private bool _started;

    public ArchiverManager(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        Archiver archiver,
        NotifierManager notifier)
    {
        _logger = logger.ForContext("ArchiverManager");
        _configLoader = configLoader;
        _archiver = archiver;
        _notifier = notifier;
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(_configLoader.Value.IntervalMin));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await _notifier.StartAsync(cancellationToken);

        _archiveLoop = Task.Run(() => ArchiveLoopAsync(_cts.Token), CancellationToken.None);
        _started = true;
        _logger.Info("归档调度器已启动，间隔 {Interval} 分钟", _configLoader.Value.IntervalMin);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _cts.Cancel();

        if (_archiveLoop is not null)
        {
            try
            {
                await _archiveLoop.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }
            catch (Exception ex)
            {
                // 放弃等待并不影响关闭：剩余工作会随进程退出被终止，不需要为此阻塞 shutdown。
                _logger.Warning(ex, "等待归档循环退出失败，不再等待（剩余工作随进程退出终止）");
            }
        }

        await _notifier.StopAsync(cancellationToken);
        _logger.Info("归档调度器已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _timer.Dispose();
        _cts.Dispose();
        // 不 Dispose _lock：SemaphoreSlim 不持有 OS 句柄，Dispose 反而会在
        // 循环超出等待窗口仍在运行时抛 ObjectDisposedException。
    }

    public void UpdateInterval(int intervalMin)
    {
        _timer.Period = TimeSpan.FromMinutes(intervalMin);
        _logger.Info("归档间隔已更新为 {Interval} 分钟", intervalMin);
    }

    /// <summary>手动触发一次归档；正在执行时返回 false。</summary>
    public async Task<bool> ManualArchive(CancellationToken cancellationToken)
    {
        if (!await _lock.WaitAsync(0, cancellationToken))
        {
            _logger.Warning("归档任务正在执行中，跳过本次手动请求");
            return false;
        }

        try
        {
            await _archiver.Archive(cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ArchiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 给 Ready 之后的其他初始化一点时间，同时避免状态报告/缓存等 10 分钟后才出现
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await _lock.WaitAsync(0, cancellationToken))
                {
                    _logger.Warning("归档任务正在执行中，本次定时触发已跳过");
                }
                else
                {
                    try
                    {
                        await _archiver.Archive(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "后台定时归档任务执行失败");
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }

                await _timer.WaitForNextTickAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("归档循环因服务关闭而中断");
        }
        catch (Exception ex)
        {
            _logger.Critical(ex, "归档循环发生严重崩溃");
            throw;
        }
    }
}
