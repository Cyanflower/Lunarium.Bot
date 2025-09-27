# LunariumBot.Net.Logger 开发说明文档

本文档详细介绍了如何在 `LunariumBot.Net.Examples` 模板项目或基于此模板创建的项目中配置和使用 `LunariumBot.Net.Logger` 日志库。

## 1. 核心概念

在开始之前，请先了解以下几个核心概念：

- **`ILogger`**: 日志记录器的核心接口，所有日志操作都通过它进行。
- **`LoggerBuilder`**: 用于构建和配置 `ILogger` 实例的构建器类。它采用流式接口（Fluent API），可以链式调用。
- **`Sink` (输出目标)**: 日志的最终去向。日志库支持多种 `Sink`，例如控制台（`ConsoleSink`）、文件（`RotatingFileSink`）等。一个 `ILogger` 实例可以配置多个 `Sink`，每个 `Sink` 都可以有独立的过滤规则。
- **`Context` (上下文)**: 一个用于标识日志来源的字符串，通常代表模块、类名或功能区。它支持层级结构（例如 `ArchiveBotService.CommandAuth`），是实现日志分类和过滤的关键。
- **结构化日志 (Structured Logging)**: 在调用日志操作时, 与其使用`$""`记录扁平的字符串，更应当使用参数化调用来记录带有上下文的模板消息。例如，记录 `_logger.Info("用户 {Username} 已登录", "Alice")` 而不是 `_logger.Info($"用户 Alice 已登录")`。这样做可以让日志以更结构化的形式（如 JSON）存储，便于后续的查询和分析。

## 2. 配置日志系统 (在 `Program.cs` 中)

所有日志系统的配置都在应用程序的入口点 `Program.cs` 中完成。`Examples` 项目已经提供了一个功能完备的配置模板。

### 2.1. 基本配置流程

配置一个日志记录器的典型步骤如下：

1.  **创建 `LoggerBuilder` 实例**：`new LoggerBuilder()`
2.  **设置根记录器名称**：`.LoggerName("Runtime")`
3.  **添加一个或多个 `Sink`**：使用 `.Add...Sink()` 扩展方法。
4.  **构建 `ILogger` 实例**：`.Build()`
5.  **将其注册到依赖注入容器**：`services.AddSingleton(_rootLogger)`
6.  **替换默认的日志提供程序**：`logging.AddLunariumLogger(_rootLogger)`
7.  **在程序结束时正确释放**：`await disposableLogger.DisposeAsync()`

### 2.2. 添加和配置 Sink

`LoggerBuilder` 提供了一些便捷的扩展方法来添加不同类型的 `Sink`。每个 `Sink` 都可以接受一个 `SinkConfiguration` 对象来进行详细配置。

**示例代码 (源自 `Program.cs`)**:

```csharp
_rootLogger = new LoggerBuilder()
    .LoggerName("Runtime") 

    // 添加一个按大小轮转的文件 Sink
    .AddSizedRotatingFileSink(
        logFilePath: "Logs/Runtime/Runtime.log", // 文件路径
        maxFileSizeMB: 10,  // 单文件最大 10MB
        maxFile: 5,         // 最多保留 5 个文件
        sinkConfiguration: new SinkConfiguration
        {
            // 在此 Sink 中，排除来自 "ExamplesBotService.SomeNoisyModule" 上下文的日志
            ContextFilterExcludes = ["ExamplesBotService.SomeNoisyModule"], 
            LogMinLevel = fileLogLevel // 设置最低日志级别
        })

    // 添加一个专门记录错误的 Sink
    .AddSizedRotatingFileSink(
        logFilePath: "Logs/EW/Error.log",
        maxFileSizeMB: 10,
        maxFile: 5,
        sinkConfiguration: new SinkConfiguration
        {
            LogMinLevel = LogLevel.Error // 只记录 Error 和 Critical 级别
        })
    
    // 添加一个只记录警告的 Sink
    .AddSizedRotatingFileSink(
        logFilePath: "Logs/EW/Warning.log",
        maxFileSizeMB: 10,
        maxFile: 5,
        sinkConfiguration: new SinkConfiguration
        {
            LogMinLevel = LogLevel.Warning, // 最低级别是 Warning
            LogMaxLevel = LogLevel.Warning  // 最高级别也是 Warning
        })

    // 添加一个控制台 Sink
    .AddConsoleSink(
        sinkConfiguration: new SinkConfiguration
        {
            LogMinLevel = consoleLogLevel
        })
    .Build(); 
```

### 2.3. `SinkConfiguration` 详解

`SinkConfiguration` 对象是实现日志精细化管理的核心。

