# Lunarium.Bot 项目概况

## 1. 简介

`Lunarium.Bot` 是一个基于 .NET 10.0 构建的现代化、模块化的 Discord Bot 解决方案。该项目旨在提供一个健壮、可扩展的框架，用于开发功能复杂的 Discord 机器人。整个解决方案围绕着清晰的架构原则、分层依赖注入和可重用的共享库构建，目前包含了多个功能独立的机器人应用。

该项目的最新架构标准参考 `DataCenter`、`Archive` 和 `Extensions` 项目（`Examples` 尚未同步到该标准，见 §3.1）。

> **命名提示**：本机仓库目录仍沿用旧名 `LunariumBot.Net`，但解决方案名、项目名与命名空间已统一为 `Lunarium.Bot`。如果代码里出现 `LunariumBot.Net.*`，说明该处停留在停工前的旧命名。

## 2. 核心架构原则

本项目遵循一系列现代 .NET 开发的最佳实践，以确保代码的可维护性、可测试性和可扩展性。

### 2.1. .NET Generic Host 托管模型
所有机器人应用都构建在 .NET Generic Host 之上。核心业务逻辑被封装在实现 `IHostedService` 接口的服务中（如 `DataCenterBotService`, `ArchiveBotService`），由主机统一管理其生命周期（启动、停止、优雅关机）。

### 2.2. 双层依赖注入 (DI) 模式
这是一个关键的设计模式，用于实现应用级服务和机器人核心服务的解耦：
1.  **主机 DI 容器 (`Program.cs`)**: 负责管理全局的、与具体机器人业务无关的应用级服务，例如：
    *   **日志系统**（外部 NuGet 包 `Lunarium.Logging`，见 §3.3）
    *   **网络代理配置** (`ProxyConfig`)
2.  **私有 DI 容器 (`*BotService.cs`)**: 每个机器人服务内部都维护一个自己的 DI 容器。它负责管理仅与该机器人相关的服务，例如：
    *   `DiscordSocketClient` 和 `InteractionService`
    *   配置加载器 (`ConfigLoader`)
    *   业务逻辑服务 (如 `DataProcessService`, `ArchiverService`)
    *   命令模块和工具类 (如 `AuthUtils`)

这种模式使得每个机器人的核心逻辑高度内聚和独立，可以轻松地被移植或复用。

### 2.3. 配置分离
-   **敏感数据**: Bot Token 和代理地址等敏感信息通过**环境变量**加载，遵循十二要素应用（Twelve-Factor App）原则，增强了安全性。
-   **业务配置**: 机器人的行为配置（如管理员角色 ID、部署的服务器列表等）通过 `appsettings.jsonc` 文件管理，支持注释，便于修改和维护。

### 2.4. 共享核心库

**已抽离为独立项目**（独立仓库 + 独立发布 NuGet，源码不在本仓库，详见 §3.3）：
-   `Lunarium.Logging`: 结构化日志框架（`LogLevel` / `ILogger` / `LoggerBuilder` / `GlobalConfigurator`）。
-   `Lunarium.Scheduler`: 基于 Cron 表达式的定时任务调度器。

**仍在本仓库维护**：
-   `Lunarium.Bot.Extensions`: 通用扩展与基础工具，详见 §3.4。

> ⚠️ 仓库内还留着 `src/Lunarium.Bot.Logger` 与 `src/Lunarium.Bot.Scheduler` 两个**停工前的遗留副本**，已被上述独立项目取代。**不要修改它们，也不要照着它们写代码**——其 API 与现行 NuGet 版本已不一致（Logger 变动最大）。详见 §3.5。

### 2.5. 本地 vendored Discord.Net

本项目**不通过 NuGet 引用 Discord.Net**，而是在仓库内维护一份官方源码的检出：`./Discord.Net`（当前 3.20.2）。
-   它**不是 submodule**：父仓库不记录 gitlink，`Discord.Net/` 已在 `.gitignore` 中忽略——对 git 来说它就是一个普通文件夹。
-   其内容由 `Discord.Net.Local/sync-discordnet.sh` 维护：按 `Discord.Net.Local/discordnet.version` 里的 tag 克隆/切换官方仓库（`github.com/discord-net/Discord.Net`，零本地改动），再把 `Discord.Net.Local/Directory.Build.props` 注入到检出根目录。
-   该 props 注入 `InternalsVisibleTo → Lunarium.Bot.Extensions` 并关闭强签名，因此 `Lunarium.Bot.Extensions` 可以直接调用 Discord.Net 的内部 API（这也是 Extensions 与 Discord.Net 强耦合的原因）。
-   `src` 下各项目通过 `ProjectReference` 引用 `Discord.Net/src/...`。

> ⚠️ 全新 clone 之后必须先跑一次 `Discord.Net.Local/sync-discordnet.sh`，否则 `Discord.Net/` 不存在，项目无法编译。升级版本的做法：改 `discordnet.version`，再跑一次脚本。

