using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LunariumBot.Net.Scheduler.Models;
using LunariumBot.Net.Scheduler.Interfaces;

namespace LunariumBot.Net.Scheduler;

public class LunariumScheduler : IDisposable 
{
    private readonly List<ScheduledJob> _jobs = new();
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private bool _isRunning = false;
    private readonly object _lock = new();


    #region 构造函数

    /// <summary>
    /// 创建调度器实例
    /// </summary>
    /// <param name="serviceProvider">服务提供者(DI容器)</param>
    /// <param name="logger">日志记录器（可选）。如果为 null，将使用空日志记录器。</param>
    public LunariumScheduler(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        // 验证参数
        if (serviceProvider == null)
        {
            throw new ArgumentNullException(
                nameof(serviceProvider),
                "服务提供者(DI容器)不能为 null"
            );
        }

        // 初始化字段
        _serviceProvider = serviceProvider;
        _jobs = new List<ScheduledJob>();
        _lock = new object();
        _isRunning = false;

        // 如果没有提供日志记录器，使用 NullLogger
        _logger = logger ?? NullLogger.Instance;
    }

    #endregion
    
    #region 公共 API - 注册与移除任务
    
    /// <summary>
    /// 注册一个同步定时任务
    /// </summary>
    /// <typeparam name="TJob">任务类型，必须实现 IJob</typeparam>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="name">任务名称(可选)</param>
    public void Schedule<TJob>(string cronExpression, string? name = null, TimeZoneInfo? timeZone = null) 
        where TJob : IJob
    {
        // 创建任务信息对象
        var scheduledJob = new ScheduledJob(
            typeof(TJob),
            cronExpression,
            name,
            timeZone
        );

        // 线程安全地添加到列表
        lock (_lock)
        {
            _jobs.Add(scheduledJob);
        }
        
        _logger.LogInformation(
            "已注册任务: {JobName} (类型: {JobType}, Cron: {CronExpression}, 时区: {TimeZoneId})",
            scheduledJob.Name,
            typeof(TJob).Name,
            cronExpression,
            scheduledJob.TimeZone.Id
        );
        
        // 如果调度器已经启动，立即为新任务创建调度
        if (_isRunning)
        {
            ScheduleNextRun(scheduledJob);
            _logger.LogDebug(
                "调度器正在运行，立即为任务 {JobName} 创建调度",
                scheduledJob.Name
            );
        }
    }

    /// <summary>
    /// 注册一个异步定时任务
    /// </summary>
    /// <typeparam name="TJob">任务类型，必须实现 IAsyncJob</typeparam>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="name">任务名称(可选)</param>
    public void ScheduleAsync<TJob>(string cronExpression, string? name = null, TimeZoneInfo? timeZone = null) 
        where TJob : IAsyncJob
    {
        // 创建任务信息对象
        var scheduledJob = new ScheduledJob(
            typeof(TJob),
            cronExpression,
            name,
            timeZone
        );

        // 线程安全地添加到列表
        lock (_lock)
        {
            _jobs.Add(scheduledJob);
        }
        
        _logger.LogInformation(
            "已注册异步任务: {JobName} (类型: {JobType}, Cron: {CronExpression}, 时区: {TimeZoneId})",
            scheduledJob.Name,
            typeof(TJob).Name,
            cronExpression,
            scheduledJob.TimeZone.Id
        );
        
        // 如果调度器已经启动，立即为新任务创建调度
        if (_isRunning)
        {
            ScheduleNextRun(scheduledJob);
        }
    }

