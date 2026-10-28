using Lunarium.Common;
using Lunarium.Logging;
namespace Lunarium.Bot.Lum;

public static class LogConfigurator
{
    public static ILogger Initialize()
    {
        // === 配置日志 ===
        var fileLogLevel = EnvVar.Get("FILE_LOG_LEVEL")?.AsEnum<LogLevel>(defaultValue: LogLevel.Info) ?? LogLevel.Info;
        var consoleLogLevel = EnvVar.Get("CONSOLE_LOG_LEVEL")?.AsEnum<LogLevel>(defaultValue: LogLevel.Info) ?? LogLevel.Info;

        GlobalConfigurator.Configure()
            .UseCustomTimezone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"))
            .UseJsonISO8601Timestamp()
            .UseTextCustomTimestamp("yyyy-MM-dd HH:mm:ss.fff zzz")
            .EnableAutoDestructuring()
            .Apply();
        // === 构建日志 ===
        var rootLogger = new LoggerBuilder()
            .SetLoggerName("Runtime")
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Runtime/Runtime.log",
                maxFileSizeMB: 10,
                maxFile: 5,
                FilterConfig: new FilterConfig
                {
                    ContextFilterExcludes = ["LumBotService.CommandAuth", "PerformanceMonitoringService"],
                    LogMinLevel = fileLogLevel
                })
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Audit/Audit.log",
                maxFileSizeMB: 10,
                maxFile: 10,
                FilterConfig: new FilterConfig
                {
                    ContextFilterIncludes = ["LumBotService.CommandAuth"],
                    LogMinLevel = fileLogLevel
                })
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/EW/Error.log",
                maxFileSizeMB: 10,
                maxFile: 5,
                FilterConfig: new FilterConfig
                {
                    LogMinLevel = LogLevel.Error
                })
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/EW/Warning.log",
                maxFileSizeMB: 10,
                maxFile: 5,
                FilterConfig: new FilterConfig
                {
                    LogMinLevel = LogLevel.Warning,
                    LogMaxLevel = LogLevel.Warning
                })
            .AddConsoleSink(
                FilterConfig: new FilterConfig
                {
                    LogMinLevel = consoleLogLevel
                })
            .Build();
        return rootLogger;
    }
}