# 🔄 迁移指南：从旧版本升级到改进版

## 📋 重大变更清单

### 1. ✅ IDisposable → IAsyncDisposable

**旧版本：**
```csharp
public class LunariumScheduler : IDisposable
{
    public void Dispose()
    {
        Stop();
        // ...
    }
}
```

**新版本：**
```csharp
public class LunariumScheduler : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Stop();
        // 等待所有任务完成
        await Task.WhenAll(/* ... */);
    }
}
```

**迁移步骤：**
```csharp
// ❌ 旧代码
scheduler.Dispose();

// ✅ 新代码
await scheduler.DisposeAsync();
```

---

### 2. ✅ DateTime → DateTimeOffset

**旧版本：**
```csharp
public class ScheduledJob
{
    public DateTime? NextRun { get; set; }
    public DateTime? LastRun { get; set; }
}
```

**新版本：**
```csharp
public class ScheduledJob
{
    public DateTimeOffset? NextRun { get; set; }
    public DateTimeOffset? LastRun { get; set; }
}
```

**影响：**
- 如果你直接访问 `ScheduledJob` 对象（不太可能），需要修改类型
- `GetJobInfo()` 方法的输出格式略有变化（现在显示时区偏移）

---

### 3. ✅ Timer → Task.Delay

**旧版本：**
```csharp
public class ScheduledJob
{
    public Timer? CurrentTimer { get; set; }
}
```

**新版本：**
```csharp
public class ScheduledJob
{
    internal CancellationTokenSource? LoopCancellationTokenSource { get; set; }
    internal Task? LoopTask { get; set; }
}
```

**影响：**
- `CurrentTimer` 属性已移除
- 如果你在代码中检查 `CurrentTimer`，需要改用 `LoopTask`
- 大多数情况下不需要修改（内部实现）

---

### 4. ✅ 新增：任务执行历史

**新版本新增：**
```csharp
public class ScheduledJob
{
    public IReadOnlyCollection<JobExecutionHistory> GetHistory();
    public JobExecutionHistory? GetLastHistory();
    public int GetSuccessCount();
    public int GetFailureCount();
}

public class JobExecutionHistory
{
    public DateTimeOffset ExecutedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}
```

**使用方式：**
这是新增功能，不影响现有代码。如需使用，请参考文档。

---

### 5. ✅ 新增：便捷扩展方法

**旧版本：**
```csharp
scheduler.Schedule<DailyJob>("0 4 * * *");
```

**新版本（兼容旧语法，并新增便捷方法）：**
```csharp
// 旧语法仍然有效
scheduler.Schedule<DailyJob>("0 4 * * *");

// 新增便捷方法
scheduler.ScheduleDaily<DailyJob>(hour: 4);
scheduler.ScheduleWeekly<WeeklyJob>(DayOfWeek.Tuesday, hour: 9);
scheduler.ScheduleInterval<HeartbeatJob>(intervalMinutes: 5);
```

---

## 🚀 快速迁移步骤

### 步骤 1：更新引用

```csharp
// 添加新的扩展方法
using Lunarium.Bot.Scheduler;  // 新增：扩展方法命名空间
```

---

### 步骤 2：修改 Dispose 调用

**在 IHostedService 中：**

```csharp
// ❌ 旧代码
public class MyModule : IHostedService
{
    private readonly LunariumScheduler _scheduler;
    
    public Task StopAsync(CancellationToken ct)
    {
        _scheduler.Stop();
        _scheduler.Dispose();  // ❌ 旧代码
        return Task.CompletedTask;
    }
}

// ✅ 新代码
public class MyModule : IHostedService
{
    private readonly LunariumScheduler _scheduler;
    
    public async Task StopAsync(CancellationToken ct)
    {
        _scheduler.Stop();
        await _scheduler.DisposeAsync();  // ✅ 异步释放
    }
}
```

---

**在 using 语句中：**

```csharp
// ❌ 旧代码
using (var scheduler = new LunariumScheduler(services))
{
    scheduler.Start();
    // ...
}

// ✅ 新代码
await using (var scheduler = new LunariumScheduler(services))
{
    scheduler.Start();
    // ...
}
```

---

### 步骤 3：（可选）使用便捷方法