## 3. 解决方案组件 (项目)

### 3.1. 活跃的应用机器人 (Executable Projects)

#### `Lunarium.Bot.DataCenter`
**职责**: 核心数据中心，负责从 Discord 收集、处理、存储和分析数据。
-   **数据采集**: 通过 `Listener` 实时监听 Discord 事件（消息、帖子创建/更新等），并通过 `Scanner` 定期全量扫描服务器数据，确保数据完整性。
-   **数据处理**: `DataProcessService` 负责处理原始数据，包括使用 `JiebaNet.Segmenter` 对中文内容进行分词以便于全文搜索。
-   **数据持久化**: 使用 `DatabaseService` 将处理后的数据存入 PostgreSQL 数据库。采用 Dapper 作为 ORM，并包含一套数据库迁移 (`DB.Migration.cs`) 逻辑。
-   **数据分析与报告**: `DataAnalysis` 服务和 `AnalysisJob` 定时任务负责生成服务器的活动报告，并通过 `DisplayEmbedBuilder` 将其格式化为 Discord Embed 消息。
-   **迁移状态**: 仍在使用仓库内的 Logger / Scheduler，正在迁移到 NuGet 版本（见 §5）。

#### `Lunarium.Bot.Archive`
**职责**: 一个专门的工具机器人，用于自动管理 Discord 论坛和线程的生命周期。
-   **自动归档**: `ArchiverService` 定期检查所有活跃线程，根据服务器配置的活跃线程上限，将最不活跃的帖子自动归档。
-   **置顶帖维护**: 自动维持被标记为“置顶”的帖子处于活跃状态，防止其被意外归档。
-   **优先归档**: 支持一个可配置的优先归档列表 (`PriorityArchiveDataLoader`)，可以为特定帖子设置归档权重。
-   **迁移状态**: 已完成，使用 NuGet `Lunarium.Logging`（无调度需求，不涉及 Scheduler）。

#### `Lunarium.Bot.SearchBot`
**职责**: 搜索机器人，采用**旧架构**（未按 §2.2 双层 DI 标准组织），但属于继续开发的项目。
-   仍在引用仓库内的 Logger / Scheduler / Extensions。
-   ⚠️ 仓库根目录下的 `LunariumBot.Net.SearchBot/` 是另一份**已废弃的旧副本**（见 §3.2），不要与 `src/Lunarium.Bot.SearchBot` 混淆。

#### `Lunarium.Bot.Examples`
**职责**: 示例项目。
-   ⚠️ **尚未同步到当前架构标准**：仍引用仓库内 Logger、Discord.Net 仍用 NuGet 包（3.18.0）而非 vendored 版本，因此目前**不适合作为"最佳实践模板"**。待其迁移后再恢复模板定位。

### 3.2. 冻结项目（不再开发，请勿参考）

以下项目**已冻结**：不打算继续开发，保留在仓库中仅作存档。请不要在其上叠加功能，也不要参照其架构——它们大多停留在旧标准（仓库内 Logger/Scheduler、Discord.Net 走 NuGet 包、命名空间仍为 `LunariumBot.Net.*`）。

| 项目 | 位置 | 说明 |
| --- | --- | --- |
| ReleaseBot | `src/Lunarium.Bot.ReleaseBot` | 发布助手机器人；2025-12 起无改动，已从解决方案中注释掉 |
| VoteBot | `src/Lunarium.Bot.VoteBot` | 投票机器人；2026-03 创建后再无改动，已从解决方案中注释掉 |
| LunariumProxy | `src/LunariumProxy` | Blazor + YARP 的代理/面板实验；**未纳入 git 跟踪**（见 `.gitignore`），已从解决方案中注释掉 |
| 旧 SearchBot 副本 | `LunariumBot.Net.SearchBot/`（仓库根目录） | 重组前遗留的独立副本，命名空间与依赖仍指向**已不存在**的 `LunariumBot.Net.Logger/Scheduler`，无法编译；现行项目是 `src/Lunarium.Bot.SearchBot` |

### 3.3. 外部基础库（独立仓库 + NuGet）

这两个库已从本仓库抽离，各自维护独立仓库并发布 NuGet 包。**需要查阅或修改它们的实现时，请前往本机源码目录，而不是仓库内的遗留副本。**

| 库 | NuGet 包 | 版本 | 本机源码位置 |
| --- | --- | --- | --- |
| Logger | `Lunarium.Logging`、`Lunarium.Logging.Hosting`、`Lunarium.Logging.Configuration`、`Lunarium.Logging.Http` | 1.0.3 | `~/Projects/Lunarium.Logging` |
| Scheduler | `Lunarium.Scheduler` | 1.0.0 | `~/Projects/Lunarium.Scheduler` |

