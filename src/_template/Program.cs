using Lunarium.Common;
using Lunarium.Common.Net;
using Lunarium.Logging;
using Lunarium.Logging.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lunarium.Bot.Template;

/// <summary>
/// 进程入口。职责只有四段：配置日志 → 构建 Host → 注册 BotService → RunAsync。
/// 业务逻辑一律放进 <see cref="TemplateBotService"/> 的私有 DI 容器。
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        ILogger? rootLogger = null;
        try
        {
            // === 1. 配置日志 ===
            rootLogger = LogConfigurator.Initialize();
            ILogger mainLogger = rootLogger.ForContext("Main");

            // === 2. 环境准备（代理 / Token）===
            var proxyUrl = ProxyUrl.FromEnvironment();

            // 环境变量名约定：<项目>_BOT_TOKEN，由 systemd 的 EnvironmentFile 提供
            var token = EnvVar.GetRequired("TEMPLATE_BOT_TOKEN");

            // === 3. 构建 Host ===
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    // 用 Lunarium.Logging 替换 Host 默认日志系统
                    logging.AddLunariumLogger(rootLogger);
                })
                .ConfigureServices(services =>
                {
                    services
                        .Configure<HostOptions>(opts =>
                        {
                            // systemd 侧 TimeoutStopSec 必须大于此值
                            opts.ShutdownTimeout = TimeSpan.FromSeconds(120);
                        })
                        .AddHostedService<TemplateBotService>(_ =>
                            new TemplateBotService(token, rootLogger, proxyUrl));
                })
                .Build();

            mainLogger.Info("🚀 服务启动中...");
            mainLogger.Info("🔗 代理: {ProxyUrl}", proxyUrl?.ToString() ?? "未启用");

            // 全局未捕获异常
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                mainLogger.Error("💥 发生未捕获的致命异常", e.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                mainLogger.Error(e.Exception, "⚠️ 检测到未观察的任务异常");
                e.SetObserved();
            };

            // === 4. 运行 ===
            await host.RunAsync();
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            if (rootLogger is not null)
            {
                rootLogger.ForContext("Program").Critical(ex, "💥 服务启动失败");
            }
            else
            {
                Console.Write($"💥 服务启动失败: {ex}");
            }
            Environment.ExitCode = 1;
        }
        finally
        {
            if (rootLogger is IAsyncDisposable disposableLogger)
            {
                Console.WriteLine("⏳ 正在关闭日志系统...");
                await disposableLogger.DisposeAsync();
                Console.WriteLine("✅ 日志系统已关闭。");
            }
        }
    }
}
