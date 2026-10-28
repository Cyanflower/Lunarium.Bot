using Lunarium.Common;
using Lunarium.Logging;
namespace Lunarium.Bot.Archive;

public static class LogConfigurator
{
    /// <summary>
    /// 日志上下文的根前缀，与 <see cref="ArchiveBotService"/> 里 <c>ForContext</c> 的值一致。
    /// <para>
    /// 私有 DI 容器注册的是已经带本前缀的 logger，各服务再
    /// <c>ForContext("Archiver")</c> / <c>ForContext("Notifier")</c> 会被 <c>LoggerWrapper</c>
    /// 拼接成 <c>ArchiveBot.Archiver</c> / <c>ArchiveBot.Notifier</c>。
    /// </para>
    /// </summary>
    public const string RootContext = "ArchiveBot";

    /// <summary>
    /// 仅审计：这些上下文只写 <c>Logs/Audit/Audit.log</c>，不重复写 Runtime.log。
    /// <para>
    /// <see cref="FilterConfig" /> 的前缀匹配区分大小写且只做 <c>StartsWith</c>，
    /// 因此这里必须写**完整路径**（含 <see cref="RootContext"/>），不能只写中间那段。
    /// </para>
    /// </summary>
    private static readonly List<string> AuditOnlyPrefixes = [$"{RootContext}.Audit"];

    /// <summary>
    /// 审计 + 运行时：所有用户/管理交互的日志两边都留——
    /// Runtime.log 保留完整链路（含 <c>BeginTrack</c> 的追踪 ID）便于排查，
    /// Audit.log 单独落一份便于按操作人检索。
    /// </summary>
    private static readonly List<string> SharedAuditPrefixes = [$"{RootContext}.AppInteraction"];

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
                    ContextFilterExcludes = AuditOnlyPrefixes,
                    LogMinLevel = fileLogLevel
                })
            .AddSizedRotatingFileSink(
                logFilePath: "Logs/Audit/Audit.log",
                maxFileSizeMB: 10,
                maxFile: 10,
                FilterConfig: new FilterConfig
                {
                    ContextFilterIncludes = [.. AuditOnlyPrefixes, .. SharedAuditPrefixes],
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