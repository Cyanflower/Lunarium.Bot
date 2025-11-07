# 📋 变更日志 (CHANGELOG)

## v2.0.0 - 重大改进版本

### 🔥 重大变更 (Breaking Changes)

#### 1. IDisposable → IAsyncDisposable
- **变更原因**: 确保所有异步任务在释放前正确完成
- **影响**: 必须使用 `await DisposeAsync()` 替代 `Dispose()`
- **详情**: [MIGRATION.md#步骤2](#)

#### 2. DateTime → DateTimeOffset
- **变更原因**: 更清晰的时区处理
- **影响**: `ScheduledJob.NextRun` 和 `LastRun` 现在使用 `DateTimeOffset?`
- **详情**: 大多数用户无需修改代码

---

### ✨ 新增功能

#### 1. 任务执行历史追踪
- 保留最近 10 次执行记录
- 记录执行时间、耗时、成功/失败状态
- 新增方法：
  - `ScheduledJob.GetHistory()` - 获取所有历史
  - `ScheduledJob.GetLastHistory()` - 获取最近一次
  - `ScheduledJob.GetSuccessCount()` - 成功次数
  - `ScheduledJob.GetFailureCount()` - 失败次数

**示例:**
```csharp
var info = scheduler.GetJobInfo<MyJob>();
// 现在会包含执行历史统计
```

---

#### 2. 便捷扩展方法
新增语义化的调度方法，无需手写 Cron 表达式：

**同步任务:**
- `ScheduleDaily<TJob>(hour, minute)` - 每天执行
- `ScheduleWeekly<TJob>(dayOfWeek, hour, minute)` - 每周执行
- `ScheduleMonthly<TJob>(day, hour, minute)` - 每月执行
- `ScheduleInterval<TJob>(intervalMinutes)` - 按间隔执行
- `ScheduleHourly<TJob>(minute)` - 每小时执行

**异步任务:**
- `ScheduleDailyAsync<TJob>(...)` 
- `ScheduleWeeklyAsync<TJob>(...)`
- `ScheduleMonthlyAsync<TJob>(...)`
- `ScheduleIntervalAsync<TJob>(...)`
- `ScheduleHourlyAsync<TJob>(...)`

**示例:**
```csharp
// 旧方式（仍然支持）
scheduler.Schedule<MyJob>("0 4 * * *");

// 新方式（更易读）
scheduler.ScheduleDaily<MyJob>(hour: 4);
```

---

#### 3. 链式调用支持
所有扩展方法都返回 `LunariumScheduler`，支持 Fluent API：

```csharp
var scheduler = new LunariumScheduler(services, logger)
    .ScheduleDaily<DailyJob>(hour: 4)
    .ScheduleWeekly<WeeklyJob>(DayOfWeek.Tuesday, hour: 9)
    .ScheduleInterval<HeartbeatJob>(intervalMinutes: 5);

scheduler.Start();
```

---

### 🐛 Bug 修复

#### 1. ⚠️ 修复 Timer 内存泄漏
- **问题**: 旧版本使用 `Timer` 递归创建，可能导致内存泄漏
- **解决方案**: 改用 `Task.Delay` 实现调度循环
- **影响**: 提升稳定性和性能

**技术细节:**
```csharp
// 旧实现（有问题）
private void OnTimerElapsed(ScheduledJob job)
{
    // 执行任务...
    
    // ❌ 创建新 Timer，可能导致泄漏
    job.CurrentTimer = new Timer(...);
}

// 新实现（正确）
private async Task RunJobLoopAsync(ScheduledJob job, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var nextRun = CalculateNextRun(job);
        await Task.Delay(nextRun - DateTime.UtcNow, ct);
        await ExecuteJobAsync(job);
    }
}
```

---

#### 2. ⚠️ 修复异步任务 Fire-and-Forget 问题
- **问题**: 异步任务没有被正确追踪，可能被意外吞掉
- **解决方案**: 
  - 追踪所有正在执行的任务
  - `DisposeAsync` 时等待任务完成
- **影响**: 确保应用程序关闭时所有任务都能正确完成

**新增追踪机制:**
```csharp
public class ScheduledJob
{
    private Task? _runningTask;
    
    public async Task WaitForCompletionAsync()
    {
        if (_runningTask != null)
        {
            await _runningTask;
        }
    }
}

public async ValueTask DisposeAsync()
{
    Stop();
    
    // ✅ 等待所有任务完成
    var tasks = _jobs.Select(j => j.WaitForCompletionAsync());
    await Task.WhenAll(tasks);
}
```

---

### 🎨 改进

#### 1. 时区处理更清晰
- 统一使用 `DateTimeOffset` 
- 日志输出包含时区偏移信息
- `GetJobInfo()` 同时显示 UTC 和本地时间

**输出示例:**
```
下次执行 (UTC): 2025-11-05 19:00:00 +00:00
下次执行 (本地): 2025-11-06 04:00:00 +09:00 (東京 (標準時))
```

---

#### 2. 更详细的任务信息
`GetJobInfo()` 和 `GetAllJobsInfo()` 现在包含：
- 执行历史统计
- 最近一次执行详情
- 更清晰的时区信息

---

#### 3. 更好的错误处理
- 任务执行失败时记录完整错误信息
- 执行历史包含异常消息和堆栈跟踪

---

### 📚 文档

新增以下文档：
- **README.md** - 完整使用指南
- **MIGRATION.md** - 迁移指南
- **CHANGELOG.md** - 本文件

---

### 🔄 内部重构

#### 1. 调度机制重写
- 从 `Timer` 改为 `Task.Delay` + `CancellationToken`
- 每个任务有独立的调度循环
- 更好的取消令牌管理

#### 2. 代码结构改进
- 新增 `JobExecutionHistory` 模型
- 新增 `SchedulerExtensions` 扩展类
- 更清晰的职责分离

---

### ⚠️ 已知限制

1. **执行历史仅保留 10 条**
   - 如需持久化，请考虑使用 Hangfire 等库

2. **间隔调度仅支持分钟级别**
   - 使用 `ScheduleInterval()` 仅支持 1-59 分钟
   - 对于小时/天级间隔，请使用原始 Cron 表达式

3. **任务重叠自动跳过**
   - 如果任务执行时间超过调度间隔，会自动跳过
   - 这是设计行为，防止任务堆积

---

### 📊 性能改进

| 指标 | 旧版本 | 新版本 | 改进 |
|------|--------|--------|------|
| 内存泄漏风险 | 中 | 低 | ✅ 修复 Timer 泄漏 |
| 任务追踪 | 无 | 完整 | ✅ 追踪所有任务 |
| 取消响应性 | 一般 | 优秀 | ✅ 独立取消令牌 |
| 时区处理 | 混乱 | 清晰 | ✅ 统一 DateTimeOffset |

---

## v1.0.0 - 初始版本

### ✨ 初始功能
- 基于 Cron 表达式的任务调度
- 支持同步和异步任务
- DI 级别的灵活性
- 时区支持
- 防止任务重叠执行

---

## 迁移指南

从 v1.0.0 升级到 v2.0.0，请参考 [MIGRATION.md](./MIGRATION.md)。

---

## 贡献者

感谢所有为这个项目做出贡献的开发者！

---

## 许可证

[您的许可证信息]