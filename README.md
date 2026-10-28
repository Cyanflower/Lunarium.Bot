# Lunarium.Bot 开发指南

> 本文件是仓库的架构事实来源。**新代码、重构与评审一律以 `src/Lunarium.Bot.Ticket` 为模板。**
> 功能进度以 `git log` 为准；本文只描述架构代际，不承诺某个 bot 的功能完成度。

## 1. 从哪里开始

### 1.1 三个层次

| 层次 | 位置 | 职责 |
| --- | --- | --- |
| 应用 | `src/Lunarium.Bot.*` | 各 bot 的业务实现，独立进程、独立部署 |
| 仓库内基础设施 | `src/Lunarium.Bot.Framework`、`src/Lunarium.Bot.AI` | Bot 通用基建（DI / 配置 / 鉴权 / 会话 / 通知 / 健康监控）与 LLM 客户端 |
| 通用基础库（NuGet） | `Lunarium.Common`、`Lunarium.Logging`、`Lunarium.Scheduler` | 与 Discord 无关，独立仓库维护并发布 |

**参考实现**：`src/Lunarium.Bot.Ticket`。它是最新从头开发、且完整走通上述三层协作的项目。

### 1.2 判定“已是新标准”的硬判据

- `Program.cs` 约 100 行，只有四段职责（见 §2.2）
- 不手写 `new DiscordSocketClient(...)`，用 `AddDiscordSocketClient(...)`
- 不出现 `AuthUtils`，鉴权走 `RuleEngine.ValidateAsync(...)`
- 配置模型实现 `IValidatableObject`，读写走 `ConfigLoader<T>`
- 消息用 `MessagePrefab` / `ModalWindowPrefab`，不手拼 `EmbedBuilder`
- 日志有独立的 `LogConfigurator.cs`，且有审计分流
- 引用 `Lunarium.Bot.Framework`，不引用已删除的 `Lunarium.Bot.Extensions`、`Lunarium.Bot.Logger`、`Lunarium.Bot.Scheduler`

### 1.3 项目状态

| 项目 | 类型 | 架构状态 | 说明 |
| --- | --- | --- | --- |
| `Lunarium.Bot.Ticket` | 应用 | ✅ 标准 | 参考实现；工单提交 / 审批 / 归档 / LLM 建议 / 自动通过 / 安抚 |
| `Lunarium.Bot.Framework` | 共享库 | ✅ 标准 | 仓库内 bot 基础设施的唯一来源（原 `Lunarium.Bot.Extensions`） |
| `Lunarium.Bot.AI` | 共享库 | ✅ 新增 | DeepSeek Responses 无状态推理客户端，按需接入 |
| `Lunarium.Bot.Archive` | 应用 | 🚧 迁移中 | 已切到 Framework，现代化改造尚未完成 |
| `Lunarium.Bot.DataCenter` | 应用 | 🚧 部分迁移 | 已用 Framework 的扩展与健康监控，但配置 / 日志 / 调度仍是旧实现 |
| `Lunarium.Bot.SearchBot` | 应用 | ⚠️ 旧架构 | NuGet Discord.Net 3.18.0 + 仓库内 Logger / Scheduler；仍在开发 |
| `Lunarium.Bot.Lum` | 应用 | 🧪 本机实验 | 已入库，`slnx` 中已登记；尚未迁移到 Framework |
| `Lunarium.Bot.Logger` / `Lunarium.Bot.Scheduler` | 遗留副本 | 🗑 已删除 | 2026-10-29 移除；`SearchBot` 仍引用它们，**当前无法编译**（待迁移到 NuGet） |
| `ReleaseBot` / `VoteBot` / `LunariumProxy` | 应用 | 🗑 已移出 | 2026-10-29 移出仓库：源码见提交 `97d4c89`；`LunariumProxy` 未曾入库（现位于 `~/Projects/LunariumProxy`） |
| 旧 SearchBot 副本 | 应用 | ❌ 废弃 | 仓库根目录 `LunariumBot.Net.SearchBot/`，已停止维护并移除中 |

> **命名**：本机仓库目录仍叫 `LunariumBot.Net`，但解决方案、项目、命名空间已统一为 `Lunarium.Bot`。代码里出现 `LunariumBot.Net.*` 即表示停留在停工前的旧命名。

