# Lunarium.Bot 项目概况

## 1. 简介

`Lunarium.Bot` 是一个基于 .NET 9.0 构建的现代化、模块化的 Discord Bot 解决方案。该项目旨在提供一个健壮、可扩展的框架，用于开发功能复杂的 Discord 机器人。整个解决方案围绕着清晰的架构原则、分层依赖注入和可重用的共享库构建，目前包含了多个功能独立的机器人应用。

该项目的最新架构标准参考 `DataCenter`、`Archive` 和 `Examples` 项目。

## 2. 核心架构原则

本项目遵循一系列现代 .NET 开发的最佳实践，以确保代码的可维护性、可测试性和可扩展性。

### 2.1. .NET Generic Host 托管模型
所有机器人应用都构建在 .NET Generic Host 之上。核心业务逻辑被封装在实现 `IHostedService` 接口的服务中（如 `DataCenterBotService`, `ArchiveBotService`），由主机统一管理其生命周期（启动、停止、优雅关机）。

### 2.2. 双层依赖注入 (DI) 模式
这是一个关键的设计模式，用于实现应用级服务和机器人核心服务的解耦：
1.  **主机 DI 容器 (`Program.cs`)**: 负责管理全局的、与具体机器人业务无关的应用级服务，例如：
    *   **日志系统** (`Lunarium.Bot.Logger`)
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
项目将通用功能抽象为可重用的类库，供所有机器人应用引用：
-   `Lunarium.Bot.Logger`: 自定义的日志框架。
-   `Lunarium.Bot.Scheduler`: 定时任务调度器。
-   `Lunarium.Bot.Extensions`: 通用的扩展方法。

## 3. 解决方案组件 (项目)

### 3.1. 应用机器人 (Executable Projects)

#### `Lunarium.Bot.DataCenter`
**职责**: 核心数据中心，负责从 Discord 收集、处理、存储和分析数据。
-   **数据采集**: 通过 `Listener` 实时监听 Discord 事件（消息、帖子创建/更新等），并通过 `Scanner` 定期全量扫描服务器数据，确保数据完整性。
-   **数据处理**: `DataProcessService` 负责处理原始数据，包括使用 `JiebaNet.Segmenter` 对中文内容进行分词以便于全文搜索。
-   **数据持久化**: 使用 `DatabaseService` 将处理后的数据存入 PostgreSQL 数据库。采用 Dapper 作为 ORM，并包含一套数据库迁移 (`DB.Migration.cs`) 逻辑。
-   **数据分析与报告**: `DataAnalysis` 服务和 `AnalysisJob` 定时任务负责生成服务器的活动报告，并通过 `DisplayEmbedBuilder` 将其格式化为 Discord Embed 消息。

#### `Lunarium.Bot.Archive`
**职责**: 一个专门的工具机器人，用于自动管理 Discord 论坛和线程的生命周期。
-   **自动归档**: `ArchiverService` 定期检查所有活跃线程，根据服务器配置的活跃线程上限，将最不活跃的帖子自动归档。
-   **置顶帖维护**: 自动维持被标记为“置顶”的帖子处于活跃状态，防止其被意外归档。
-   **优先归档**: 支持一个可配置的优先归档列表 (`PriorityArchiveDataLoader`)，可以为特定帖子设置归档权重。

#### `Lunarium.Bot.Examples`
**职责**: 一个功能完备的项目模板和最佳实践示例。
-   它完整地展示了如何遵循本解决方案的核心架构原则来构建一个新的机器人。
-   包含详细的 `README.md` 和注释，是开发新机器人的理想起点。

### 3.2. 共享库 (Class Libraries)

#### `Lunarium.Bot.Logger`
一个功能强大的自定义日志框架。
-   **异步处理**: 日志写入操作在后台任务中进行，不会阻塞主线程。
-   **结构化日志**: 支持 `{Property}` 格式的占位符，便于后续的日志分析。
-   **多目标输出 (Sink)**: 支持向控制台、轮转文件（按大小或时间）等多个目标输出，且每个目标都可独立配置。
-   **上下文与过滤**: 支持通过 `ForContext()` 创建层级化的日志上下文，并可根据上下文对 `Sink` 的输出进行精确的包含/排除过滤。

#### `Lunarium.Bot.Scheduler`
一个轻量级的、基于 Cron 表达式的定时任务调度器。
-   **DI 集成**: 任务 (`IJob`/`IAsyncJob`) 通过依赖注入容器创建，可以轻松使用项目中的其他服务。
-   **支持异步/同步任务**: 可以调度长时间运行的异步任务而不会阻塞调度器。
-   **时区支持**: 允许为每个任务指定独立的执行时区。

#### `Lunarium.Bot.Extensions`
一个小型工具库，提供通用的扩展方法，如 `string.Slice()` 用于安全截断字符串，`ToDbId()`/`ToDiscordId()` 用于 `ulong` 和 `long` 之间的安全转换。

## 4. 数据持久化 (Database)

`Lunarium.Bot.DataCenter` 项目依赖于 **PostgreSQL** 数据库。
-   **ORM**: 使用 **Dapper** 进行数据访问，提供了高性能和灵活性。
-   **数据模型**: `Models` 目录下定义了清晰的领域模型 (`DomainModels.cs`) 和数据库模型 (`DatabaseModels.cs`)，实现了业务逻辑与数据存储的解耦。
-   **数据库迁移**: 项目包含一套简单的数据库迁移系统 (`DB.Migration.cs`)，它会检查 `DBMigrations` 目录下的 `.pgsql` 脚本，并在应用启动时自动更新数据库结构到最新版本。
-   **高级特性**: 数据库 schema (`v1.initial_schema.pgsql`) 利用了 PostgreSQL 的触发器（Triggers）来自动维护数据一致性，例如自动更新标签使用次数、自动生成全文搜索向量等。

## 5. 设计模式与实践

-   **依赖注入 (DI)**: 贯穿整个解决方案，用于解耦组件。
-   **IHostedService**: 用于管理后台服务的生命周期。
-   **Builder 模式**: `LoggerBuilder` 用于流畅地配置日志系统。
-   **Partial Class**: 用于将大型服务类（如 `DataProcessService`, `DatabaseService`）按功能拆分到多个文件中，提高代码可读性。
-   **异步编程**: 广泛使用 `async/await` 和 `Task` 来处理 I/O 密集型操作，保证了应用的高性能和响应能力。

***
*注意：`SearchBot` 是一个采用旧架构的待重构项目，开发新功能或新机器人时不应作为参考。*


Discord.Net/Directory.Build.props
```
<Project>
  <!-- 
    这个文件会自动被子项目加载。
    在这里做的配置会应用到 Discord.Net 的所有项目 (Rest, WebSocket, Core 等)。
  -->

  <ItemGroup>
    <!-- 
      核心：注入 InternalsVisibleTo 属性。
      _Parameter1 是你的项目程序集名称（注意：是程序集名称，通常与 .csproj 文件名一致）。
      如果有多个项目需要访问，就复制多行。
    -->
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>Lunarium.Bot.Extensions</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <PropertyGroup>
    <!-- 
      重要：禁用强签名 (Strong Naming)。
      默认情况下 Discord.Net 是开启签名的, 如果程序集已签名, InternalsVisibleTo 必须包含公钥
    -->
    <SignAssembly>false</SignAssembly>
    <AssemblyOriginatorKeyFile></AssemblyOriginatorKeyFile>
  </PropertyGroup>

</Project>
```