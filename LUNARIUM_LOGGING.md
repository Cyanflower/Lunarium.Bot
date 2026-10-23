# Lunarium.Logging 使用经验

> 包版本：`Lunarium.Logging 1.1.0`（`Hosting` / `Configuration` / `Http` 按需引用）
> 来源：本仓库 `src/Lunarium.Bot.Ticket` 的接入过程与踩坑记录，结论均对照 `~/Projects/Lunarium.Logging` 源码核实。

---

## 1. 包结构

| 包 | 作用 | 何时需要 |
| --- | --- | --- |
| `Lunarium.Logging` | 核心：`LoggerBuilder`、Sink、`FilterConfig`、`GlobalConfigurator` | 必装 |
| `Lunarium.Logging.Hosting` | `IHostBuilder` / `ILoggingBuilder` 集成、MEL 桥接 | 用 Generic Host 且想把 `ILogger<T>` 接过去 |
| `Lunarium.Logging.Configuration` | 从 `appsettings.json` 读取日志配置并热更新 | 需要运行时改级别 |
| `Lunarium.Logging.Http` | Seq / Loki / 通用 HTTP 批量推送 | 需要集中日志 |

本仓库的 `Ticket` 项目只用了核心包，日志系统在 `LogConfigurator.Initialize()` 里手工构建。

---

## 2. 初始化顺序（硬约束）

```csharp
// 1) 先做全局配置（时区、时间戳格式、JSON 选项），必须 Apply
GlobalConfigurator.Configure()
    .UseCustomTimezone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"))
    .UseJsonISO8601Timestamp()
    .UseTextCustomTimestamp("yyyy-MM-dd HH:mm:ss.fff zzz")
    .EnableAutoDestructuring()
    .Apply();

// 2) 再构建 logger
var logger = new LoggerBuilder()
    .SetLoggerName("Runtime")
    .AddConsoleSink()
    .Build();
```

**`Configure().Apply()` 必须早于任何 `Build()`。**

原因在 `GlobalConfigurator.cs`：

- `Configure()` 在 `GlobalConfigLock.Configured == true` 时直接抛 `InvalidOperationException`；
- `Apply()` 和 `Build()`（走 `ApplyDefaultIfNotConfigured()`）都会把 `Configured` 置为 `true` 并**锁定**。

所以先 `Build()` 的话，默认配置被自动应用并锁定，之后再 `Configure()` 就会抛异常。全局配置**全进程只能成功一次**，单元测试里反复初始化要多加注意。

---

## 3. LoggerName 与 Context 是两个不同的东西

| 概念 | 设置方式 | 用途 | 是否参与过滤 |
| --- | --- | --- | --- |
| `LoggerName` | `SetLoggerName("Runtime")` | 输出里的 `[Runtime]` 前缀，`LoggerManager` 的注册键 | ❌ 不参与 |
| `Context` | `ForContext("TicketService.TicketManager")` | **唯一的过滤依据** | ✅ 参与 |

