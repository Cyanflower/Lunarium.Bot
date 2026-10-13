# Lunarium.Bot.Scheduler

一个轻量级、高性能的 .NET 定时任务调度器，基于 Cron 表达式实现，完美集成依赖注入 (DI) 容器。

## 🎯 设计初衷

相比于市面上的其他定时任务库（如 Quartz.NET、Hangfire），`Lunarium.Bot.Scheduler` 专为 **DI 容器内使用** 而设计，完美适配 `Lunarium.Bot` 解决方案的 **双层 DI 架构**（主机 DI + 服务类私有 DI）。

### 核心优势

**🪶 轻量级**
- 零配置文件，零外部依赖（除 Cronos）
- 不需要数据库、不需要持久化
- 整个调度器只有几个核心类，代码简洁易懂

**⚡ 高性能**
- 基于 .NET 原生 `Timer`，开销极小
- 任务执行互不阻塞，支持并发
- 内存占用低，适合长时间运行

**🔧 简单易配置**
- API 设计简洁，几行代码即可完成配置
- 标准 Cron 表达式，学习成本低
- 无需复杂的配置文件或数据库表

**💉 DI 原生集成**
- **可在任意 DI 容器内注册和管理**，而不是只能在主机级别使用
- 任务通过 DI 容器创建，自动解析依赖
- 完美适配 `IHostedService` 内部的私有容器场景

### 典型应用场景

本调度器专为以下场景设计：

```csharp
// ❌ 其他库通常需要在主机级别配置
builder.Services.AddQuartz(...);  // 全局配置，所有服务共享

// ✅ LunariumScheduler 可以在服务类的私有容器内独立管理
public class DataCenterBotService : IHostedService
{
    private readonly IServiceProvider _privateServices;
    
    public DataCenterBotService(...)
    {
        // 每个服务类都有自己的 DI 容器和调度器
        _privateServices = new ServiceCollection()
            .AddSingleton<FullSyncJob>()
            .AddSingleton<AnalysisJob>()
            .AddSingleton<LunariumScheduler>(provider =>
                new LunariumScheduler(provider, logger))
            .BuildServiceProvider();
            
        var scheduler = _privateServices.GetRequiredService<LunariumScheduler>();
        scheduler.ScheduleAsync<FullSyncJob>("0 4 * * 2");
        scheduler.Start();
    }
}
```

这种设计使得：
- **每个机器人服务可以有自己的任务调度器**，互不干扰
- **任务的依赖可以限定在服务内部**，不会污染全局容器
- **服务的启动/停止完全独立**，符合微服务架构思想

## ✨ 特性

- **🎯 Cron 表达式支持**：使用标准 Cron 表达式定义任务执行时间
- **💉 DI 原生集成**：任务通过依赖注入容器创建，自动解析依赖
- **⚡ 异步/同步双模式**：同时支持同步 (`IJob`) 和异步 (`IAsyncJob`) 任务
- **🌍 时区感知**：支持为每个任务指定独立的时区
- **🔄 并发保护**：内置任务执行状态管理，防止重复执行
- **📊 任务监控**：提供详细的任务信息查询接口
- **🛡️ 异常隔离**：单个任务失败不影响其他任务的调度
- **📝 完整日志**：集成日志系统，记录任务执行详情

## 📦 安装

在项目中引用此类库：

```xml
<ProjectReference Include="../Lunarium.Bot.Scheduler/Lunarium.Bot.Scheduler.csproj" />
```

## 🚀 快速开始

### 1. 定义任务

创建一个实现 `IJob` 或 `IAsyncJob` 接口的任务类：

```csharp
using Lunarium.Bot.Scheduler.Interfaces;

// 同步任务
public class DailyReportJob : IJob
{
    private readonly ILogger<DailyReportJob> _logger;
    private readonly IDataService _dataService;

    public DailyReportJob(ILogger<DailyReportJob> logger, IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;
    }

    public void Execute()
    {
        _logger.LogInformation("正在生成每日报告...");
        var report = _dataService.GenerateReport();
        // 处理报告...
    }
}

// 异步任务
public class DataSyncJob : IAsyncJob
{
    private readonly IApiClient _apiClient;

    public DataSyncJob(IApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task ExecuteAsync()
    {
        await _apiClient.SyncDataAsync();
    }
}
```

### 2. 注册任务到 DI 容器

在 `Program.cs` 或服务配置中注册任务类：

```csharp
var builder = Host.CreateApplicationBuilder(args);

// 注册任务类
builder.Services.AddTransient<DailyReportJob>();
builder.Services.AddTransient<DataSyncJob>();

// 注册其他依赖
builder.Services.AddSingleton<IDataService, DataService>();
builder.Services.AddSingleton<IApiClient, ApiClient>();
```

### 3. 创建并配置调度器