- `LogMinLevel`: 此 `Sink` 处理的最低日志级别。默认为 `LogLevel.Info`。
- `LogMaxLevel`: 此 `Sink` 处理的最高日志级别。默认为 `LogLevel.Critical`。
- `ContextFilterIncludes`: **包含**列表。只有当日志的上下文**以列表中的任意一个前缀开头**时，日志才会被此 `Sink` 处理。如果此列表为 `null` 或空，则匹配所有上下文。
- `ContextFilterExcludes`: **排除**列表。如果日志的上下文**以列表中的任意一个前缀开头**时，日志将**不会**被此 `Sink` 处理。此规则优先级高于 `Includes`。

> **高级示例**: 假设你想创建一个专门记录某个模块（例如 `CommandAuth`）日志的文件，可以这样配置：
> ```csharp
> .AddSizedRotatingFileSink(
>     logFilePath: "Logs/CommandAuth/CommandAuth.log",
>     maxFile: 10,
>     sinkConfiguration: new SinkConfiguration
>     {
>         // 只包含以 "ExamplesBotService.CommandAuth" 开头的上下文
>         ContextFilterIncludes = ["ExamplesBotService.CommandAuth"], 
>         LogMinLevel = LogLevel.Info
>     })
> ```

### 2.4. 集成到 .NET 主机

为了让整个应用程序（包括像 `Discord.Net` 这样的第三方库）都使用我们的日志系统，必须将其集成到 .NET Generic Host 中。

1.  **注册为单例服务**:
    ```csharp
    services.AddSingleton(_rootLogger);
    ```
    这使得我们可以在项目的任何地方通过依赖注入获取 `ILogger` 实例。

2.  **替换默认日志提供程序**:
    ```csharp
    .ConfigureLogging((context, logging) =>
    {
        logging.AddLunariumLogger(_rootLogger);
    })
    ```
    这一步至关重要，它用 `LunariumLogger` 替换了 .NET 默认的日志系统。之后，`Discord.Net` 等库产生的日志就会自动被我们的 `Sink` 捕获和处理。

### 2.5. 安全退出和日志刷新

`LunariumLogger` 是异步处理日志的，这意味着当你调用 `_logger.Info(...)` 时，日志消息只是被放入了一个内存队列，由后台任务负责写入文件或控制台。

**为防止程序退出时丢失仍在队列中的日志**，必须在 `Program.cs` 的 `finally` 块中调用 `DisposeAsync()`。

```csharp
finally
{
    if (_rootLogger is IAsyncDisposable disposableLogger)
    {
        Console.WriteLine("⏳ 正在关闭日志系统...");
        await disposableLogger.DisposeAsync();
        Console.WriteLine("✅ 日志系统已关闭。");
    }
}
```

## 3. 在项目中使用日志

配置完成后，在项目的其他部分使用日志系统就非常简单了，核心思想是**依赖注入**和**上下文隔离**。

### 3.1. 通过构造函数注入 `ILogger`

在任何需要记录日志的类中（例如 `ExamplesBotService`, `ConfigLoader`, `AuthUtils`），通过构造函数请求一个 `ILogger` 实例。DI 容器会自动将我们在 `Program.cs` 中注册的根日志记录器注入进来。

```csharp
public class ConfigLoader
{
    private readonly ILogger _logger;

    // ILogger 通过构造函数被注入
    public ConfigLoader(ILogger logger)
    {
        // ...
    }
}
```

### 3.2. 创建和使用上下文 (`ForContext`)

在大型项目中，日志可能会从数十个不同的类和模块中涌出。如果所有日志都混在一起，那么排查问题就像是在大海里捞一根针。**上下文（Context）** 是 `LunariumLogger` 解决这一问题的核心机制。

#### 将上下文理解为 "日志的路径"

您可以将日志的上下文想象成一个文件系统的路径，例如 `C:\Services\DiscordBot\Commands`。这个路径清晰地告诉我们文件的位置。同样地，日志上下文 `ExamplesBotService.AdminCommands.ReloadConfig` 也像一个“面包屑导航”，精确地指明了这条日志信息的来源。

-   **`ExamplesBotService`**: 根目录，代表整个 Bot 核心服务。
-   **`AdminCommands`**: 子目录，代表管理员命令模块。
-   **`ReloadConfig`**: 文件名，代表具体执行的功能。

有了这样的“路径”，我们就能对日志进行前所未有的精确分类和过滤。

#### 为什么上下文至关重要？

1.  **精确的日志过滤**: 这是上下文最强大的功能。你可以根据“路径”来决定哪些日志应该被写入哪个文件。例如，在 `Program.cs` 的 `SinkConfiguration` 中：
    -   想要一个只记录所有管理员操作的审计日志？
        `ContextFilterIncludes = ["ExamplesBotService.AdminCommands"]`
    -   想要在主日志中屏蔽掉某个特别“吵闹”的模块？
        `ContextFilterExcludes = ["ExamplesBotService.NoisyModule"]`