```csharp
// ❌ 旧代码（仍然有效）
scheduler.Schedule<DailyJob>("0 4 * * *");
scheduler.ScheduleAsync<WeeklyJob>("0 9 * * 2");

// ✅ 新代码（更易读）
scheduler.ScheduleDaily<DailyJob>(hour: 4);
scheduler.ScheduleWeeklyAsync<WeeklyJob>(DayOfWeek.Tuesday, hour: 9);
```

---

## 📊 完整示例对比

### 旧版本代码

```csharp
public class MyModule : IHostedService
{
    private readonly LunariumScheduler _scheduler;
    
    public MyModule(IServiceProvider services, ILogger<MyModule> logger)
    {
        _scheduler = new LunariumScheduler(services, logger);
        
        // 注册任务
        _scheduler.Schedule<DailyJob>("0 4 * * *", "每日任务");
        _scheduler.ScheduleAsync<WeeklyJob>("0 9 * * 2", "每周任务");
    }
    
    public Task StartAsync(CancellationToken ct)
    {
        _scheduler.Start();
        return Task.CompletedTask;
    }
    
    public Task StopAsync(CancellationToken ct)
    {
        _scheduler.Stop();
        _scheduler.Dispose();  // ❌ 同步释放
        return Task.CompletedTask;
    }
}
```

---

### 新版本代码

```csharp
public class MyModule : IHostedService
{
    private readonly LunariumScheduler _scheduler;
    
    public MyModule(IServiceProvider services, ILogger<MyModule> logger)
    {
        _scheduler = new LunariumScheduler(services, logger);
        
        // 使用便捷方法注册任务
        _scheduler
            .ScheduleDaily<DailyJob>(hour: 4, name: "每日任务")
            .ScheduleWeeklyAsync<WeeklyJob>(DayOfWeek.Tuesday, hour: 9, name: "每周任务");
    }
    
    public Task StartAsync(CancellationToken ct)
    {
        _scheduler.Start();
        return Task.CompletedTask;
    }
    
    public async Task StopAsync(CancellationToken ct)
    {
        _scheduler.Stop();
        await _scheduler.DisposeAsync();  // ✅ 异步释放
    }
}
```

---

## ⚠️ 常见问题

### Q1: 必须改用 DisposeAsync 吗？

**A:** 是的。旧的 `Dispose()` 方法已被移除，必须使用 `await DisposeAsync()`。这确保了所有任务都能正确完成。

---

### Q2: 如果我不想用便捷方法呢？

**A:** 完全没问题！旧的 `Schedule()` 和 `ScheduleAsync()` 方法仍然可用，便捷方法只是为了更好的可读性。

```csharp
// 这两种写法都有效
scheduler.Schedule<MyJob>("0 4 * * *");
scheduler.ScheduleDaily<MyJob>(hour: 4);
```

---

### Q3: DateTimeOffset 会破坏我的代码吗？

**A:** 不会。除非你直接访问 `ScheduledJob.NextRun` 或 `LastRun`（这种情况很少见），否则不会有影响。`GetJobInfo()` 的输出会更清晰地显示时区信息。

---

### Q4: 性能有提升吗？

**A:** 是的！新版本：
- 修复了 Timer 可能的内存泄漏
- 异步任务得到正确追踪
- 任务取消更加高效

---

### Q5: 如何查看任务执行历史？

**A:** 使用 `GetJobInfo()` 方法：

```csharp
var info = scheduler.GetJobInfo<MyJob>();
Console.WriteLine(info);

// 输出会包含执行历史：
// 执行历史记录数: 10
// 成功次数: 9
// 失败次数: 1
// 最近一次执行:
//   时间: 2025-11-05 04:00:00 +09:00
//   结果: 成功
//   耗时: 1234.56ms
```

---

## ✅ 迁移检查清单

- [ ] 将所有 `Dispose()` 改为 `await DisposeAsync()`
- [ ] 将 `using` 改为 `await using`（如果使用了 using 语句）
- [ ] （可选）使用便捷扩展方法替换 Cron 表达式
- [ ] 测试所有任务是否正常调度
- [ ] 测试应用程序关闭时任务是否正确完成

---

## 🎉 完成！

恭喜，你已成功迁移到改进版！享受更稳定、更强大的调度器吧！

如有问题，请参考完整文档或提交 Issue。