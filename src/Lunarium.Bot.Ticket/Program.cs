using Lunarium.Common;
using Lunarium.Common.Net;
using Lunarium.Logging;
using Lunarium.Logging.Extensions;
using Lunarium.Bot.Framework;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lunarium.Bot.Ticket;

public class Program
{
    public static async Task Main(string[] args)
    {
        ILogger? _rootLogger = null;
        try
        {
            // === 配置日志 ===
            _rootLogger = LogConfigurator.Initialize();
            ILogger _mainLogger = _rootLogger.ForContext("Program");

            // === 代理 ===
            var proxyUrl = ProxyUrl.FromEnvironment();

            // === Bot Token ===
            var token = EnvVar.GetRequired("TICKET_BOT_TOKEN");

            // 配置主机服务
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    // 使用LunariumLogger日志系统替换Host自身默认的日志系统
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
                        // 添加 Bot 服务
                        .AddHostedService<TicketBotService>(provider =>
                        {
                            return new TicketBotService(token, _rootLogger, proxyUrl);
                        });
                })
                .Build();

            _mainLogger.Info("🚀 服务启动中...");
            _mainLogger.Info("🔗 代理: {ProxyUrl}", proxyUrl?.ToString() ?? "未启用");

            // 注册全局未捕获异常处理器
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                _mainLogger.Error("💥 发生未捕获的致命异常", e.ExceptionObject as Exception);
            };
            // 捕获全局并行任务异常
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                _mainLogger.Error(e.Exception, "⚠️ 检测到未观察的任务异常");
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
                _rootLogger.ForContext("Program").Critical(ex, "💥 服务启动失败");
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