几条实际输出示例样本:
```log
[2026-10-23 09:42:02.057 +08:00] [INF] [Runtime] [Program] 🚀 服务启动中...
[2026-10-23 09:42:02.057 +08:00] [INF] [Runtime] [Program] 🔗 代理: http://127.0.0.1:7890
[2026-10-23 09:42:02.065 +08:00] [INF] [Runtime] [TicketBot] 初始化 TicketBotService...
[2026-10-23 09:42:02.066 +08:00] [INF] [Runtime] [TicketBot] 配置 TicketBotService 的私有服务容器...
[2026-10-23 09:42:02.152 +08:00] [INF] [Runtime] [TicketBot.ConfigLoader] ⚙️ 配置文件加载并验证成功: /home/silica/Projects/LunariumBot.Net/src/Lunarium.Bot.Ticket/appsettings.json
[2026-10-23 09:42:02.229 +08:00] [INF] [Runtime] [TicketBot.ConfigLoader] ⚙️ 配置文件加载并验证成功: /home/silica/Projects/LunariumBot.Net/src/Lunarium.Bot.Ticket/appsettings.health.json
[2026-10-23 09:43:59.589 +08:00] [INF] [Runtime] [TicketBot.AppInteraction.TicketEntryHandle] [TrackId=3Fc30ppJ4p] AppInteraction: entry:complaint:fixed 权限校验通过: GuildId=1562804058111090698, UserId=867658156510150716
[2026-10-23 09:45:36.922 +08:00] [INF] [Runtime] [TicketBot.TicketService.TicketCardService] 审核卡片已发送: Ticket-00007 ChannelId=1562906521900425247 MessageId=1563005441858863164
[2026-10-23 09:46:43.635 +08:00] [INF] [Runtime] [TicketBot.AppInteraction.TicketAuditHandle] 审核操作完成: 工单=Ticket-00007 操作=Approve 结果=True 操作人=867658156510150716
```

两条容易搞错的规则：

1. **`Context` 不包含 `LoggerName`**。`LoggerName` 是 `LogEntry.LoggerName`，`Context` 是 `LogEntry.Context`，过滤只看后者。
2. **`ForContext` 是叠加的**。`LoggerWrapper` 的构造函数里：

   ```csharp
   _context = $"{wrapper.GetContext()}.{context}";
   ```

   因此 `logger.ForContext("TicketBot").ForContext("TicketService.TicketManager")`
   最终 Context 是 `TicketBot.TicketService.TicketManager`，而不是 `TicketService.TicketManager`。

### 这个叠加特性最容易埋雷

典型场景：Bot 在构造函数里给自己加了一层上下文，然后把**这个已经带上下文的 logger** 注册进私有 DI 容器：

```csharp
_logger = logger.ForContext("TicketBot");
_privateServices = ConfigurePrivateServices(proxyUrl, _logger); // ← 带上下文
...
.AddSingleton(logger)   // 所有子服务拿到的都是 TicketBot.Xxx
```

结果：每个子服务写的 `ForContext("AppInteraction.TicketAuditHandle")`，实际 Context 是
`TicketBot.AppInteraction.TicketAuditHandle`。过滤配置里如果只写 `AppInteraction.TicketAuditHandle`，
**永远不会命中**，而且不会报错——只是那个 sink 一条日志都没有。

> 经验：凡是配置过滤前缀，都要先确认"logger 到手时已经带了几层上下文"。

---

## 4. FilterConfig 的确切语义

```csharp
public record FilterConfig
{
    public List<string>? ContextFilterIncludes { get; init; }
    public List<string>? ContextFilterExcludes { get; init; }
    public bool IgnoreFilterCase { get; init; } = false;
    public LogLevel LogMinLevel { get; init; } = LogLevel.Info;
    public LogLevel LogMaxLevel { get; init; } = LogLevel.Critical;
}
```

`Filter.CheckContextFilters` 的实现要点：

1. **只做 `StartsWith`**，不是包含、不是通配符。
2. **默认大小写敏感**（`Ordinal`）。要忽略大小写必须显式 `IgnoreFilterCase = true`。
3. 判断顺序：先 `Includes`（非空则必须命中其一）→ 再 `Excludes`（命中任一即丢弃）。
4. `Includes` 与 `Excludes` 都为空 → 全部通过（不是全部丢弃）。
5. 每个 sink 各自持有一个 `LoggerFilter`，并缓存 `Context -> bool`；**缓存上限 2048，满了整体清空**。

第 5 点意味着：**Context 必须是有限的静态集合**。源码注释里专门写了反例：

```csharp
logger.ForContext("Order.Processor").Info(...)   // ✅ 复用同一个字符串
logger.ForContext($"Order.{orderId}").Info(...)  // ❌ 缓存迅速膨胀，且没法配置过滤
```