**可以在任意位置创建调度器**（主机级别、服务类内部、甚至是普通方法中）：

```csharp
// 方式 1: 在服务类的私有容器中创建（推荐）
public class MyBotService : IHostedService
{
    private readonly LunariumScheduler _scheduler;
    
    public MyBotService(...)
    {
        var privateServices = new ServiceCollection()
            .AddSingleton<DailyReportJob>()
            .AddSingleton<DataSyncJob>()
            .AddSingleton<LunariumScheduler>(sp => 
                new LunariumScheduler(sp, logger))
            .BuildServiceProvider();
            
        _scheduler = privateServices.GetRequiredService<LunariumScheduler>();
        
        // 注册任务
        _scheduler.Schedule<DailyReportJob>("0 9 * * *", name: "每日报告");
        _scheduler.ScheduleAsync<DataSyncJob>("0 * * * *", name: "数据同步");
        
        // 启动调度器
        _scheduler.Start();
    }
}

// 方式 2: 在主机级别创建（传统方式）
var scheduler = new LunariumScheduler(
    serviceProvider: builder.Services.BuildServiceProvider(),
    logger: loggerInstance
);

// 注册任务
// 每天 9:00 执行
scheduler.Schedule<DailyReportJob>("0 9 * * *", name: "每日报告");

// 每小时执行一次（异步）
scheduler.ScheduleAsync<DataSyncJob>("0 * * * *", name: "数据同步");

// 每 5 分钟执行一次，使用东京时区
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
scheduler.ScheduleAsync<DataSyncJob>("*/5 * * * *", name: "实时监控", timeZone: tokyoTz);

// 启动调度器
scheduler.Start();
```

## 📖 详细用法

### Cron 表达式格式

调度器使用标准的 5 位 Cron 表达式：

```
┌───────────── 分钟 (0 - 59)
│ ┌───────────── 小时 (0 - 23)
│ │ ┌───────────── 日期 (1 - 31)
│ │ │ ┌───────────── 月份 (1 - 12)
│ │ │ │ ┌───────────── 星期 (0 - 6) (0 = 星期日)
│ │ │ │ │
* * * * *
```

**常用示例：**

| Cron 表达式 | 说明 |
|------------|------|
| `0 * * * *` | 每小时整点执行 |
| `*/5 * * * *` | 每 5 分钟执行一次 |
| `0 9 * * *` | 每天 9:00 执行 |
| `0 9 * * 1` | 每周一 9:00 执行 |
| `0 0 1 * *` | 每月 1 号 0:00 执行 |
| `30 8 * * 1-5` | 工作日 8:30 执行 |
| `0 */2 * * *` | 每 2 小时执行一次 |

### 时区支持

每个任务可以指定独立的时区：

```csharp
// 使用 UTC（默认）
scheduler.Schedule<JobA>("0 12 * * *");

// 使用东京时区
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
scheduler.Schedule<JobB>("0 12 * * *", timeZone: tokyoTz);

// 使用纽约时区
var nyTz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
scheduler.Schedule<JobC>("0 12 * * *", timeZone: nyTz);
```

**注意：** Cron 表达式中的时间将按照指定时区解释。

### 任务管理

```csharp
// 移除任务（按类型）
scheduler.Remove<DailyReportJob>();

// 移除任务（按名称）
scheduler.Remove("每日报告");

// 查询单个任务信息
string? info = scheduler.GetJobInfo<DailyReportJob>();
Console.WriteLine(info);

// 查询所有任务
string allInfo = scheduler.GetAllJobsInfo();
Console.WriteLine(allInfo);

// 停止调度器
scheduler.Stop();

// 重新启动
scheduler.Start();

// 释放资源
scheduler.Dispose();
```

### 任务信息输出示例

```
==================== 调度器信息 ====================
调度器状态: 运行中
已注册任务数: 2

-------------------- 任务 [1] --------------------
任务名称: 每日报告
任务类型: DailyReportJob
Cron 表达式: 0 9 * * *
任务模式: 同步
时区: UTC
下次执行 (UTC): 2025-10-31 09:00:00
下次执行 (本地): 2025-10-31 09:00:00 (UTC)
上次执行 (UTC): 2025-10-30 09:00:00
上次执行 (本地): 2025-10-30 09:00:00 (UTC)
运行状态: 已调度

-------------------- 任务 [2] --------------------
任务名称: 数据同步
任务类型: DataSyncJob
Cron 表达式: 0 * * * *
任务模式: 异步
时区: Asia/Tokyo
下次执行 (UTC): 2025-10-30 15:00:00
下次执行 (本地): 2025-10-31 00:00:00 (Tokyo Standard Time)
上次执行: 从未执行
运行状态: 已调度

====================================================
```

## 🔧 高级特性

### 并发控制

调度器内置了任务执行状态管理。如果某个任务仍在执行中，调度器会自动跳过下一次调度：