## 2. 架构标准（Ticket 范式）

### 2.1 进程模型

所有 bot 都是 Generic Host 应用：`Program.Main` 只负责装配，业务实现在实现 `IHostedService` 的 `*BotService` 中，由 Host 管理启动、停止与优雅关机。

- `HostOptions.ShutdownTimeout = 120s`（归档、下载等长任务需要排空时间）
- `StopAsync` 必须：取消事件订阅 → 停掉后台任务 → `StopAsync` Discord 客户端 → 释放私有 DI 容器
- 全局异常兜底：`AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`

### 2.2 `Program.cs` 四段式

```csharp
public static async Task Main(string[] args)
{
    // ① 日志引导（失败时退回 Console）
    var rootLogger = LogConfigurator.Initialize();

    // ② 环境变量：代理 + Token
    var proxyUrl = ProxyUrl.FromEnvironment();
    var token = EnvVar.GetRequired("TICKET_BOT_TOKEN");

    // ③ Host 装配（只注册日志与 BotService）
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureLogging((_, logging) => logging.AddLunariumLogger(rootLogger))
        .ConfigureServices(services => services
            .Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(120))
            .AddHostedService<TicketBotService>(_ =>
                new TicketBotService(token, rootLogger, proxyUrl)))
        .Build();

    // ④ 全局异常兜底，然后 RunAsync
    await host.RunAsync();
}
```

约束：

- 主机 DI 容器**只放**日志与 `IHostedService`；业务对象一律进私有容器
- Token / 代理只从环境变量读，不写进配置文件
- 结束时 `await ((IAsyncDisposable)rootLogger).DisposeAsync()`，确保日志落盘

### 2.3 双层 DI

**主机容器**（`Program.cs`）：日志、Host 选项、BotService 本身。

**私有容器**（`*BotService.ConfigurePrivateServices`）：该 bot 的全部运行时依赖。注册顺序即释放逆序，推荐：

```csharp
new ServiceCollection()
    .AddSingleton(logger)                    // 已带 RootContext 前缀
    .AddSingleton<ConfigLoader<TConfig>>()   // 业务配置
    .AddDiscordSocketClient(proxyUrl, cfg => ...)
    .AddInteractionService()
    .AddDiscordLogAdapter()                  // 以下均为可选能力
    .AddStatusNotifier()
    .AddCommandSynchronizer()
    .AddHealthMonitor()
    .AddSessionManager()
    // ↓ 业务单例
    .AddSingleton<XxxService>()
    .BuildServiceProvider();
```

- `ConfigLoader<T>`、`CacheStore` 等被业务服务依赖的对象必须注册在业务单例**之前**
- `InteractionService` 的模块加载与交互执行都要传 `_privateServices`，保证命令模块能解析到业务服务
- 私有容器用 `BuildServiceProvider()` 创建，`StopAsync` 中显式 `DisposeAsync`

### 2.4 配置：`ConfigLoader<T>`

**文件分工**

| 文件 | 内容 | 是否入库 |
| --- | --- | --- |
| `appsettings.json` | 业务配置（部署 guild、频道、角色、阈值） | 否，只提交 `appsettings.template.json` |
| `appsettings.health.json` | 运维配置（健康监控端口、心跳） | 是（无敏感信息），另有 `.template.json` |

**读取规则**

- 严格 JSON：属性名大小写不敏感，**未知键直接报错**（防止“配了却不生效”）
- 定位顺序：`Directory.GetCurrentDirectory()` → `AppContext.BaseDirectory`，兼容 `dotnet run` 与发布后启动
- 启动时校验失败即抛 `ConfigValidationException`，**这是预期行为**，不要吞掉

**模型约束**

```csharp
public sealed class TicketConfig : IValidatableObject
{
    public DeployerConfig DeployerConfig { get; set; } = new();
    public Dictionary<ulong, GuildConfig> DeployGuildConfig { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext context) { ... }
}
```

- 模型必须 `class` + 无参构造 + 实现 `IValidatableObject`
- 校验工具：`ConfigValidation.Check` / `CheckIdList`，ID 字段加 `[SnowflakeId]`
- 业务规则写在模型自己身上，`ConfigLoader<T>` 不含任何业务判断

**写回契约**

