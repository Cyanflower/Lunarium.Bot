# LunariumScheduler 改进版使用指南

## 🎯 主要改进

### 1. ✅ 修复了 Timer 内存泄漏
- 使用 `Task.Delay` 代替 `Timer`
- 每个任务有独立的调度循环
- 正确处理取消令牌

### 2. ✅ 异步任务追踪
- 追踪所有正在执行的任务
- `DisposeAsync` 时等待所有任务完成
- 防止任务被意外吞掉

### 3. ✅ 统一使用 DateTimeOffset
- `NextRun` 和 `LastRun` 都使用 `DateTimeOffset`
- 更清晰的时区处理

### 4. ✅ 便捷的扩展方法
- `ScheduleDaily` - 每天执行
- `ScheduleWeekly` - 每周执行
- `ScheduleMonthly` - 每月执行
- `ScheduleInterval` - 按间隔执行
- `ScheduleHourly` - 每小时执行

### 5. ✅ 任务执行历史
- 保留最近 10 次执行记录
- 记录执行时间、耗时、成功/失败
- 提供统计方法

---

## 📖 基础使用

### 1. 创建调度器

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lunarium.Bot.Scheduler;

// 创建服务容器
var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole())
    .AddScoped<DailyReportJob>()
    .AddScoped<WeeklyReviewJob>()
    .BuildServiceProvider();

// 创建调度器
var logger = services.GetRequiredService<ILogger<LunariumScheduler>>();
var scheduler = new LunariumScheduler(services, logger);
```

---

### 2. 使用便捷方法注册任务

```csharp
// 获取东京时区
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

// 每天凌晨 4 点执行报告任务
scheduler.ScheduleDailyAsync<DailyReportJob>(
    hour: 4,
    minute: 0,
    name: "每日报告",
    timeZone: tokyoTz
);

// 每周二上午 9 点执行审查任务
scheduler.ScheduleWeeklyAsync<WeeklyReviewJob>(
    dayOfWeek: DayOfWeek.Tuesday,
    hour: 9,
    minute: 0,
    name: "每周审查",
    timeZone: tokyoTz
);

// 每 5 分钟执行心跳检查
scheduler.ScheduleInterval<HeartbeatJob>(
    intervalMinutes: 5,
    name: "心跳检查"
);

// 每月 1 号凌晨 2 点执行月度汇总
scheduler.ScheduleMonthlyAsync<MonthlyReportJob>(
    day: 1,
    hour: 2,
    minute: 0,
    name: "月度汇总"
);

// 每小时的第 30 分钟执行数据清理
scheduler.ScheduleHourly<DataCleanupJob>(
    minute: 30,
    name: "数据清理"
);
```

---

### 3. 链式调用（Fluent API）

```csharp
var scheduler = new LunariumScheduler(services, logger)
    .ScheduleDaily<DailyReportJob>(hour: 4)
    .ScheduleWeekly<WeeklyReviewJob>(DayOfWeek.Tuesday, hour: 9)
    .ScheduleInterval<HeartbeatJob>(intervalMinutes: 5);

scheduler.Start();
```

---

### 4. 启动和停止

```csharp
// 启动调度器
scheduler.Start();

// 获取所有任务信息
Console.WriteLine(scheduler.GetAllJobsInfo());

// 停止调度器
scheduler.Stop();

// 异步释放资源（等待所有任务完成）
await scheduler.DisposeAsync();
```

---

## 📊 查询任务信息

### 1. 查询单个任务

```csharp
// 按类型查询
var info = scheduler.GetJobInfo<DailyReportJob>();
Console.WriteLine(info);

// 按名称查询
var info = scheduler.GetJobInfo("每日报告");
Console.WriteLine(info);
```

### 2. 查询所有任务

```csharp
var allInfo = scheduler.GetAllJobsInfo();
Console.WriteLine(allInfo);

/*
输出示例：
==================== 调度器信息 ====================
调度器状态: 运行中
已注册任务数: 3

-------------------- 任务 [1] --------------------
任务名称: 每日报告
任务类型: DailyReportJob
Cron 表达式: 0 4 * * *
任务模式: 异步
时区: Tokyo Standard Time (東京 (標準時))
下次执行 (UTC): 2025-11-05 19:00:00 +00:00
下次执行 (本地): 2025-11-06 04:00:00 +09:00
上次执行 (UTC): 2025-11-04 19:00:00 +00:00
上次执行 (本地): 2025-11-05 04:00:00 +09:00
运行状态: 已调度
是否正在执行: 否
执行历史记录数: 5
成功次数: 4
失败次数: 1
最近一次执行:
  时间: 2025-11-04 19:00:00 +00:00
  结果: 成功
  耗时: 1234.56ms
====================================================
*/
```

---

## 🔍 访问执行历史

```csharp
// 在任务内部访问调度器
public class MyModule
{
    private readonly LunariumScheduler _scheduler;
    
    public MyModule(IServiceProvider services)
    {
        _scheduler = new LunariumScheduler(services);
        _scheduler.ScheduleDaily<DailyJob>(hour: 4);
        _scheduler.Start();
    }
    
    public void CheckJobStatus()
    {
        // 获取任务信息
        var info = _scheduler.GetJobInfo<DailyJob>();
        Console.WriteLine(info);
        
        // 获取任务对象（需要访问内部列表）
        // 注意：这需要你在 Scheduler 中添加公开方法
    }
}
```

---

## 🎨 完整示例

### 定义任务

```csharp
using Lunarium.Bot.Scheduler.Interfaces;