```csharp
public class LongRunningJob : IAsyncJob
{
    public async Task ExecuteAsync()
    {
        // 这个任务可能需要 10 分钟才能完成
        await SomeLongOperation();
    }
}

// 每 5 分钟调度一次
// 但如果任务还在运行，下一次调度会被跳过
scheduler.ScheduleAsync<LongRunningJob>("*/5 * * * *");
```

日志输出：
```
[警告] 任务 LongRunningJob 仍在执行中，跳过本次调度
```

### 动态任务注册

调度器启动后，仍可以动态添加新任务：

```csharp
// 启动调度器
scheduler.Start();

// 运行中动态添加任务
scheduler.Schedule<NewJob>("0 * * * *");
// 新任务会立即被调度
```

### 异常处理

任务执行中的异常会被自动捕获和记录，不会影响其他任务：

```csharp
public class FaultyJob : IJob
{
    public void Execute()
    {
        throw new Exception("出错了！");
    }
}
```

日志输出：
```
[错误] 任务执行失败: FaultyJob，耗时: 0.52ms
System.Exception: 出错了！
   at FaultyJob.Execute()
```

调度器会继续为该任务安排下一次执行。

## 📚 完整示例

### 在私有 DI 容器中使用（推荐）

这是 `Lunarium.Bot.DataCenter` 项目的实际使用案例，展示了如何在 `IHostedService` 的私有容器中集成调度器：

```csharp
public class DataCenterBotService : IHostedService
{
    private readonly string _token;
    private readonly ILogger _logger;
    private readonly IServiceProvider _privateServices;
    private readonly LunariumScheduler _scheduler;

    public DataCenterBotService(string token, ILogger runtimeLogger, ProxyConfig proxyConfig)
    {
        _token = token;
        _logger = runtimeLogger.ForContext("DataCenterBotService");

        // ========== 构建私有 DI 容器 ==========
        _privateServices = new ServiceCollection()
            // 基础设施
            .AddSingleton(_logger)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton<DatabaseService>()
            .AddSingleton<DataProcessService>()
            
            // 业务服务
            .AddSingleton<Scanner>()
            .AddSingleton<Listener>()
            .AddSingleton<DataAnalysis>()
            
            // ========== 定时任务 ==========
            .AddSingleton<AnalysisJob>()      // 数据分析任务
            .AddSingleton<FullSyncJob>()      // 全量同步任务
            
            // ========== 调度器 ==========
            .AddSingleton<LunariumScheduler>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger>();
                return new LunariumScheduler(
                    provider, 
                    logger.ToMicrosoftLogger("Scheduler")
                );
            })
            
            .BuildServiceProvider();

        // 解析调度器
        _scheduler = _privateServices.GetRequiredService<LunariumScheduler>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("⚙️ 正在配置定时任务调度器...");

        // 配置时区
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

        // 注册任务
        _scheduler.ScheduleAsync<FullSyncJob>(
            cronExpression: "0 4 * * 2",    // 每周二 4:00
            name: "同步扫描",
            timeZone: timeZone
        );

        _scheduler.ScheduleAsync<AnalysisJob>(
            cronExpression: "0 4 * * *",    // 每天 4:00
            name: "每日分析",
            timeZone: timeZone
        );

        // 启动调度器
        _scheduler.Start();
        
        _logger.Info("✅ 定时任务已配置完成。");
        _logger.Info($"\n{_scheduler.GetAllJobsInfo()}");

        // 启动 Discord Bot
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止服务...");

        // 停止调度器
        _scheduler.Stop();
        _scheduler.Dispose();

        // 释放私有容器
        if (_privateServices is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await _client.StopAsync();
    }
}
```

### 定义任务类

任务类从私有容器中解析依赖：

```csharp
// 异步任务示例
public class AnalysisJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly DataAnalysis _analysis;
    private readonly DatabaseService _database;

    // 依赖通过构造函数注入
    public AnalysisJob(
        ILogger logger,
        DataAnalysis analysis,
        DatabaseService database)
    {
        _logger = logger.ForContext(nameof(AnalysisJob));
        _analysis = analysis;
        _database = database;
    }

    public async Task ExecuteAsync()
    {
        _logger.Info("开始执行数据分析任务...");

        try
        {
            // 生成分析报告
            var report = await _analysis.GenerateReportAsync();
            
            // 保存到数据库
            await _database.SaveReportAsync(report);
            
            _logger.Info("数据分析任务执行完成。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "数据分析任务执行失败");
            throw;
        }
    }
}

// 同步任务示例
public class FullSyncJob : IAsyncJob
{
    private readonly Scanner _scanner;
    private readonly ILogger _logger;

    public FullSyncJob(Scanner scanner, ILogger logger)
    {
        _scanner = scanner;
        _logger = logger.ForContext(nameof(FullSyncJob));
    }

    public async Task ExecuteAsync()
    {
        _logger.Info("开始执行全量同步...");
        await _scanner.ScanAllGuildsAsync();
        _logger.Info("全量同步完成。");
    }
}
```