| 成员 | 语义 |
| --- | --- |
| `Value` | 当前快照，整对象替换，读取无需加锁 |
| `TryUpdate(Action<T>)` | 以**磁盘当前内容**为基线修改 → 校验 → 原子写回（临时文件 + Move）；失败返回错误列表且文件不变 |
| `TryReload()` | 从磁盘重载；失败保留旧配置，返回 `false` |
| `OnConfigApplied(T)` | 派生类覆写，用于重建派生状态（如重设定时器、刷新频道集合） |
| `Validate(T)` | 离线校验候选配置（测试 / `--check-config`） |

- **禁止**直接修改 `ConfigLoader.Value` 上的属性——会绕过校验与写回
- 所有写回必须走 `TryUpdate`，它天然支持“手工改文件 + 运行期更新”共存

### 2.5 鉴权：`RuleEngine`

```csharp
var failure = await RuleEngine.ValidateAsync(
    Context,
    new BaseRuleSet.MaintainerBypass(deployerIds),
    new BaseRuleSet.RequireInGuild(guildIds),
    new BaseRuleSet.RequireAdminRole(adminRoleIds));

if (failure is not null)
{
    await FollowupAsync(components: failure, ephemeral: true);
    return;
}
```

- 返回 `null` 表示通过；返回 `MessageComponent` 表示失败，直接回给用户
- 顺序语义（**先命中先返回**）：`Bypass` → 放行；`Fail` → 拒绝；`Success` → 继续
- 因此顺序固定为：**绕过规则 → 环境约束 → 权限规则**
- 内置规则：`RequireAdminUser`、`RequireAdminRole`、`RequireRole`、`RequireInGuild`、`RequireInAnyGuild`、`RequireInChannel`、`RequireMaintainer`、`MaintainerBypass`
- 交互与文本消息两条路径都支持（`IAuthRule` / `IMsgBasedAuthRule`）
- 交互命令在 `BeforeExecuteAsync` 里 `DeferAsync(ephemeral: true)`，鉴权失败用 `FollowupAsync` 回复

### 2.6 交互会话：`SessionManager`

用于跨多次交互的表单 / 面板（如配置编辑器）：

- 注册：`AddSessionManager(alphabet, expiresHours)`，默认 `Alphabet.Base` + 1 小时
- 会话继承 `BaseInteractionsSession<TContext>`，实现 `Scope`、`Render()`
- `CustomId(action)` 统一生成 `scope:action:sessionId`，不要手拼
- 回调用 `HasPrefixSessionId.TryParse` 解析，并校验 `Context.User.Id` 是否为会话创建者
- 过期即失效；`RefreshExpiration` 可在有效操作后延长

### 2.7 消息与模态窗：Prefab

- `MessagePrefab`：`AnyFail` / `AnySuccess` / `AnyNotifier` / `AuthFail` / `FunctionFail` / `ConfigFail` / `OperateSuccess` / `EntryKey` / `AfterEntryKey`
- `ModalWindowPrefab`：`SingleTextField` / `MultiRoleSelectField` / `ChannelSelectField`
- 新代码使用 Component V2（`ComponentBuilderV2` + `TextDisplay`），不受 Embed 描述 4096 字符限制
- 旧项目里的 `EmbedUtils` / `GeneralEmbedBuilder` 不再扩散，改到时顺手替换

### 2.8 日志：`LogConfigurator` 与审计分层

日志配置是**每个 bot 自己的一份 `LogConfigurator.cs`**，不进 Framework（各 bot 的 sink 与上下文不同）。

- 定义 `RootContext` 常量（如 `"TicketBot"`），私有容器注册的 logger 已带此前缀
- 各服务 `ForContext("TicketService.Xxx")` 会被拼成 `RootContext.TicketService.Xxx`
- 审计分流：`AuditOnlyPrefixes`（只写 Audit）与 `SharedAuditPrefixes`（Audit + Runtime）
- 过滤器按 `StartsWith` 匹配且**区分大小写**，必须写完整路径，不能只写中间段
- 默认 sink：`Logs/Runtime`、`Logs/Audit`、`Logs/EW/Error`、`Logs/EW/Warning`、Console
- 级别可用 `FILE_LOG_LEVEL` / `CONSOLE_LOG_LEVEL` 覆盖
- 详细语义与踩坑见 [`LUNARIUM_LOGGING.md`](./LUNARIUM_LOGGING.md)

