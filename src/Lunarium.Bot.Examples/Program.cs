// -----------------------------------------------------------------------------
// 文件: Program.cs
// 描述: 应用程序的主入口点。
//       此文件负责初始化和配置所有核心服务，包括日志系统、依赖注入容器、
//       配置加载以及后台服务的启动。它遵循 .NET Generic Host 模式，
//       确保了应用程序的健壮性和可扩展性。
// -----------------------------------------------------------------------------

// NuGet 包依赖
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 解决方案内引用项目
using Lunarium.Bot.Logger;
using Lunarium.Bot.Logger.Extensions;

// 项目内命名空间 (请确保创建 Model 文件夹及相应文件)
using Lunarium.Bot.Examples.Model;

namespace Lunarium.Bot.Examples;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 声明一个可空的根日志记录器实例。
        // 它在 try 块外部声明，以便在 catch 和 finally 块中都能访问。
        ILogger? _rootLogger = null;

        try
        {
            #region 日志系统配置
            // --- 步骤 1: 配置日志系统 ---
            // 通过读取环境变量来动态设置不同日志目标的最低日志级别。
            // 这使得在不同部署环境（如开发、生产）中可以灵活调整日志详细程度，而无需修改代码。

            // 尝试从环境变量 "FILE_LOG_LEVEL" 解析文件日志级别，默认为 Info。
            var envFileLogLevel = Environment.GetEnvironmentVariable("FILE_LOG_LEVEL");
            var fileLogLevel = LogLevel.Info; // 默认值
            if (!string.IsNullOrEmpty(envFileLogLevel) &&
                Enum.TryParse<LogLevel>(envFileLogLevel, true, out var parsedFileLevel))
            {
                fileLogLevel = parsedFileLevel;
            }

            // 尝试从环境变量 "CONSOLE_LOG_LEVEL" 解析控制台日志级别，默认为 Info。
            var envConsoleLogLevel = Environment.GetEnvironmentVariable("CONSOLE_LOG_LEVEL");
            var consoleLogLevel = LogLevel.Info; // 默认值
            if (!string.IsNullOrEmpty(envConsoleLogLevel) &&
                Enum.TryParse<LogLevel>(envConsoleLogLevel, true, out var parsedConsoleLevel))
            {
                consoleLogLevel = parsedConsoleLevel;
            }

            // 使用 LoggerBuilder 构建日志记录器实例。
            // 这是一个采用流式接口（Fluent API）的设计，可以链式调用来配置多个日志输出目标（Sink）。
            _rootLogger = new LoggerBuilder()
                .LoggerName("Runtime") // 设置根日志记录器的名称

                // 配置一个用于常规运行时日志的文件 Sink。
                // 它会根据文件大小进行轮转，防止单个日志文件过大。
                .AddSizedRotatingFileSink(
                    logFilePath: "Logs/Runtime/Runtime.log", // 日志文件路径
                    maxFileSizeMB: 10,  // 单个文件最大 10MB
                    maxFile: 5,         // 最多保留 5 个轮转文件
                    sinkConfiguration: new SinkConfiguration
                    {
                        // 通过上下文过滤器排除某些来源的日志，例如过于频繁的日志源
                        ContextFilterExcludes = ["ExamplesBotService.SomeNoisyModule"],
                        LogMinLevel = fileLogLevel // 应用从环境变量读取的最低日志级别
                    })

                // 配置一个专门用于记录错误的日志文件 Sink。
                // 这样可以方便地隔离和审查所有错误信息。
                .AddSizedRotatingFileSink(
                    logFilePath: "Logs/EW/Error.log",
                    maxFileSizeMB: 10,
                    maxFile: 5,
                    sinkConfiguration: new SinkConfiguration
                    {
                        LogMinLevel = LogLevel.Error // 此 Sink 只记录 Error 及以上级别的日志
                    })

                // 配置一个专门用于记录警告的日志文件 Sink。
                .AddSizedRotatingFileSink(
                    logFilePath: "Logs/EW/Warning.log",
                    maxFileSizeMB: 10,
                    maxFile: 5,
                    sinkConfiguration: new SinkConfiguration
                    {
                        // 精确控制只记录 Warning 级别
                        LogMinLevel = LogLevel.Warning,
                        LogMaxLevel = LogLevel.Warning
                    })

                // 配置控制台输出 Sink，用于在开发和调试时实时查看日志。
                .AddConsoleSink(
                    sinkConfiguration: new SinkConfiguration
                    {
                        LogMinLevel = consoleLogLevel // 应用从环境变量读取的最低日志级别
                    })
                .Build(); // 完成配置并构建日志记录器

            // 创建一个带有 "Main" 上下文的日志记录器，用于记录启动过程中的信息。
            ILogger _mainLogger = _rootLogger.ForContext("Main");
            #endregion

            #region 代理配置
            // --- 步骤 2: 配置网络代理 ---
            // 从环境变量中读取代理设置，这对于在需要代理的网络环境中运行至关重要。
            var proxyUrl = Environment.GetEnvironmentVariable("http_proxy") ?? Environment.GetEnvironmentVariable("HTTP_PROXY");
            var proxyConfig = new ProxyConfig { };
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                proxyConfig.Enable = true;
                proxyConfig.ProxyUrl = proxyUrl;
            }
            else
            {
                proxyConfig.Enable = false;
                proxyConfig.ProxyUrl = "";
            }
            #endregion

            #region 机器人 Token 读取
            // --- 步骤 3: 读取敏感配置 (Token) ---
            // 从环境变量中获取 Bot 的 Token。
            var token = Environment.GetEnvironmentVariable("Examples_BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                var ex = new InvalidOperationException("关键配置缺失: 环境变量 Examples_BOT_TOKEN 未设置！");
                _mainLogger.Critical(ex.Message, ex);
                throw ex;
            }
            #endregion

            #region 主机服务配置 (Host Builder)
            // --- 步骤 4: 配置和构建主机 (Host) ---
            // 使用 .NET Generic Host 来组织和管理应用程序的生命周期。
            var host = Host.CreateDefaultBuilder(args)
                // 配置日志系统集成
                .ConfigureLogging((context, logging) =>
                {
                    // 将默认的 Microsoft.Extensions.Logging 提供程序替换为自定义的 LunariumLogger。
                    // 这样，所有使用 ILogger<T> 的库（如 Discord.Net）的日志都会被重定向到 LunariumLogger 系统中。
                    logging.AddLunariumLogger(_rootLogger);
                })
                // 配置服务依赖注入 (DI)
                .ConfigureServices((services) =>
                {
                    services
                        // 配置主机的关闭超时时间，确保有足够的时间进行清理工作。
                        .Configure<HostOptions>(opts =>
                        {
                            opts.ShutdownTimeout = TimeSpan.FromSeconds(120);
                        })
                        // 将代理配置注册为单例服务，使其可在整个应用程序中被注入和使用。
                        .AddSingleton(proxyConfig)
                        // 将根日志记录器注册为单例服务。
                        .AddSingleton(_rootLogger)
                        // 将主要的 Bot 逻辑注册为一个托管服务 (Hosted Service)。
                        // Host 会负责在启动时调用其 StartAsync，在关闭时调用 StopAsync。
                        .AddHostedService<ExamplesBotService>(provider =>
                        {
                            // 此处是工厂模式，用于创建 ExamplesBotService 实例。
                            // 它从 DI 容器中解析出所需的依赖项（如日志和代理配置）。
                            var logger = provider.GetRequiredService<ILogger>();
                            var proxy = provider.GetRequiredService<ProxyConfig>();
                            // 将依赖项传递给服务的构造函数。
                            return new ExamplesBotService(token, logger, proxy);
                        });
                })
                .Build(); // 构建主机实例
            #endregion

            #region 启动前准备与全局异常处理
            _mainLogger.Info("🚀 服务启动中...");

            if (proxyConfig.Enable)
            {
                _mainLogger.Info("🔗 代理已启用: {ProxyUrl}", proxyConfig.ProxyUrl);
            }
            else
            {
                _mainLogger.Info("🔗 代理未启用");
            }

            // --- 步骤 5: 注册全局异常处理器 ---
            // 捕获所有未在代码中显式处理的异常，防止程序意外崩溃。
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                _mainLogger.Error("💥 发生未捕获的致命异常", e.ExceptionObject as Exception);
            };
            // 捕获在 Task 中未被观察到的异常。
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                _mainLogger.Error("⚠️ 检测到未观察的任务异常", e.Exception);
                e.SetObserved(); // 将异常标记为已处理，防止进程终止。
            };
            #endregion

            #region 启动服务主机
            // --- 步骤 6: 运行主机 ---
            // 此方法会启动所有已注册的 IHostedService，并阻塞当前线程直到应用程序收到关闭信号。
            await host.RunAsync();

            // 设置退出代码为 0，表示正常退出。
            Environment.ExitCode = 0;
            #endregion
        }
        catch (Exception ex)
        {
            // 捕获在启动过程中发生的任何严重错误。
            // 如果日志系统已成功初始化，则使用它来记录错误；否则，回退到标准控制台输出。
            if (_rootLogger != null)
            {
                _rootLogger.ForContext("Program").Critical("💥 服务启动失败", ex);
            }
            else
            {
                Console.WriteLine($"💥 服务启动失败: {ex}");
            }
            // 设置退出代码为 1，表示异常退出。
            Environment.ExitCode = 1;
        }
        finally
        {
            // --- 步骤 7: 停机和资源释放 ---
            // finally 块确保无论程序是正常退出还是因异常终止，都会执行此处的清理代码。
            if (_rootLogger is IAsyncDisposable disposableLogger)
            {
                // LunariumLogger 是异步处理日志的，在程序退出前必须调用 DisposeAsync。
                // 这会确保所有在内存缓冲区中的日志都被完全写入到文件或其他目标，防止日志丢失。
                Console.WriteLine("⏳ 正在关闭日志系统，请稍候...");
                await disposableLogger.DisposeAsync();
                Console.WriteLine("✅ 日志系统已成功关闭。");
            }
        }
    }
}