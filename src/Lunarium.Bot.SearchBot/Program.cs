// NuGet 包依赖
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 解决方案内引用项目
using Lunarium.Bot.Logger;
using Lunarium.Bot.Logger.Extensions;

// 项目内命名空间
using Lunarium.Bot.SearchBot.Models;

namespace Lunarium.Bot.SearchBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        // AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        ILogger? _rootLogger = null;

        try
        {
            // === 配置日志 ===
            // 读取环境变量
            var envFileLogLevel = Environment.GetEnvironmentVariable("FILE_LOG_LEVEL");
            var fileLogLevel = LogLevel.Info;
            if (!string.IsNullOrEmpty(envFileLogLevel) && 
                Enum.TryParse<LogLevel>(envFileLogLevel, true, out var parsedFileLevel))
            {
                fileLogLevel = parsedFileLevel;
            }
            var envConsoleLogLevel = Environment.GetEnvironmentVariable("CONSOLE_LOG_LEVEL");
            var consoleLogLevel = LogLevel.Info;
            if (!string.IsNullOrEmpty(envConsoleLogLevel) &&
                Enum.TryParse<LogLevel>(envConsoleLogLevel, true, out var parsedConsoleLevel))
            {
                consoleLogLevel = parsedConsoleLevel;
            }
            GlobalConfigurator.Configure()
                .UseLocalTimeZone()
                .UseJsonISO8601Timestamp()
                .UseTextCustomTimestamp("yyyy-MM-dd HH:mm:ss.fff zzz")
                .EnableAutoDestructuring()
                .Apply();
            // === 配置日志 ===
            _rootLogger = new LoggerBuilder()
                .LoggerName("Runtime")
                .AddSizedRotatingFileSink(
                    logFilePath: "Logs/Runtime/Runtime.log",
                    maxFileSizeMB: 10,
                    maxFile: 5,
                    sinkConfiguration: new SinkConfiguration
                    {
                        ContextFilterExcludes = ["SearchBotService.CommandAuth"],
                        LogMinLevel = LogLevel.Debug
                    })
                .AddTimedRotatingFileSink(
                    logFilePath: "Logs/CommandAuth/CommandAuth.log",
                    maxFile: 30,
                    sinkConfiguration: new SinkConfiguration
                    {
                        ContextFilterIncludes = ["SearchBotService.CommandAuth"],
                        LogMinLevel = LogLevel.Debug
                    })
                .AddSizedRotatingFileSink(
                    logFilePath: "Logs/EW/Error.log",
                    maxFileSizeMB: 10,
                    maxFile: 5,
                    sinkConfiguration: new SinkConfiguration
                    {
                        LogMinLevel = LogLevel.Error
                    })
                .AddSizedRotatingFileSink(
                    logFilePath: "Logs/EW/Warning.log",
                    maxFileSizeMB: 10,
                    maxFile: 5,
                    sinkConfiguration: new SinkConfiguration
                    {
                        LogMinLevel = LogLevel.Warning,
                        LogMaxLevel = LogLevel.Warning
                    })
                .AddConsoleSink(
                    sinkConfiguration: new SinkConfiguration
                    {
                        ContextFilterExcludes = [],
                        LogMinLevel = LogLevel.Debug
                    })
                .Build();
            // Main 上下文包装器
            ILogger _mainLogger = _rootLogger.ForContext("Main");

            // === 代理 ===
            // 从环境变量中获取代理
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

            // === Token 读取 ===
            var token = Environment.GetEnvironmentVariable("SEARCH_BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                var ex = new InvalidOperationException();
                _mainLogger.Critical("环境变量 SEARCH_BOT_TOKEN 未设置！");
                throw ex;
            }

            // 配置主机服务
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    // 使用LunariumLogger日志系统替换默认的日志系统
                    logging.AddLunariumLogger(_rootLogger);
                })
                .ConfigureServices((services) =>
                {
                    services
                        // 配置 Host 选项
                        .Configure<HostOptions>(opts =>
                        {
                            opts.ShutdownTimeout = TimeSpan.FromSeconds(120);
                        })
                        // 添加代理配置到容器
                        .AddSingleton(proxyConfig)
                        // 添加日志实例到容器
                        .AddSingleton(_rootLogger)
                        // 添加 Bot 服务
                        .AddHostedService<SearchBotService>(provider =>
                        {
                            var logger = provider.GetRequiredService<ILogger>();
                            var proxy = provider.GetRequiredService<ProxyConfig>();
                            return new SearchBotService(token, logger, proxy);
                        });
                })
                .Build();

            _mainLogger.Info("🚀 服务启动中...");

            if (proxyConfig.Enable)
            {
                _mainLogger.Info("🔗 代理已启用: {proxyConfig.ProxyUrl}", proxyConfig.ProxyUrl);
            }
            else
            {
                _mainLogger.Info("🔗 代理未启用");
            }

            // 注册全局未捕获异常处理器
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                _mainLogger.Error("💥 发生未捕获的致命异常", e.ExceptionObject as Exception);
            };
            // 捕获全局并行任务异常
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                _mainLogger.Error("⚠️ 检测到未观察的任务异常", e.Exception);
                e.SetObserved(); // 标记为已处理，防止程序崩溃
            };

            await host.RunAsync();

            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            // 如果日志系统初始化成功，就用它来记录启动失败的错误
            // 否则，退回到用 Console.Write
            if (_rootLogger != null)
            {
                _rootLogger.ForContext("Program").Critical("💥 服务启动失败", ex);
            }
            else
            {
                Console.Write($"💥 服务启动失败: {ex}");
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            // 在 finally 块里调用关闭方法
            if (_rootLogger is IAsyncDisposable disposableLogger)
            {
                // 在程序退出时确保日志被完全写入文件
                Console.WriteLine("⏳ 正在关闭日志系统...");
                await disposableLogger.DisposeAsync();
                Console.WriteLine("✅ 日志系统已关闭。");
            }
        }
    }
}