### 2.9 可选能力

| 能力 | 注册方法 | 说明 |
| --- | --- | --- |
| Discord 日志桥接 | `AddDiscordLogAdapter()` | `_client.Log += _adapter.Handle` |
| 上线 / 重连 / 审计通知 | `AddStatusNotifier()` | `Notifier.SendLoginNotificationAsync` / `ReConnect` / `SendAuditMessageTo*` / `SendKeyEntryMessageAsync` |
| 文本命令 | `AddCommandSynchronizer()` | `重新注册` / `在本服务器重新注册` |
| 健康监控 | `AddHealthMonitor()` | 读取 `appsettings.health.json`；HTTP 端口 / 心跳 |
| 交互会话 | `AddSessionManager()` | 见 §2.6 |
| LLM | `AddLunariumAi(proxyUrl, options)` | 见 §3.2；未配置 Key 时 `IsAvailable == false`，调用方需降级 |

## 3. 共享库

### 3.1 `Lunarium.Bot.Framework`（仓库内）

Bot 基础设施的唯一来源。按目录划分：

| 目录 / 类型 | 内容 |
| --- | --- |
| `Configuration/` | `ConfigLoader<T>`、`ConfigFilePaths`、`ConfigValidation`、`ConfigValidationException`、`JsonConfigOptions`、`SnowflakeIdAttribute` |
| `AuthRuleEngine/` | `RuleEngine`、`IAuthRule` / `IMsgBasedAuthRule`、`BaseRuleSet`、`ValidateResult` |
| `Interaction/` | `SessionManager`、`BaseInteractionsSession<T>`、`IInteractionSession`、`CustomId`、`HasPrefixSessionId` |
| `Health/` | `HealthMonitor`、`HealthMonitorConfig`、`HealthMonitorRemote` |
| 根目录 | `ServiceCollectionExtensions`（`AddDiscordSocketClient` / `AddInteractionService` / `AddDiscordLogAdapter` / `AddStatusNotifier` / `AddCommandSynchronizer` / `AddHealthMonitor` / `AddSessionManager`） |
| 消息 | `MessagePrefab`、`ModalWindowPrefab`、`Notifier`、`SelectMenuOption` |
| 其他 | `CommandSynchronizer`、`DiscordLogAdapter`、`RateLimiterExtensions`、`RetryPolicy`、`TypeExtensionMethod`、`OpenAPI/` |

依赖：`Lunarium.Common`、`Lunarium.Logging`、`Lunarium.Scheduler`、`Polly`、`System.Threading.RateLimiting`，以及 vendored Discord.Net（依赖其 `InternalsVisibleTo`，见 §7）。

> ⚠️ 版本收敛：Framework 目前锁定的 Common / Logging 版本比 Ticket 低。**改动 Framework 时顺带对齐**，但不要把“升级依赖”混进业务提交。

### 3.2 `Lunarium.Bot.AI`（仓库内）

无状态、单次、非流式的 LLM 推理客户端：

- 接口：`IAiClient.CompleteAsync`，以及按 JSON Schema 反序列化的扩展方法
- 实现：DeepSeek Responses API
- 注册：`services.AddLunariumAi(proxyUrl, o => { o.DefaultModel = ...; o.BaseUrl = ...; o.ApiKeyEnvironmentVariable = ...; })`
- 运行期可注入 Key：`IAiKeyConfigurator`（默认仅存内存，重启回落到环境变量）
- **明确不做**：会话历史、工具调用、MCP、流式输出、本地 token 估算

### 3.3 NuGet 基础库（独立仓库）

| 库 | 包 | 本机源码 |
| --- | --- | --- |
| Common | `Lunarium.Common` | `~/Projects/Lunarium.Common` |
| Logger | `Lunarium.Logging`、`.Hosting`、`.Configuration`、`.Http` | `~/Projects/Lunarium.Logging` |
| Scheduler | `Lunarium.Scheduler` | `~/Projects/Lunarium.Scheduler` |