2.  **清晰的来源识别**: 当你查看日志文件时，每一行前面的 `[ExamplesBotService.AdminCommands]` 标签会立即告诉你这条日志的“出处”，让你能迅速定位到相关的代码，极大地提高了调试效率。

3.  **促进代码模块化**: 每个类或模块只需要负责定义**自己的**那一层上下文，而无需关心它的“父目录”是什么。这使得代码更加解耦和可重用。`AdminCommands` 模块可以被用在任何地方，它总会自动地将自己的 `AdminCommands` 上下文附加到父级上下文中。

#### 如何创建和使用上下文：`.ForContext()` 方法

`.ForContext()` 是一个扩展方法，它并**不**修改原始的 `ILogger` 实例，而是创建一个新的**包装器（Wrapper）**。这个包装器会记住你提供的上下文，并在记录日志时自动将其添加到“路径”的末尾。

**标准使用模式：**

1.  **在构造函数中注入 `ILogger`**:
    这是获取“父级”日志记录器的标准方式。DI容器会提供一个已经带有上层上下文的 `ILogger` 实例。

2.  **在构造函数中创建特定于当前类的 `ILogger`**:
    这是最佳实践。每个类都应该在初始化时，从注入的 `logger` 创建一个属于自己的、更具体的 `logger`。

**示例：上下文的逐层深化**

让我们跟踪一下日志上下文是如何从 `Program.cs` 一步步传递并深化的。

**第1层: `Program.cs` - 根记录器**
这里创建的是最顶层的 `ILogger`，它没有初始上下文。

```csharp
// Program.cs
_rootLogger = new LoggerBuilder().LoggerName("Runtime")...Build();
services.AddSingleton(_rootLogger);
```

**第2层: `ExamplesBot.cs` - 服务级上下文**
`ExamplesBotService` 从DI容器获取根记录器，并为其添加第一层上下文。

```csharp
// ExamplesBot.cs
public class ExamplesBotService : IHostedService
{
    private readonly ILogger _logger; // 这个 logger 的上下文是 "ExamplesBotService"
    
    public ExamplesBotService(ILogger runtimeRootLogger, ...)
    {
        // 从根记录器创建服务级的上下文
        _logger = runtimeRootLogger.ForContext("ExamplesBotService");
        
        // 当创建私有服务容器时，这个带有 "ExamplesBotService" 上下文的 _logger 被注册进去
        _privateServices = ConfigurePrivateServices(proxyConfig, _logger);
    }
}
```

**第3层: `AdminCommands.cs` - 模块级上下文**
`AdminCommands` 模块由 `_privateServices` 这个DI容器创建。因此，它注入的 `ILogger` 实例就是上面那个已经带有 `ExamplesBotService` 上下文的记录器。

```csharp
// AppInteractionService/AdminCommands.cs
[Group("admin", "管理员专属配置命令")]
public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger; // 这个 logger 的上下文是 "ExamplesBotService.AdminCommands"

    public AdminCommands(ILogger logger, ...) // 注入的是带有 "ExamplesBotService" 上下文的 logger
    {
        // 在父级上下文的基础上，添加 "AdminCommands" 这一层
        _logger = logger.ForContext("AdminCommands");
    }

    [SlashCommand("reload-config", "从文件重载配置")]
    public async Task ReloadConfig()
    {
        _logger.Info("用户 {User} 正在执行 'reload-config' 命令。", Context.User.Username);
        // => 日志输出: [ExamplesBotService.AdminCommands] 用户 Lunarium 执行了 'reload-config' 命令。
    }
}
```

**第4层 (可选): `ReloadConfig` - 临时操作级上下文**
你甚至可以在方法内部创建更临时的上下文，用于追踪一个具体的操作流程。

```csharp
// 在 ReloadConfig 方法内部
public async Task ReloadConfig()
{
    // 创建一个仅用于本次命令执行的、带有唯一ID的 logger
    var operationLogger = _logger.ForContext($"Operation.{Context.InteractionId}");
    
    operationLogger.Info("操作开始。");
    // => 日志输出: [ExamplesBotService.AdminCommands.Operation.987654321] 操作开始。
    
    try
    {
        _configLoader.ReloadConfig();
        operationLogger.Info("配置重载成功。");
        // => 日志输出: [ExamplesBotService.AdminCommands.Operation.987654321] 配置重载成功。
    }
    catch (Exception ex)
    {
        operationLogger.Error("配置重载失败。", ex);
        // => 日志输出: [ExamplesBotService.AdminCommands.Operation.987654321] 配置重载失败。
    }
}
```

