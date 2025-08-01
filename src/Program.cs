// Program.cs
using System.Net;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using DotNetEnv;

using SearchBot;
using SearchBot.Core;

public class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            // 🔧 必须在 Host.CreateDefaultBuilder 之前加载 .env 文件
            if (File.Exists(".env"))
            {
                // 先记录系统当前的环境变量
                var systemEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                Console.WriteLine($"🔍 系统环境变量: {systemEnv ?? "未设置"}");

                // 加载 .env 文件
                Env.Load();
                
            }

            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

            var host = Host.CreateDefaultBuilder(args)
                .UseEnvironment(environment)  // 明确指定环境
                .UseSerilog(LoggerSetup.Configure)
                .ConfigureServices((context, services) => {
                    services
                        .Configure<ProxyConfig>(context.Configuration.GetSection("ProxyConfig"))

                        // 配置 Host 选项
                        .Configure<HostOptions>(opts => 
                        {
                            opts.ShutdownTimeout = TimeSpan.FromSeconds(120);
                        })
                        
                        // 添加各个 Bot 服务
                        .AddHostedService<SearchBotService>()
                        // 添加性能监控服务
                        .AddHostedService<PerformanceMonitorService>();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            var env = host.Services.GetRequiredService<IHostEnvironment>();

            var proxyConfig = host.Services.GetRequiredService<IOptions<ProxyConfig>>();

            logger.LogInformation("🚀 服务启动中... 环境: {Environment}", env.EnvironmentName);

            if (proxyConfig.Value.Enable)
            {
                logger.LogInformation("🔗 代理已启用: {ProxyUrl}", proxyConfig.Value.ProxyUrl);
            }
            else
            {
                logger.LogInformation("🔗 代理未启用");
            }
            
            // 注册全局未捕获异常处理器
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                logger.LogError(e.ExceptionObject as Exception, "💥 发生未捕获的致命异常");
            };
            
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                logger.LogError(e.Exception, "⚠️ 检测到未观察的任务异常");
                e.SetObserved(); // 标记为已处理，防止程序崩溃
            };

            await host.RunAsync();
            
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Write("💥 服务启动失败: {0}",ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}

public class ProxyConfig
{
    public bool Enable { get; set; } = false;
    public string ProxyUrl { get; set; } = "";
}