- **Common**：零外部依赖、AOT 兼容。模块：`Lunarium.Common`（`Alphabet`）、`.UID`（`LocalSnowflake` / `NanoId`）、`.Debounce`（`DirtyDebouncer` / `CtsDebouncer` / 工厂）、`.Caching`（`CacheStore` / `JsonDocumentStore`）、`.Net`（`ProxyUrl` / `EnvUrl`）、`EnvVar`
- **Logger**：`Lunarium.Logging` 命名空间；`Hosting` 提供 Host / MEL 桥接，`Configuration` 提供 `appsettings.json` 热更新，`Http` 提供 Seq / Loki Sink
- **Scheduler**：`Lunarium.Scheduler`，基于 Cronos，支持 DI 创建任务、同步 / 异步任务、按时区调度

> 版本号不在本文锁定——**以各 `*.csproj` 为准**。目前各项目存在版本漂移，属于已知待收敛项。

### 3.4 遗留副本（已删除）

`src/Lunarium.Bot.Logger` 与 `src/Lunarium.Bot.Scheduler` 已在 2026-10-29 从仓库移除，由 §3.3 的 NuGet 包取代。

- 其 API 与现行 NuGet 版本**不一致**（Logger 变动最大：`Error(msg, ex)` vs `Error(ex, msg)`）
- ⚠️ `SearchBot`（Logger 29 处 + Scheduler 2 处）与 `Examples`（Logger 4 处）仍引用它们，**当前无法编译**，需先迁到 NuGet 再移除 ProjectReference

## 4. 应用项目

### 4.1 `Lunarium.Bot.Ticket`（参考实现）

工单机器人：提交入口 → 审核（可选 LLM 建议）→ 建频道 / 生命周期 → 归档（HTML + 附件 ZIP）→ 删除队列；支持自动通过、无人回复安抚、运行期设置 AI Key。

阅读顺序建议：`Program.cs` → `TicketBot.cs` → `LogConfigurator.cs` → `Model/ConfigModel.cs` → `AppInteraction/` → `TicketService/`。

### 4.2 `Lunarium.Bot.Archive`（迁移中）

论坛 / 线程生命周期管理：按上限自动归档最不活跃帖子（仅在超限时才逐帖取最后活跃时间），每轮向状态频道维护一条归档报告。已切到 Framework；旧优先归档与置顶监控均已移除，配置走 `/维护 编辑配置` 面板，全局间隔用 `/维护 重设间隔`。

### 4.3 `Lunarium.Bot.DataCenter`（部分迁移）

数据采集 / 分词 / PostgreSQL 持久化 / 活动分析。已使用 Framework 的扩展与健康监控，但仍自带 `ConfigLoader`、仓库内 Logger / Scheduler，属于迁移中间态。

### 4.4 旧架构项目

- `Lunarium.Bot.SearchBot`：搜索机器人，仍在开发，但仍是 NuGet Discord.Net 3.18.0 + 自带 `ConfigLoader` + 仓库内 Logger / Scheduler

> 原 `Lunarium.Bot.Examples` 已于 2026-10-29 移除（新项目请直接复制 `src/_template/`）。

### 4.5 冻结 / 废弃

| 项目 | 位置 | 说明 |
| --- | --- | --- |
| ReleaseBot / VoteBot / LunariumProxy | 已移出仓库（2026-10-29） | 源码可 `git checkout 97d4c89 -- <path>` 恢复；本地配置备份在 `~/Projects/Lunarium.Bot.removed/`；`LunariumProxy` 未曾入库，现位于 `~/Projects/LunariumProxy` |
| 旧 SearchBot 副本 | `LunariumBot.Net.SearchBot/` | 重组前遗留，已停止维护并移除中；现行项目是 `src/Lunarium.Bot.SearchBot` |

## 5. 新 Bot 脚手架清单

**先复制骨架** `src/_template/`（一个能 `dotnet build` 通过的最小 bot），占位符替换步骤见
[`src/_template/README.md`](./src/_template/README.md)。下面是骨架没覆盖、需要自己写的部分：

1. **csproj**（骨架已给，通常只需改项目名）
   - 首行 `<Import Project="../../build/Bot.props" />` 决定发布形态，不能删
   - `ProjectReference`：`Lunarium.Bot.Framework` + vendored Discord.Net 的 4 个项目（Core / Interactions / Rest / WebSocket）
   - `PackageReference` 不写 `Version`，由 `src/Directory.Packages.props` 统一管理；需要 LLM 时再加 `Lunarium.Bot.AI`