通过这种方式，你可以精确地筛选出某一次特定命令执行过程中的所有日志，这对于调试复杂或并发的业务流程非常有帮助。

### 3.3. 多层级上下文与日志隔离 (Chaining Contexts)

`.ForContext()` 是可以**链式调用**的。每次调用都会在现有上下文的基础上追加一个新的层级，使用 `.` 分隔。这允许我们创建非常精细的日志流。

假设我们有一个处理斜杠命令的模块，它本身也需要记录日志。

**`ExamplesBot.cs` 中的日志上下文:**

```csharp
public class ExamplesBotService : IHostedService
{
    private readonly ILogger _logger;
    
    public ExamplesBotService(string token, ILogger runtimeRootLogger, ProxyConfig proxyConfig)
    {
        // ...
        // Bot 服务的根上下文是 "ExamplesBotService"
        _logger = runtimeRootLogger.ForContext("ExamplesBotService");
        
        // 当创建私有服务容器时，这个带有上下文的 _logger 被注册
        _privateServices = ConfigurePrivateServices(proxyConfig, _logger);
        // ...
    }
}
```

**`AdminCommands.cs` (斜杠命令模块) 中的日志上下文:**

```csharp
[Group("admin", "管理员专属配置命令")]
public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger; // 这是更具体的记录器

    // 当 DI 容器创建 AdminCommands 实例时，
    // 它注入的是上面 ExamplesBotService 注册的那个 logger
    public AdminCommands(ILogger logger, /*...其他服务...*/)
    {
        // 在父级上下文 "ExamplesBotService" 的基础上，
        // 我们添加了 "AdminCommands" 这一层，实现了上下文的深化。
        _logger = logger.ForContext("AdminCommands");
    }

    [SlashCommand("reload-config", "从文件重载配置")]
    public async Task ReloadConfig()
    {
        _logger.Info("用户 {User} 正在执行 'reload-config' 命令。", Context.User.Username);
    }
}
```

**结果分析**:

当 `ReloadConfig` 命令被执行时，记录的日志最终上下文将是 `ExamplesBotService.AdminCommands`。

这种模式带来了极高的灵活性：

-   **精细过滤**: 你可以配置一个 `Sink` 只捕获 `ExamplesBotService.AdminCommands` 的日志，用于审计所有管理员操作，而不会被其他日志干扰。
-   **清晰的日志流**: 在阅读大型日志文件时，层级化的上下文 `A.B.C` 可以让你快速定位到问题的具体模块（`A`）、子模块（`B`）和功能区域（`C`）。
-   **代码解耦**: `AdminCommands` 模块不需要关心它的父级上下文是什么，它只负责添加自己的上下文层级，保持了模块的独立性和可重用性。

### 3.4. 记录不同级别的日志

`ILogger` 接口提供了一系列便捷方法来记录不同级别的日志。

```csharp
_logger.Debug("这是一条用于调试的详细信息。");
_logger.Info("服务已成功启动。");
_logger.Warning("配置项 'SomeValue' 未找到，使用默认值。");
_logger.Error("无法连接到数据库。");
_logger.Critical("关键服务崩溃，应用程序可能无法继续运行。");
```

### 3.5. 使用结构化日志

为了充分利用日志库的优势，请始终使用结构化日志格式。

- **推荐**:
  ```csharp
  var userId = 123;
  var ipAddress = "192.168.1.1";
  _logger.Info("用户 {UserId} 从 {Ip} 登录成功。", userId, ipAddress);
  ```
- **不推荐**:
  ```csharp
  var userId = 123;
  var ipAddress = "192.168.1.1";
  _logger.Info($"用户 {userId} 从 {ipAddress} 登录成功。");
  ```

结构化日志的占位符 `{}` 中的名称（如 `UserId`）会成为 JSON 日志中的一个字段名，极大地增强了日志的可查询性。

### 3.6. 记录异常

`Error` 和 `Critical` 方法有专门的重载来接收 `Exception` 对象。

```csharp
try
{
    // ... 可能会抛出异常的代码 ...
}
catch (Exception ex)
{
    // 记录异常，并附加上下文消息
    _logger.Error("处理用户请求时发生错误", ex);

    // 或者只记录异常本身
    _logger.Error(ex);
}
```

当记录异常时，日志库会自动捕获完整的异常信息，包括类型、消息、堆栈跟踪以及所有内部异常（InnerException），并将其格式化到日志中。

---
**总结**: `LunariumBot.Net.Logger` 提供了一个强大且灵活的日志记录框架。通过在 `Program.cs` 中进行一次性配置，并在代码中遵循**依赖注入**和**上下文隔离**的最佳实践，您可以轻松实现对应用程序日志的全面控制。