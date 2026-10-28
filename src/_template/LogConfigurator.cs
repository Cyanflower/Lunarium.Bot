using Lunarium.Common;
using Lunarium.Logging;

namespace Lunarium.Bot.Template;

/// <summary>
/// 日志初始化。日志级别从环境变量读取（由 systemd 的 <c>Environment=FILE_LOG_LEVEL=...</c> 提供）。
/// </summary>
public static class LogConfigurator
{
    public static ILogger Initialize()
    {
        var fileLogLevel = EnvVar.Get("FILE_LOG_LEVEL")?.AsEnum(defaultValue: LogLevel.Info) ?? LogLevel.Info;
        var consoleLogLevel = EnvVar.Get("CONSOLE_LOG_LEVEL")?.AsEnum(defaultValue: LogLevel.Info) ?? LogLevel.Info;

        GlobalConfigurator.Configure()
            .UseCustomTimezone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"))
            .UseJsonISO8601Timestamp()
            .UseTextCustomTimestamp("yyyy-MM-dd HH:mm:ss.fff zzz")
            .EnableAutoDestructuring()
            .Apply();

        var rootLogger = new LoggerBuilder()
            .SetLoggerName("Runtime")
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Runtime/Runtime.log",
                maxFileSizeMB: 10,
                maxFile: 5,
                FilterConfig: new FilterConfig
                {
                    // 审计上下文单独入 Audit sink，运行时日志里排除掉
                    ContextFilterExcludes = ["TemplateBotService.CommandAuth", "PerformanceMonitoringService"],
                    LogMinLevel = fileLogLevel
                })
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Audit/Audit.log",
                maxFileSizeMB: 10,
                maxFile: 10,
                FilterConfig: new FilterConfig
                {
                    ContextFilterIncludes = ["TemplateBotService.CommandAuth"],
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