    /// <summary>
    /// 移除指定类型的定时任务
    /// </summary>
    /// <typeparam name="TJob">要移除的任务类型</typeparam>
    /// <returns>如果找到并移除返回 true，否则返回 false</returns>
    public bool Remove<TJob>() where TJob : IJob
    {
        lock (_lock)
        {
            // 查找任务
            var jobToRemove = _jobs.FirstOrDefault(j => j.JobType == typeof(TJob));
            
            if (jobToRemove == null)
            {
                _logger.LogWarning(
                    "尝试移除不存在的任务: {JobType}",
                    typeof(TJob).Name
                );
                return false;
            }
            
            // 释放 Timer
            jobToRemove.CurrentTimer?.Dispose();
            jobToRemove.CurrentTimer = null;

            // 从列表移除
            _jobs.Remove(jobToRemove);
            
            _logger.LogInformation(
                "已移除任务: {JobName} (类型: {JobType})",
                jobToRemove.Name,
                typeof(TJob).Name
            );
            
            return true;
        }
    }

    /// <summary>
    /// 移除指定名称的定时任务
    /// </summary>
    /// <param name="name">任务名称</param>
    /// <returns>如果找到并移除返回 true，否则返回 false</returns>
    public bool Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (_lock)
        {
            var jobToRemove = _jobs.FirstOrDefault(j => j.Name == name);

            if (jobToRemove == null)
            {
                _logger.LogWarning(
                    "尝试移除不存在的任务: {JobType}",
                    name
                );
                return false;
            }

            jobToRemove.CurrentTimer?.Dispose();
            jobToRemove.CurrentTimer = null;

            _jobs.Remove(jobToRemove);

            _logger.LogInformation(
                "已移除任务: {JobName} (类型: {JobType})",
                jobToRemove.Name,
                name
            );

            return true;
        }
    }

    #endregion
    
    #region 公共 API - 调度器的生命周期控制

    /// <summary>
    /// 启动调度器
    /// </summary>
    public void Start()
    {
        // 线程安全地检查和设置状态
        lock (_lock)
        {
            // 如果已经在运行，直接返回
            if (_isRunning)
            {
                _logger.LogWarning("调度器已经在运行，忽略重复启动");
                return;
            }

            // 标记为运行状态
            _isRunning = true;
            _logger.LogInformation("调度器启动，共有 {JobCount} 个任务", _jobs.Count);
        }

        // 为所有已注册的任务创建调度
        lock (_lock)
        {
            foreach (var job in _jobs)
            {
                ScheduleNextRun(job);
                _logger.LogInformation(
                    "为任务 {JobName} 创建调度，下次执行: {NextRun:yyyy-MM-dd HH:mm:ss}",
                    job.Name,
                    job.NextRun
                );
            }
        }
        
        _logger.LogInformation("调度器启动完成");
    }

    /// <summary>
    /// 停止调度器
    /// </summary>
    public void Stop()
    {
        // 线程安全地检查和设置状态
        lock (_lock)
        {
            // 如果没有运行，直接返回
            if (!_isRunning)
            {
                _logger.LogWarning("调度器未运行，忽略停止操作");
                return;
            }

            // 标记为停止状态
            _isRunning = false;
            _logger.LogInformation("正在停止调度器...");
        }

        int stoppedCount = 0;
        // 停止所有任务的调度
        lock (_lock)
        {
            foreach (var job in _jobs)
            {
                // 释放 Timer
                job.CurrentTimer?.Dispose();
                // 清空引用
                job.CurrentTimer = null;

                stoppedCount++;
            }
        }
        _logger.LogInformation("调度器已停止，共停止了 {Count} 个任务", stoppedCount);
    }
    
    #endregion

    #region 公共 API - 任务信息查询

    /// <summary>
    /// 获取指定任务的信息
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <returns>任务信息字符串，如果任务不存在返回 null</returns>
    public string? GetJobInfo<TJob>() where TJob : IJob
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobType == typeof(TJob));
            
            if (job == null)
            {
                return null;
            }
            
            return FormatJobInfo(job);
        }
    }

    /// <summary>
    /// 获取指定名称任务的信息
    /// </summary>
    /// <param name="name">任务名称</param>
    /// <returns>任务信息字符串，如果任务不存在返回 null</returns>
    public string? GetJobInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Name == name);
            
            if (job == null)
            {
                return null;
            }
            
            return FormatJobInfo(job);
        }
    }

    /// <summary>
    /// 获取所有任务的信息
    /// </summary>
    /// <returns>所有任务的信息字符串</returns>
    public string GetAllJobsInfo()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();

            // 调度器状态
            sb.AppendLine("==================== 调度器信息 ====================");
            sb.AppendLine($"调度器状态: {(_isRunning ? "运行中" : "已停止")}");
            sb.AppendLine($"已注册任务数: {_jobs.Count}");
            sb.AppendLine();

            // 如果没有任务
            if (_jobs.Count == 0)
            {
                sb.AppendLine("尚未注册任何任务");
                sb.AppendLine("====================================================");
                return sb.ToString();
            }

            // 遍历所有任务
            for (int i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];

                sb.AppendLine($"-------------------- 任务 [{i + 1}] --------------------");
                sb.Append(FormatJobInfo(job));
                sb.AppendLine();
            }

            sb.AppendLine("====================================================");

            return sb.ToString();
        }
    }

    #endregion

    #region IDisposable 实现

    /// <summary>
    /// 释放调度器资源
    /// </summary>
    public void Dispose()
    {
        // 停止调度
        Stop();

        // 清空任务列表
        lock (_lock)
        {
            _jobs.Clear();
        }
    }

    #endregion

    #region 私有方法 - 核心调度与执行逻辑

    /// <summary>
    /// Timer 触发时的回调方法
    /// </summary>
    /// <param name="scheduledJob">要执行的任务</param>
    private void OnTimerElapsed(ScheduledJob scheduledJob)
    {
        // 根据任务类型选择执行方式
        if (scheduledJob.IsAsync)
        {
            // 异步任务：不等待完成，让任务在后台执行
            _ = ExecuteJobAsync(scheduledJob);
        }
        else
        {
            // 同步任务：直接执行
            ExecuteJob(scheduledJob);
        }

        // 调度下次执行(递归调用)
        // 重新调度前，必须检查调度器是否仍在运行
        lock (_lock)
        {
            // 如果在任务执行期间，调度器被停止了，则不再安排下一次
            if (!_isRunning)
            {
                return;
            }

            // 只有仍在运行时，才调度下一次
            ScheduleNextRun(scheduledJob);
        }
    }

    /// <summary>
    /// 执行同步任务
    /// </summary>
    /// <param name="scheduledJob">要执行的任务</param>
    private void ExecuteJob(ScheduledJob scheduledJob)
    {
        _logger.LogDebug(
            "开始执行任务: {JobName} (类型: {JobType})",
            scheduledJob.Name,
            scheduledJob.JobType.Name
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // 创建作用域(临时的服务容器)
            using var scope = _serviceProvider.CreateScope();
            
            // 从 DI 容器获取任务实例
            var jobInstance = scope.ServiceProvider.GetRequiredService(scheduledJob.JobType);
            
            // 转换为 IJob 接口类型
            var job = (IJob)jobInstance;
            
            // 执行任务
            job.Execute();

            // 更新最后执行时间
            scheduledJob.LastRun = DateTime.UtcNow;  
            
            scheduledJob.LastRun = DateTime.UtcNow;      
            _logger.LogInformation(
                "任务执行成功: {JobName}，耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
        );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // 捕获所有异常，记录但不中断调度
            _logger.LogError(
                ex,
                "任务执行失败: {JobName}，耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
    }

    /// <summary>
    /// 执行异步任务
    /// </summary>
    /// <param name="scheduledJob">要执行的任务</param>
    private async Task ExecuteJobAsync(ScheduledJob scheduledJob)
    {
        _logger.LogDebug(
            "开始执行异步任务: {JobName} (类型: {JobType})",
            scheduledJob.Name,
            scheduledJob.JobType.Name
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 创建作用域
            using var scope = _serviceProvider.CreateScope();

            // 从 DI 容器获取任务实例
            var jobInstance = scope.ServiceProvider.GetRequiredService(scheduledJob.JobType);

            // 转换为 IAsyncJob 接口类型
            var job = (IAsyncJob)jobInstance;

            // 异步执行任务
            await job.ExecuteAsync();

            // 更新最后执行时间
            scheduledJob.LastRun = DateTime.UtcNow;

            stopwatch.Stop();
            _logger.LogInformation(
                "异步任务执行成功: {JobName}，耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // 捕获所有异常，记录但不中断调度
            _logger.LogError(
                ex,
                "异步任务执行失败: {JobName}，耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
    }

    /// <summary>
    /// 为任务调度下次执行
    /// </summary>
    /// <param name="scheduledJob">要调度的任务</param>
    private void ScheduleNextRun(ScheduledJob scheduledJob)
    {
        // 计算下次执行时间
        var now = DateTime.UtcNow;
        var nextOccurrence = scheduledJob.CronExpression.GetNextOccurrence(DateTimeOffset.UtcNow,scheduledJob.TimeZone);

        // 如果没有下次执行时间，退出
        if (nextOccurrence == null)
        {
            _logger.LogWarning(
                "任务 {JobName} 在时区 {TimeZoneId} 没有下次执行时间，可能已到期",
                scheduledJob.Name,
                scheduledJob.TimeZone.Id
            );
            return;
        }

        // 更新任务的下次执行时间
        scheduledJob.NextRun = nextOccurrence.Value.UtcDateTime;

        // 计算延迟时间
        var delay = nextOccurrence.Value - DateTimeOffset.UtcNow;

        // 防止负延迟(如果计算时间超过了触发点)
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
            _logger.LogWarning(
            "任务 {JobName} 的下次执行时间已过期，将立即执行",
            scheduledJob.Name
            );
        }

        // 释放旧的 Timer(如果存在)
        scheduledJob.CurrentTimer?.Dispose();

        // 创建新的 Timer
        scheduledJob.CurrentTimer = new Timer(
            callback: _ => OnTimerElapsed(scheduledJob),
            state: null,
            dueTime: delay,
            period: Timeout.InfiniteTimeSpan
        );

        _logger.LogInformation(
            "任务 {JobName} 下次执行时间: {NextRun} (UTC: {NextRunUtc:yyyy-MM-dd HH:mm:ss})",
            scheduledJob.Name,
            nextOccurrence.Value,
            scheduledJob.NextRun 
        );
    }

    #endregion

    #region 私有方法 - 辅助工具

    /// <summary>
    /// 格式化单个任务的信息
    /// </summary>
    /// <param name="job">任务对象</param>
    /// <returns>格式化的信息字符串</returns>
    private string FormatJobInfo(ScheduledJob job)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"任务名称: {job.Name}");
        sb.AppendLine($"任务类型: {job.JobType.Name}");
        sb.AppendLine($"Cron 表达式: {job.CronExpression}");
        sb.AppendLine($"任务模式: {(job.IsAsync ? "异步" : "同步")}");
        sb.AppendLine($"时区: {job.TimeZone.Id}");

        if (job.NextRun.HasValue)
        {
            var localNextRun = TimeZoneInfo.ConvertTimeFromUtc(job.NextRun.Value, job.TimeZone);
            sb.AppendLine($"下次执行 (UTC): {job.NextRun.Value:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"下次执行 (本地): {localNextRun:yyyy-MM-dd HH:mm:ss} ({job.TimeZone.StandardName})");
        }
        else
        {
            sb.AppendLine("下次执行: 未调度");
        }

        if (job.LastRun.HasValue)
        {
            var localLastRun = TimeZoneInfo.ConvertTimeFromUtc(job.LastRun.Value, job.TimeZone);
            sb.AppendLine($"上次执行 (UTC): {job.LastRun.Value:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"上次执行 (本地): {localLastRun:yyyy-MM-dd HH:mm:ss} ({job.TimeZone.StandardName})");
        }
        else
        {
            sb.AppendLine("上次执行: 从未执行");
        }

        sb.AppendLine($"运行状态: {(job.CurrentTimer != null ? "已调度" : "未调度")}");

        return sb.ToString();
    }
    
    #endregion
}