需要请求级标识请用 `BeginTrack(trackId)`，它走的是 `TrackInfo`，不会污染 `Context`。

---

## 5. 一个可用的分层方案

本仓库 `src/Lunarium.Bot.Ticket/LogConfigurator.cs` 的实际结构：

| 文件 | Includes | Excludes | 级别 |
| --- | --- | --- | --- |
| `Logs/Runtime/Runtime.log` | 无 | 仅审计前缀 | `FILE_LOG_LEVEL`（默认 Info） |
| `Logs/Audit/Audit.log` | 审计前缀 | 无 | 同上 |
| `Logs/EW/Error.log` | 无 | 无 | `Min = Error` |
| `Logs/EW/Warning.log` | 无 | 无 | `Min = Max = Warning` |
| Console | 无 | 无 | `CONSOLE_LOG_LEVEL` |

两条约定：

- **"只有 Warning"要写 `LogMinLevel = Warning` 且 `LogMaxLevel = Warning`**，只写 Min 会把 Error/Critical 也收进来。
- **告警文件不要加 Context 过滤**。审计日志里的异常同样需要出现在 `Error.log`，否则"只进 Audit"会变成告警黑洞。

### 前缀常量，别写两遍

```csharp
public const string RootContext = "TicketBot";

private static readonly List<string> AuditOnlyPrefixes = [$"{RootContext}.Audit"];
private static readonly List<string> SharedAuditPrefixes = [$"{RootContext}.AppInteraction"];
```

`ForContext` 那一侧也引用同一个常量（`logger.ForContext(LogConfigurator.RootContext)`），避免两处各写一次字符串、改一处忘另一处导致静默失效。

### 仅审计 vs 审计 + 运行时

- **仅审计**：纯操作记录（通过 / 拒绝 / 关闭 / 归档），用专属 `_audit` logger 输出，Runtime.log 不重复记一份。
- **审计 + 运行时**：管理交互类（按钮、斜杠命令）两边都留。这些类用 `BeginTrack` 生成追踪 ID，只搬进 Audit 会让 Runtime 里出现断链，排查"用户点了没反应"时丢上下文。

已核实 `TrackScope.Log` 是直接转发给内层 logger，**不会**给 `Context` 加追踪 ID 前缀，所以前缀匹配在 `track.Info(...)` 场景下依然成立。

---

## 6. 踩坑清单

| # | 症状 | 原因 | 处理 |
| --- | --- | --- | --- |
| 1 | `Audit.log` 一直是空文件 | 过滤器里的 Context 名是从旧项目抄的，本项目根本不存在 | 用实际 `ForContext` 值核对 |
| 2 | 同上 | 漏了根上下文（`TicketBot.`），`StartsWith` 匹配失败 | 写**完整路径**，或抽常量 |
| 3 | 改了 `ForContext` 字符串，审计突然空了 | 配置里也硬编码了旧名字 | 用 `RootContext` 常量统一 |
| 4 | 想只看 Warning，结果 Error 也进来了 | 只设了 `LogMinLevel` | 同时设 `LogMaxLevel` |
| 5 | 过滤器"看起来配了但没效果" | 大小写不一致（默认敏感） | 统一命名，或 `IgnoreFilterCase = true` |
| 6 | 测试里第二次初始化抛异常 | `GlobalConfigurator` 只能成功一次 | 测试隔离或复用同一实例 |
| 7 | 日志里上下文越来越长 | 多次 `ForContext` 叠加 | 明确"哪一层 logger 传给谁" |
| 8 | 日志量突然暴涨 / CPU 升高 | `ForContext` 里拼了动态值 | 动态标识改用 `BeginTrack` |

---

## 7. 如何验证过滤真的生效

不要靠"看起来应该对"，用一个探针把最终 `Context` 打出来（`GetContext()` 是 internal，用反射）：

