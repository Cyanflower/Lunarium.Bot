# LunariumBot.Net.Examples 开发指南

欢迎使用 `LunariumBot.Net.Examples`！这是一个功能完备、结构清晰的 Discord Bot 项目模板，旨在帮助你快速启动新项目，并遵循现代 .NET 开发的最佳实践。

本文档将指导你完成从配置、运行到添加新功能的整个流程。

## 目录

1.  [核心设计理念](#1-核心设计理念)
2.  [快速开始](#2-快速开始)
    -   [2.1. 环境准备](#21-环境准备)
    -   [2.2. 配置项目](#22-配置项目)
    -   [2.3. 运行 Bot](#23-运行-bot)
3.  [项目结构详解](#3-项目结构详解)
4.  [开发实践指南](#4-开发实践指南)
    -   [4.1. 如何添加一个新的斜杠命令](#41-如何添加一个新的斜杠命令)
    -   [4.2. 如何添加一个新的服务](#42-如何添加一个新的服务)
    -   [4.3. 理解双层依赖注入](#43-理解双层依赖注入)
    -   [4.4. 安全地修改配置](#44-安全地修改配置)
5.  [部署](#5-部署)

## 1. 核心设计理念

本模板基于以下几个核心原则构建：

-   **.NET Generic Host**: 采用标准的 .NET 托管模型，统一管理应用的生命周期、配置和依赖注入 (DI)，确保了项目的健壮性和可扩展性。
-   **`IHostedService` 模式**: Bot 的核心逻辑 (`ExamplesBotService`) 被封装为一个托管服务。主机负责在启动时调用其 `StartAsync`，在关闭时调用 `StopAsync`，实现了业务逻辑与程序启动逻辑的解耦。
-   **分层依赖注入 (DI)**: 项目包含两个DI容器：
    1.  **主机DI容器** (`Program.cs`): 负责管理应用级的全局服务，如日志系统、代理配置等。
    2.  **私有DI容器** (`ExamplesBot.cs`): 负责管理仅与 Bot 自身相关的服务，如 `DiscordSocketClient`、`InteractionService`、`ConfigLoader` 等。这种设计极大地增强了 Bot 核心逻辑的**独立性和可移植性**。
-   **配置与代码分离**:
    -   **敏感数据** (如 Bot Token) 通过**环境变量**加载，避免硬编码在代码或配置文件中，提升安全性。
    -   **业务配置** (如管理员角色ID) 通过 `appsettings.jsonc` 文件管理，便于修改和维护。
-   **专用日志库**: 集成了 `LunariumBot.Net.Logger`，提供结构化、可过滤、多目标的日志记录能力。

## 2. 快速开始

### 2.1. 环境准备

-   [.NET 8.0 SDK](https://dotnet.microsoft.com/download) 或更高版本。
-   一个 Discord Bot 应用程序及其 Token。([创建教程](https://discordpy.readthedocs.io/en/stable/discord.html))

### 2.2. 配置项目

1.  **创建配置文件**:
    在 `src/LunariumBot.Net.Examples/` 目录下，复制 `appsettings.template.jsonc` 并重命名为 `appsettings.jsonc`。

2.  **编辑 `appsettings.jsonc`**:
    ```jsonc
    {
      // 管理员鉴权配置
      "AuthConfig": {
        // 填入你的服务器中用作管理员的角色的ID
        "AdminRoleIds": [
          112233445566778899 
        ],
        // 建议创建一个频道，用于接收Bot的日志和状态报告
        "LogChannel": 998877665544332211 
      },
      // 如果你的Bot需要区分不同服务器的配置，可以在这里添加
      // 对于全局命令的Bot，此项可以忽略
      "DeployGuildConfig": {
        "123456789012345678": 1 
      }
    }
    ```

3.  **设置环境变量 (重要)**:
    环境变量是加载 Bot Token 等敏感信息的**唯一**推荐方式。

    > **⚠️ 重要安全提示：请严格区分开发与部署环境！**
    >
    > `start.sh` 脚本是为了在**生产服务器上部署和运行**而设计的。请**绝对不要**在日常开发中将你的真实 Bot Token 写入此文件。这样做极易导致 Token 意外提交到 Git 等版本控制系统，从而造成永久性泄露。
    >
    > #### **正确的开发工作流**
    >
    > 1.  **使用会话级环境变量设置 Token**:
    >     在你的开发终端中，运行以下命令来设置一个**临时的**环境变量。它只在当前终端会话中有效，关闭终端后即失效，非常安全。
    >     ```bash
    >     export EXAMPLES_BOT_TOKEN="你的开发用Bot-Token"
    >     ```
    >
    > 2.  **使用 `dotnet run` 启动项目**:
    >     直接从项目源代码启动，便于调试和快速迭代。
    >     ```bash
    >     # 在解决方案根目录运行
    >     dotnet run --project src/LunariumBot.Net.Examples
    >     # 在项目根目录运行
    >     dotnet run
    >     ```
    >
    > ---
    > 只有当你准备**正式部署**到服务器时，才应修改**服务器**上 `start.sh` 脚本中的 Token。

### 2.3. 运行 Bot

**开发环境 (推荐)**:

1.  在终端中设置会话级环境变量 (如上文所述)。
2.  在解决方案根目录运行 `dotnet run --project src/LunariumBot.Net.Examples`。
    或使用 `cd src/LunariumBot.Net.Examples` 命令进入项目根目录后运行 `dotnet run`。

**部署环境**:

1.  **编译项目**:
    在解决方案根目录打开终端，运行 `dotnet build`。

2.  **发布项目 (推荐)**:
    为了获得最佳性能和独立的运行包，请发布项目。
    ```bash
    # 在解决方案根目录运行
    dotnet publish src/LunariumBot.Net.Examples -c Release -o ./publish/ExamplesBot
    # 在项目根目录运行
    dotnet publish -c Release -o ./publish/ExamplesBot
    ```
    这会在 `./publish/ExamplesBot` 目录下生成可执行文件。

3.  **配置并启动脚本**:
    -   进入发布目录: `cd ./publish/ExamplesBot`
    -   编辑 `start.sh` 文件，**仅在此时**填入你的**生产环境** Bot Token。
    -   **首次运行**需要给脚本添加执行权限 (仅需一次):
        ```bash
        chmod +x start.sh
        ```
    -   运行 Bot:
        ```bash
        ./start.sh
        ```

如果一切顺利，你将看到日志输出，并且你的 Bot 会在 Discord 上显示为在线状态。
`start.sh` 脚本还带有**守护功能**，如果 Bot 异常崩溃，它会自动尝试重启。

## 3. 项目结构详解

-   `Program.cs`: **应用程序入口**。负责配置和启动 .NET 主机，初始化日志系统、全局DI容器，并启动 `ExamplesBotService`。
-   `ExamplesBot.cs`: **Bot 核心服务**。实现了 `IHostedService`。它包含私有DI容器，管理 `DiscordSocketClient` 的生命周期，处理 Discord 事件，并加载命令模块。
-   `ConfigLoader.cs`: **配置管理器**。一个线程安全的类，负责加载、验证、更新和保存 `appsettings.jsonc` 配置。
-   `/AppInteractionService`: **命令模块目录**。
    -   存放所有斜杠命令的模块文件。
    -   `AuthUtils.cs`: 一个权限验证的工具类，演示了如何创建和使用共享服务。
-   `/Model`: **数据模型目录**。
    -   `ConfigModel.cs`: 定义了与 `appsettings.jsonc` 对应的C#类。
    -   `ProxyConfig.cs`: 代理配置的数据模型。
    -   `e.g.DataModel.cs`: 例如: 用于业务逻辑的数据模型
-   `/Utils`: **通用工具目录**。
    -   `GeneralEmbedBuilder.cs`: 用于快速创建标准格式化 Discord Embed 的帮助类。
-   `appsettings.jsonc`: **业务配置文件**。
-   `start.sh`: **启动与守护脚本 (仅用于部署)**。

其他业务逻辑代码在项目根目录内自行组织整理文件和文件夹结构即可

## 4. 开发实践指南

### 4.1. 如何添加一个新的斜杠命令

这是最常见的开发任务。

1.  在 `/AppInteractionService` 目录下创建一个新的 C# 文件，例如 `FunCommands.cs`。

2.  编写命令模块代码。一个简单的 `ping` 命令示例如下：

    ```csharp
    // FunCommands.cs
    using Discord.Interactions;
    
    namespace LunariumBot.Net.Examples.AppInteractionService;
    
    // 所有的命令模块都必须是 public 并且继承 InteractionModuleBase
    public class FunCommands : InteractionModuleBase<SocketInteractionContext>
    {
        // [SlashCommand("命令名", "命令描述")]
        [SlashCommand("ping", "检查机器人的延迟。")]
        public async Task Ping()
        {
            // 回复消息
            await RespondAsync($"Pong! 延迟: {Context.Client.Latency}ms", ephemeral: true);
        }
    }
    ```

3.  **完成！** `ExamplesBotService` 会自动扫描并加载程序集中的所有命令模块。你只需要重新启动 Bot，新的 `/ping` 命令就会出现在 Discord 中。

**如果命令需要使用服务 (如日志或配置):**

只需通过构造函数注入即可。私有DI容器会自动提供实例。

```csharp
public class FunCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;

    // 通过构造函数注入服务
    public FunCommands(ILogger logger)
    {
        _logger = logger.ForContext("FunCommands"); // 创建上下文
    }

    [SlashCommand("ping", "检查机器人的延迟。")]
    public async Task Ping()
    {
        _logger.Info("用户 {User} 执行了 /ping 命令。", Context.User.Username);
        await RespondAsync($"Pong! 延迟: {Context.Client.Latency}ms", ephemeral: true);
    }
}
```

### 4.2. 如何添加一个新的服务

假设你需要一个服务来管理用户积分。

1.  **创建服务接口和实现**: (推荐使用接口，但非必须)
    ```csharp
    // /Services/IScoreService.cs
    public interface IScoreService { void AddScore(ulong userId, int score); }

    // /Services/ScoreService.cs
    public class ScoreService : IScoreService
    {
        private readonly ILogger _logger;
        public ScoreService(ILogger logger) { _logger = logger.ForContext("ScoreService"); }
        public void AddScore(ulong userId, int score) { /* ... 逻辑 ... */ }
    }
    ```

2.  **在私有DI容器中注册服务**:
    打开 `ExamplesBot.cs`，找到 `ConfigurePrivateServices` 方法，添加你的服务注册。

    ```csharp
    // ExamplesBot.cs -> ConfigurePrivateServices
    private IServiceProvider ConfigurePrivateServices(ProxyConfig proxyConfig, ILogger logger)
    {
        // ...
        var services = new ServiceCollection()
            // ... 其他服务
            .AddSingleton<ConfigLoader>()
            .AddSingleton<AuthUtils>()
            
            // 在这里添加你的新服务
            .AddSingleton<IScoreService, ScoreService>() // 推荐使用接口注册
            // 或者 .AddSingleton<ScoreService>()
            
            // ... Discord 客户端等
            .BuildServiceProvider();
        // ...
        return services;
    }
    ```

3.  **在命令模块中使用**:
    现在你可以在任何命令模块中通过构造函数注入 `IScoreService`。

    ```csharp
    public class GameCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IScoreService _scoreService;

        public GameCommands(IScoreService scoreService)
        {
            _scoreService = scoreService;
        }

        [SlashCommand("play", "玩游戏")]
        public async Task PlayGame()
        {
            _scoreService.AddScore(Context.User.Id, 10);
            await RespondAsync("你获得了10分！");
        }
    }
    ```

### 4.3. 理解双层依赖注入

-   **何时在 `Program.cs` 中注册?**
    当你需要一个**全局的、与 Bot 无直接关系**的服务时。例如，一个独立于 Discord 的数据库连接池或一个性能监控服务。这些服务应该在主机级别注册。

-   **何时在 `ExamplesBot.cs` 中注册?**
    当你需要一个**仅供 Bot 及其命令模块使用**的服务时。例如，`ConfigLoader`、`AuthUtils` 以及所有与 Bot 业务逻辑相关的服务。绝大多数情况下，你都应该在这里注册。

### 4.4. 安全地修改配置

`ConfigLoader` 提供了一个线程安全的方法 `UpdateConfig` 来修改并保存配置。它能确保配置在修改后仍然是有效的，如果验证失败，则会回滚所有更改。

```csharp
// 在某个管理员命令中
[SlashCommand("add-admin-role", "添加一个新的管理员角色")]
public async Task AddAdminRole(IRole role)
{
    // ... 权限检查 ...

    bool success = _configLoader.UpdateConfig(config =>
    {
        // 在这个委托中对配置的副本进行修改
        if (config.AuthConfig.AdminRoleIds.Contains(role.Id))
        {
            // 如果已存在，可以抛出异常来中断并回滚更新
            throw new InvalidOperationException("该角色已经是管理员。");
        }
        config.AuthConfig.AdminRoleIds.Add(role.Id);
    });

    if (success)
    {
        await FollowupAsync(embed: EmbedUtils.SuccessEmbed("管理员角色已成功添加。"));
    }
    else
    {
        await FollowupAsync(embed: EmbedUtils.FailedEmbed("添加失败，请检查日志。"));
    }
}
```

## 5. 部署

部署过程与本地运行类似：

1.  在你的开发机器上运行 `dotnet publish` 命令，生成发布文件。
2.  将整个发布目录（例如 `./publish/ExamplesBot`）上传到你的服务器。
3.  在服务器上，确保 `start.sh` 脚本有执行权限 (`chmod +x start.sh`)。
4.  **编辑服务器上的 `start.sh` 文件，填入你的生产环境 Token**。
5.  运行 `./start.sh` 启动 Bot。

建议使用 `screen` 或 `tmux` 等工具在后台会话中运行脚本，以防止 SSH 断开连接时 Bot 进程被终止。
