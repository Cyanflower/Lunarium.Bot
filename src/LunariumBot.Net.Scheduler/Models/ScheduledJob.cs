using System;
using System.Threading;

using Cronos;

using LunariumBot.Net.Scheduler.Interfaces;

namespace LunariumBot.Net.Scheduler.Models;

/// <summary>
/// 已调度的任务信息
/// </summary>
public class ScheduledJob
{
    /// <summary>
    /// 任务类型
    /// </summary>
    public Type JobType { get; }
    /// <summary>
    /// Cron 表达式
    /// </summary>
    public CronExpression CronExpression { get; }
    /// <summary>
    /// 当前的定时器
    /// </summary>
    public Timer? CurrentTimer { get; set; }
    /// <summary>
    /// 是否为异步任务
    /// </summary>
    public bool IsAsync { get; }
    /// <summary>
    /// 任务名称
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// 下次执行时间
    /// </summary>
    public DateTime? NextRun { get; set; }
    /// <summary>
    /// 上次执行时间
    /// </summary>
    public DateTime? LastRun { get; set; }
    /// <summary>
    /// 任务 Cron 时区
    /// </summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// 创建一个已调度的任务
    /// </summary>
    /// <param name="jobType">任务类型，必须实现 IJob 或 IAsyncJob</param>
    /// <param name="cronExpression">Cron 表达式字符串</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">任务时区（可选），默认为 UTC</param>
    public ScheduledJob(Type jobType, string cronExpression, string? name = null, TimeZoneInfo? timeZone = null)
    {
        // 验证 jobType 不能为 null
        if (jobType == null)
        {
            throw new ArgumentNullException(nameof(jobType), "任务类型不能为 null");
        }
        
        // 验证任务类型必须实现 IJob 或 IAsyncJob
        bool isJob = typeof(IJob).IsAssignableFrom(jobType);
        bool isAsyncJob = typeof(IAsyncJob).IsAssignableFrom(jobType);

        if (!isJob && !isAsyncJob)
        {
            throw new ArgumentException(
                $"任务类型 {jobType.Name} 必须实现 IJob 或 IAsyncJob 接口",
                nameof(jobType)
            );
        }

        // 验证并解析 Cron 表达式
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new ArgumentException("Cron 表达式不能为空", nameof(cronExpression));
        }

        CronExpression parsedCron;
        try
        {
            parsedCron = CronExpression.Parse(cronExpression, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException(
                $"无效的 Cron 表达式: {cronExpression}",
                nameof(cronExpression),
                ex
            );
        }

        // 赋值属性
        JobType = jobType;
        CronExpression = parsedCron;
        IsAsync = isAsyncJob;                       // 如果是 IAsyncJob 则为 true
        Name = name ?? jobType.Name;                // 如果没提供名称，用类名
        TimeZone = timeZone ?? TimeZoneInfo.Utc;    // 如果没提供时区，默认为 UTC
    }
}