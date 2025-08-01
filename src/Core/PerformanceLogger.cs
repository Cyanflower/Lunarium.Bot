// Core/PerformanceLogger.cs
using Serilog;
using Serilog.Events;
using Serilog.Filters;

namespace SearchBot.Core
{
    /// <summary>
    /// 性能监控服务的日志配置器
    /// </summary>
    public class PerformanceLogger : IBotLoggerConfiguration
    {
        public LoggerConfiguration Configure(LoggerConfiguration loggerConfig)
        {
            return loggerConfig
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(Matching.FromSource<PerformanceMonitorService>())
                    .WriteTo.File(
                        path: "logs/Performance.log",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        fileSizeLimitBytes: 1024 * 1024 * 5, // 5MB
                        rollOnFileSizeLimit: true,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Message:lj}{NewLine}"
                    )
                );
        }
    }
} 