### 在主机级别使用（传统方式）

如果你的项目采用传统的单层 DI 架构，也可以在主机级别使用：

```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);

// 注册任务类
builder.Services.AddTransient<DailyReportJob>();
builder.Services.AddTransient<DataSyncJob>();

// 注册调度器服务
builder.Services.AddHostedService<SchedulerHostedService>();

var host = builder.Build();
await host.RunAsync();

// SchedulerHostedService.cs
public class SchedulerHostedService : IHostedService
{
    private readonly LunariumScheduler _scheduler;

    public SchedulerHostedService(IServiceProvider serviceProvider, ILogger<SchedulerHostedService> logger)
    {
        _scheduler = new LunariumScheduler(serviceProvider, logger);

        // 注册所有任务
        _scheduler.Schedule<CleanupJob>("0 2 * * *", name: "每日清理");
        _scheduler.ScheduleAsync<BackupJob>("0 3 * * *", name: "每日备份");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _scheduler.Stop();
        _scheduler.Dispose();
        return Task.CompletedTask;
    }
}
```

## 🆚 与其他库的对比

| 特性 | LunariumScheduler | Quartz.NET | Hangfire |
|-----|------------------|-----------|----------|
| **轻量级** | ✅ 零配置，无外部依赖 | ❌ 需要配置文件 | ❌ 需要数据库 |
| **私有容器支持** | ✅ 完美支持 | ⚠️ 需要特殊配置 | ❌ 仅支持全局 |
| **学习成本** | ✅ 极低 | ⚠️ 中等 | ⚠️ 较高 |
| **内存占用** | ✅ 极小 | ⚠️ 中等 | ❌ 较大 |
| **集群支持** | ❌ 不支持 | ✅ 支持 | ✅ 支持 |
| **持久化** | ❌ 不支持 | ✅ 支持 | ✅ 支持 |
| **Web UI** | ❌ 无 | ⚠️ 第三方 | ✅ 内置 |

**结论：** 如果你需要分布式任务调度、持久化或 Web 管理界面，请选择 Quartz.NET 或 Hangfire。如果你只需要简单、轻量、高性能的进程内任务调度，`LunariumScheduler` 是最佳选择。

## ⚠️ 注意事项

1. **任务类必须注册到 DI 容器**：调度器通过 DI 容器获取任务实例，忘记注册会导致运行时错误。

2. **私有容器的作用域**：在私有容器中使用时，任务的依赖也应该注册在同一个容器中，否则会出现依赖解析失败。

   ```csharp
   // ✅ 正确：任务和依赖都在私有容器中
   _privateServices = new ServiceCollection()
       .AddSingleton<DatabaseService>()
       .AddSingleton<MyJob>()  // MyJob 依赖 DatabaseService
       .AddSingleton<LunariumScheduler>()
       .BuildServiceProvider();
   
   // ❌ 错误：MyJob 依赖的服务未注册
   _privateServices = new ServiceCollection()
       .AddSingleton<MyJob>()  // 缺少 DatabaseService
       .AddSingleton<LunariumScheduler>()
       .BuildServiceProvider();
   ```

3. **任务应该是轻量级的**：每次执行都会创建新的作用域 (`IServiceScope`)，避免在任务构造函数中执行耗时操作。

4. **时区陷阱**：注意夏令时 (DST) 的影响，在时区切换时可能会跳过或重复执行任务。

5. **异步任务不阻塞调度**：异步任务会在后台执行，不会阻塞下一次调度的计算。

6. **线程安全**：调度器的所有公共方法都是线程安全的。

7. **资源释放**：使用完毕后应调用 `Dispose()` 释放资源，尤其是在服务停止时。

   ```csharp
   public async Task StopAsync(CancellationToken cancellationToken)
   {
       _scheduler.Stop();
       _scheduler.Dispose();  // ✅ 释放 Timer 资源
       
       if (_privateServices is IDisposable disposable)
       {
           disposable.Dispose();  // ✅ 释放容器资源
       }
   }
   ```

8. **避免在主机和私有容器中重复注册**：如果使用私有容器模式，不要将调度器注册到主机容器，这会导致两个独立的调度器实例。

## 🔗 依赖项

- **.NET 9.0**
- **Cronos** (v0.11.1)：用于解析 Cron 表达式
- **Microsoft.Extensions.Logging.Abstractions** (v9.0.10)：日志抽象
- **Lunarium.Bot.Logger**：项目自定义日志框架

## 📄 许可证

本项目遵循与 Lunarium.Bot 主项目相同的许可证。

---

*如有问题或建议，欢迎提交 Issue 或 Pull Request。*