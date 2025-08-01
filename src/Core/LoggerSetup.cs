// Core/BaseLogger.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using System.Reflection;

public static class LoggerSetup
{
    public static void Configure(
        HostBuilderContext context,
        IServiceProvider services,
        LoggerConfiguration loggerConfiguration
    )
    {
        loggerConfiguration
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()

            // 🖥️ 统一控制台输出 - 所有日志都通过这里输出到控制台
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )

            // 📁 Program 运行时日志文件
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.FromSource<Program>())
                .WriteTo.File(
                    path: "logs/Runtime.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            );
            

        var botConfigurators = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IBotLoggerConfiguration).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Select(t => Activator.CreateInstance(t))
            .OfType<IBotLoggerConfiguration>()
            .ToList();

        if (botConfigurators.Any())
        {
            foreach (var configurator in botConfigurators)
            {
                loggerConfiguration = configurator.Configure(loggerConfiguration);
            }
        }
    }
}