// 异步任务示例
public class DailyReportJob : IAsyncJob
{
    private readonly ILogger<DailyReportJob> _logger;
    private readonly IDbContext _dbContext;
    
    public DailyReportJob(ILogger<DailyReportJob> logger, IDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("开始生成每日报告...");
        
        // 查询数据库
        var stats = await _dbContext.GetDailyStatsAsync();
        
        // 生成报告
        var report = GenerateReport(stats);
        
        // 发送邮件
        await SendEmailAsync(report);
        
        _logger.LogInformation("每日报告生成完成");
    }
}

// 同步任务示例
public class DataCleanupJob : IJob
{
    private readonly ILogger<DataCleanupJob> _logger;
    private readonly IDbContext _dbContext;
    
    public DataCleanupJob(ILogger<DataCleanupJob> logger, IDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }
    
    public void Execute()
    {
        _logger.LogInformation("开始清理过期数据...");
        
        // 删除 30 天前的临时数据
        var deletedCount = _dbContext.DeleteOldTempData(days: 30);
        
        _logger.LogInformation("数据清理完成，删除了 {Count} 条记录", deletedCount);
    }
}
```

---

### 在模块中使用

```csharp
using Microsoft.Extensions.Hosting;

public class MyBackgroundModule : IHostedService
{
    private readonly LunariumScheduler _scheduler;
    private readonly ILogger<MyBackgroundModule> _logger;
    
    public MyBackgroundModule(
        IServiceProvider services,
        ILogger<MyBackgroundModule> logger)
    {
        _logger = logger;
        
        // 创建调度器（注入当前模块的 DI 容器）
        _scheduler = new LunariumScheduler(services, logger);
        
        // 获取东京时区
        var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        
        // 注册任务
        _scheduler
            .ScheduleDailyAsync<DailyReportJob>(hour: 4, timeZone: tokyoTz)
            .ScheduleWeeklyAsync<WeeklyReviewJob>(DayOfWeek.Tuesday, hour: 9, timeZone: tokyoTz)
            .ScheduleInterval<HeartbeatJob>(intervalMinutes: 5);
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动调度器...");
        _scheduler.Start();
        
        // 打印任务信息
        _logger.LogInformation("\n{JobsInfo}", _scheduler.GetAllJobsInfo());
        
        return Task.CompletedTask;
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("停止调度器...");
        _scheduler.Stop();
        
        // 等待所有任务完成
        await _scheduler.DisposeAsync();
        
        _logger.LogInformation("调度器已停止");
    }
}
```

---

### 注册到主机

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 注册任务
        services.AddScoped<DailyReportJob>();
        services.AddScoped<WeeklyReviewJob>();
        services.AddScoped<HeartbeatJob>();
        
        // 注册模块
        services.AddHostedService<MyBackgroundModule>();
    })
    .Build();

await host.RunAsync();
```

---

## 🔧 高级用法

### 1. 使用原始 Cron 表达式

```csharp
// 复杂的调度规则仍然可以使用原始方法
scheduler.Schedule<ComplexJob>(
    cronExpression: "0 0,12 1 */2 *",  // 每隔一个月的 1 号，0 点和 12 点执行
    name: "复杂任务"
);
```

---

### 2. 动态添加任务

```csharp
public class DynamicSchedulerService
{
    private readonly LunariumScheduler _scheduler;
    
    public void AddTaskAtRuntime<TJob>(int hour) where TJob : IJob
    {
        if (_scheduler != null)
        {
            // 调度器运行时添加任务
            _scheduler.ScheduleDaily<TJob>(hour: hour);
            // 任务会立即被调度
        }
    }
}
```

---

### 3. 移除任务

```csharp
// 按类型移除
bool removed = scheduler.Remove<DailyReportJob>();

// 按名称移除
bool removed = scheduler.Remove("每日报告");
```

---

## ⚠️ 注意事项

### 1. 任务必须注册到 DI 容器

```csharp
services.AddScoped<DailyReportJob>();  // ✅ 正确
services.AddSingleton<DailyReportJob>(); // ⚠️ 可以，但注意生命周期
// 不注册会导致运行时错误 ❌
```

---

### 2. 时区处理

```csharp
// ✅ 推荐：明确指定时区
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
scheduler.ScheduleDaily<MyJob>(hour: 4, timeZone: tokyoTz);

// ⚠️ 不指定时区将使用 UTC
scheduler.ScheduleDaily<MyJob>(hour: 4);  // 这是 UTC 时间 4 点
```

---

### 3. 异步释放

```csharp
// ✅ 使用 DisposeAsync
await scheduler.DisposeAsync();

// ❌ 不要使用同步 Dispose（已移除）
// scheduler.Dispose();  // 不再支持
```

---

### 4. 任务重叠执行

如果任务执行时间超过调度间隔，会自动跳过：

```csharp
// 任务执行需要 10 分钟，但每 5 分钟调度一次
scheduler.ScheduleInterval<LongRunningJob>(intervalMinutes: 5);

// 输出日志：
// [12:00] 开始执行任务
// [12:05] 任务仍在执行中，跳过本次调度
// [12:10] 任务仍在执行中，跳过本次调度
// [12:11] 任务执行完成
// [12:15] 开始执行任务
```

---

## 🎉 总结

改进后的 LunariumScheduler：

✅ 修复了内存泄漏问题  
✅ 异步任务得到正确追踪  
✅ 时区处理更加清晰  
✅ 提供了便捷的扩展方法  
✅ 支持任务执行历史查询  

完美适配模块化架构，无需注册到主机，轻量级且高性能！