2. **`Program.cs`**：四段式（§2.2）
3. **`LogConfigurator.cs`**：`RootContext` + 审计前缀 + sink（§2.8）
4. **`XxxBotService.cs`**
   - 构造函数：`EnvVar token`、`ILogger`、`ProxyUrl?`
   - `ConfigurePrivateServices`：按 §2.3 的顺序注册
   - `StartAsync`：加载本地数据 → 挂事件 → `AddModulesAsync` → `LoginAsync` → `StartAsync`
   - `ReadyAsync`：首次注册命令 / 发上线通知；重连走 `Notifier.ReConnect`
   - `StopAsync`：按 §2.1 的顺序收尾
5. **配置**
   - `Model/ConfigModel.cs`：`XxxConfig` / `DeployerConfig` / `GuildConfig`，实现 `IValidatableObject`
   - `appsettings.json` + `appsettings.template.json`
   - `appsettings.health.json` + `appsettings.health.template.json`
6. **交互层**
   - 命令：`InteractionModuleBase<SocketInteractionContext>`，`BeforeExecuteAsync` 里 defer
   - 鉴权：`RuleEngine` + `BaseRuleSet`（§2.5）
   - 面板 / 表单：`SessionManager` + `BaseInteractionsSession<T>`（§2.6）
   - 消息：`MessagePrefab` / `ModalWindowPrefab`（§2.7）
7. **本地数据**：运行时数据放 `storage/` 或 `data/`，确认已加入 `.gitignore`
8. **文档**：在本文件 §1.3 状态表登记；需要时补 `README` / 运维说明

**不要做的事**：手写 `DiscordSocketClient` 构造、自己写 `AuthUtils`、在业务里直接 `File.WriteAllText` 配置、复制 `Logger` / `Scheduler` 旧副本、把业务服务塞进主机容器。

## 6. 数据持久化现状

没有统一持久层，按项目选择：

- **DataCenter**：PostgreSQL + Dapper；`DBMigrations/*.pgsql` 启动时自动迁移；schema 用触发器维护一致性
- **Ticket**：本地文件（`storage/`）+ 归档 ZIP；无数据库
- **Archive**：状态报告消息 id 落 `CacheStore`（重启后认领并原地编辑）

新项目没有强制 ORM 要求；需要关系型数据时参考 DataCenter，简单状态优先本地文件 + `CacheStore` / `JsonDocumentStore`。

## 7. vendored Discord.Net

- 版本：`3.20.2`，由 `Discord.Net.Local/discordnet.version` 锁定
- **不是 submodule**：`Discord.Net/` 被 `.gitignore` 忽略，对 git 只是一个普通目录
- 维护脚本：`Discord.Net.Local/sync-discordnet.sh`（按 tag 克隆 / 切换官方仓库，再注入 `Directory.Build.props`）
- `Directory.Build.props` 做两件事：`InternalsVisibleTo → Lunarium.Bot.Framework`、关闭强签名
- 全新 clone 后**必须**先跑一次同步脚本，否则项目无法编译；升级版本改 `discordnet.version` 后重跑
- 标准项目通过 `ProjectReference` 引用 `Discord.Net/src/*`，不走 NuGet；`SearchBot` / `Examples` 仍是 NuGet 3.18.0（见 §4.4）

## 8. 当前状态与已知不一致

> 快照：2026-10-24。架构信息以本文为准，功能进度以 `git log` 为准。

- Ticket 已建成并作为标准；Framework 完成从 Extensions 的更名与重构
- Archive 正在按 Ticket 范式改造，尚未完成
- DataCenter / SearchBot / Examples 尚未完成迁移

已知待收敛项：

1. Framework 的 Common / Logging 版本低于 Ticket，统一依赖版本
2. 仓库根目录旧 SearchBot 副本仍在清理中
3. `tests/` 已清空，暂无测试项目

## 9. 参考资料

- [`LUNARIUM_LOGGING.md`](./LUNARIUM_LOGGING.md)：日志上下文、过滤前缀、初始化顺序与审计分层
- [`Discord.Net.Local/Directory.Build.props`](./Discord.Net.Local/Directory.Build.props)：vendored Discord.Net 的注入配置
- 外部库源码：`~/Projects/Lunarium.Common`、`~/Projects/Lunarium.Logging`、`~/Projects/Lunarium.Scheduler`
