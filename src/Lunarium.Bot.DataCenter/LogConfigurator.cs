using Lunarium.Common;
using Lunarium.Logging;

namespace Lunarium.Bot.DataCenter;

/// <summary>
/// 日志初始化。日志级别从环境变量读取（由 systemd 的 <c>Environment=FILE_LOG_LEVEL=...</c> 提供）。
/// <para>
/// 私有 DI 容器注册的是已经带 <c>DataCenterBotService</c> 前缀的 logger，各服务再
/// <c>ForContext("Scan")</c> 会被 <c>LoggerWrapper</c> 拼接成 <c>DataCenterBotService.Scan</c>；
/// 过滤器只做 <c>StartsWith</c>，因此这里必须写**完整前缀**。
/// </para>
/// </summary>
public static class LogConfigurator
{
    public static ILogger Initialize()
    {
        var fileLogLevel = EnvVar.Get("FILE_LOG_LEVEL")?.AsEnum<LogLevel>(defaultValue: LogLevel.Info) ?? LogLevel.Info;
        var consoleLogLevel = EnvVar.Get("CONSOLE_LOG_LEVEL")?.AsEnum<LogLevel>(defaultValue: LogLevel.Info) ?? LogLevel.Info;

        GlobalConfigurator.Configure()
            .UseCustomTimezone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"))
            .UseJsonISO8601Timestamp()
            .UseTextCustomTimestamp("yyyy-MM-dd HH:mm:ss.fff zzz")
            .EnableAutoDestructuring()
            .Apply();

        return new LoggerBuilder()
            .SetLoggerName("Runtime")
            // 运行时：命令鉴权日志单独入 CommandAuth，Runtime 里排除
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Runtime/Runtime.log",
                maxFileSizeMB: 10,
                maxFile: 5,
                FilterConfig: new FilterConfig
                {
                    ContextFilterExcludes = ["DataCenterBotService.CommandAuth"],
                    LogMinLevel = fileLogLevel,
                })
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/CommandAuth/CommandAuth.log",
                maxFileSizeMB: 10,
                maxFile: 10,
                FilterConfig: new FilterConfig
                {
                    ContextFilterIncludes = ["DataCenterBotService.CommandAuth"],
                    LogMinLevel = fileLogLevel
                })
            // 扫描流程单独留档（同时保留在 Runtime 中便于串联）
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Scan/Scan.log",
                maxFileSizeMB: 10,
                maxFile: 10,
                FilterConfig: new FilterConfig
                {
                    ContextFilterIncludes = ["DataCenterBotService.Scan"],
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
    }
}