-   **Logger**: 目标框架 `net8.0;net9.0;net10.0`。命名空间为 `Lunarium.Logging`（旧仓库内版本是 `Lunarium.Bot.Logger`）。除核心包外均为可选扩展包：`Hosting` 提供 `IHostBuilder`/`ILoggingBuilder` 集成与 MEL 桥接，`Configuration` 提供 `appsettings.json` 热更新，`Http` 提供 Seq/Loki 等批量推送 Sink。
-   **Scheduler**: 命名空间 `Lunarium.Scheduler`（旧仓库内版本是 `Lunarium.Bot.Scheduler`）。基于 Cronos 实现，支持 DI 创建任务、同步/异步任务与按时区调度。

### 3.4. 仓库内共享库

#### `Lunarium.Bot.Extensions`
通用扩展与基础工具库，目前包含：
-   基础工具：`HealthMonitoring`、`Debounce`、`CacheStore` 等。
-   面向 Discord.Net 的扩展（借助 §2.5 的 `InternalsVisibleTo` 访问其内部 API）。
-   日志依赖为 NuGet `Lunarium.Logging`。

### 3.5. 遗留副本（勿参考，待删除）

#### `src/Lunarium.Bot.Logger` / `src/Lunarium.Bot.Scheduler`
停工前留在仓库内的旧版本，已被 §3.3 的独立项目取代，且已从解决方案中注释掉。

> ⚠️ 它们的 API 与现行 NuGet 版本**已不一致**（Logger 变动最大），照着它们写代码会出错。不要修改、不要新增对它们的引用。

当前仍引用它们的项目：`DataCenter`、`SearchBot`、`Examples`（另 `ReleaseBot` / `VoteBot` / `LunariumProxy` 已冻结）。待这些活跃项目全部迁移到 NuGet 之后，这两个目录会被删除。

## 4. 数据持久化 (Database)

`Lunarium.Bot.DataCenter` 项目依赖于 **PostgreSQL** 数据库。
-   **ORM**: 使用 **Dapper** 进行数据访问，提供了高性能和灵活性。
-   **数据模型**: `Models` 目录下定义了清晰的领域模型 (`DomainModels.cs`) 和数据库模型 (`DatabaseModels.cs`)，实现了业务逻辑与数据存储的解耦。
-   **数据库迁移**: 项目包含一套简单的数据库迁移系统 (`DB.Migration.cs`)，它会检查 `DBMigrations` 目录下的 `.pgsql` 脚本，并在应用启动时自动更新数据库结构到最新版本。
-   **高级特性**: 数据库 schema (`v1.initial_schema.pgsql`) 利用了 PostgreSQL 的触发器（Triggers）来自动维护数据一致性，例如自动更新标签使用次数、自动生成全文搜索向量等。

## 5. 当前状态与规划

> 快照时间：2026-10-13。此节**容易过期**，如需权威信息请以 `git log` 为准。

项目约在 2026 年初停工，2026-10 起复工。两个基础库（`Lunarium.Logging`、`Lunarium.Scheduler`）的抽离发生在停工期间，早于本次复工。

本次复工的进度与规划：

1.  **Extensions** — 新增基础工具（`HealthMonitoring` / `Debounce` / `CacheStore` 等）；日志依赖由仓库内 Logger 改为 NuGet `Lunarium.Logging`。**已完成**
2.  **Archive** — 借助 Extensions 的新工具修复若干问题；日志切换为 NuGet `Lunarium.Logging`（无调度需求，不涉及 Scheduler）。**已完成**
3.  **DataCenter** —（下一步）① 将 Logger 换成 NuGet `Lunarium.Logging`，将 Scheduler 换成 NuGet `Lunarium.Scheduler`；② 优化业务上 scan 逻辑的性能。
4.  **SearchBot** — 继续开发。

因此读代码时请注意：`Extensions` 与 `Archive` 已是"新标准"，`DataCenter` 正在迁移中，其余项目尚未迁移。

## 6. 设计模式与实践

-   **依赖注入 (DI)**: 贯穿整个解决方案，用于解耦组件。
-   **IHostedService**: 用于管理后台服务的生命周期。
-   **Builder 模式**: `LoggerBuilder`（来自外部 NuGet 包 `Lunarium.Logging`）用于流畅地配置日志系统。
-   **Partial Class**: 用于将大型服务类（如 `DataProcessService`, `DatabaseService`）按功能拆分到多个文件中，提高代码可读性。
-   **异步编程**: 广泛使用 `async/await` 和 `Task` 来处理 I/O 密集型操作，保证了应用的高性能和响应能力。

***

**速记**：活跃开发请先看 `src/Lunarium.Bot.DataCenter` 与 `src/Lunarium.Bot.Archive`；日志/调度一律使用 NuGet 的 `Lunarium.Logging` / `Lunarium.Scheduler`；`src/Lunarium.Bot.Logger`、`src/Lunarium.Bot.Scheduler` 是过期副本；`src/Lunarium.Bot.ReleaseBot`、`src/Lunarium.Bot.VoteBot`、`src/LunariumProxy`、仓库根目录 `LunariumBot.Net.SearchBot/` 均为冻结项目。