```csharp
using System.Reflection;
using Lunarium.Logging;

static string Ctx(ILogger l) =>
    (string)l.GetType()
        .GetMethod("GetContext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(l, null)!;

var root = new LoggerBuilder().SetLoggerName("Runtime").Build();
var bot = root.ForContext("TicketBot");

Console.WriteLine(Ctx(bot.ForContext("TicketService.TicketManager")));   // TicketBot.TicketService.TicketManager
Console.WriteLine(Ctx(bot.ForContext("Audit.TicketWorkflow")));          // TicketBot.Audit.TicketWorkflow
Console.WriteLine(Ctx(root.ForContext("Program")));                      // Program
```

然后用与 `Filter` 相同的方式断言（`StartsWith` + `Ordinal`）：

```csharp
static bool Matches(string ctx, IEnumerable<string> prefixes)
    => prefixes.Any(p => ctx.StartsWith(p, StringComparison.Ordinal));
```

本仓库实际的验证结果：

```
TicketBot.TicketService.TicketWorkflowService   Runtime=True   Audit=False
TicketBot.Audit.TicketWorkflow                  Runtime=False  Audit=True
TicketBot.AppInteraction.TicketAuditHandle      Runtime=True   Audit=True
TicketBot.TicketService.TicketManager           Runtime=True   Audit=False
TicketBot                                           Runtime=True   Audit=False
Program                                            Runtime=True   Audit=False
```

---

## 8. 本仓库的 Context 速查表

根上下文 `TicketBot`（定义在 `LogConfigurator.RootContext`）。

| 来源 | 完整 Context |
| --- | --- |
| `TicketBotService` 本类 | `TicketBot` |
| `Program` | `Program`（不经过 TicketBot 层） |
| 所有 `AppInteraction/*` | `TicketBot.AppInteraction.{ConfigEditorHandle, MaintainCommand, AdminCommand, TicketEntryHandle, TicketModalHandle, TicketAuditHandle, TicketManageHandle}` |
| 所有 `TicketService/*` | `TicketBot.TicketService.{TicketManager, TicketWorkflowService, TicketCardService, TicketSuggestionService, TicketCrawler, TicketResourceDownloader, TicketArchiveService, TicketLocalData, TicketCooldownStore, TicketDeletionQueue, TicketMaintenanceService}` |
| `TicketWorkflowService._audit` | `TicketBot.Audit.TicketWorkflow` |

新增日志类时遵守两条：

1. `ForContext` 用 `模块.类` 两段式，例如 `TicketService.TicketFoo`；
2. 需要参与过滤的，前缀一定要带 `TicketBot.`。

---

## 9. 速查

```csharp
// 构建
var logger = new LoggerBuilder().SetLoggerName("Runtime")
    .AddConsoleSink(toJson: false, isColor: true, FilterConfig: new FilterConfig { LogMinLevel = LogLevel.Info })
    .AddSizedRotatingFileSink("Logs/Runtime/Runtime.log", maxFileSizeMB: 10, maxFile: 5,
        FilterConfig: new FilterConfig { ContextFilterExcludes = ["TicketBot.Audit"] })
    .Build();

// 上下文（可叠加，注意根前缀）
var scoped = logger.ForContext("TicketService.TicketManager");

// 追踪（不污染 Context）
using var track = scoped.BeginTrack(NanoId.GenerateUtf8(10));
track.Info("处理工单: {Ticket}", ticket);

// 级别
scoped.Debug("...");
scoped.Info("...");
scoped.Warning("...");
scoped.Error(ex, "...");
scoped.Critical(ex, "...");
```

文字输出的字段顺序固定为：

```
[时间戳] [级别] [LoggerName] [Context] [TrackId] 消息
```

- 级别缩写：`DBG` / `INF` / `WRN` / `ERR` / `CRT`
- `LoggerName` 为空时该段省略（`ILogTarget.IncludeLoggerName` 默认 `true`）
- `Context` 与 `TrackId` 由各自的 `Include*` 开关控制
