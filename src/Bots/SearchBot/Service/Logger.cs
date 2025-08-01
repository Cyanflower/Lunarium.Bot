// Services/Logger.cs (增强版本)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;

namespace SearchBot;

public class Logger : IBotLoggerConfiguration
{
    public LoggerConfiguration Configure(LoggerConfiguration loggerConfig)
    {
        return loggerConfig
            
            // === 分类文件输出 ===
            // 所有日志只根据大小进行滚动, 不根据时间进行滚动
            
            // 配置加载器日志
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.FromSource("SearchBot.ConfigLoader"))
                .WriteTo.File(
                    path: "logs/SearchBot/ConfigLoader.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            )
            
            // 用户订阅数据库增,删,查,改操作日志 (等待实现 UserSubscriptionService)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.FromSource("SearchBot.UserSubscriptionService"))
                .WriteTo.File(
                    path: "logs/SearchBot/UserSubscription.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            )
            
            // 帖子数据库增,删,改操作日志 (等待实现 PostDBService)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.FromSource("SearchBot.PostDBService"))
                .WriteTo.File(
                    path: "logs/SearchBot/PostDB.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            )

            // 帖子数据库查询操作日志 (等待实现 PostQueryService)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.FromSource("SearchBot.PostQueryService"))
                .WriteTo.File(
                    path: "logs/SearchBot/PostDBQuery.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            )

            // 斜杠命令使用日志 - 修正匹配具体的类名
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(Matching.FromSource("SearchBot.AppCommandService.AppCommands"))
                .WriteTo.File(
                    path: "logs/SearchBot/SlashCommand.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            )

            // 运行时日志 (SearchBot 主服务和其他组件)
            .WriteTo.Logger(lc => lc
                .Filter.ByExcluding(Matching.FromSource("SearchBot.ConfigLoader"))
                .Filter.ByExcluding(Matching.FromSource("SearchBot.UserSubscriptionService"))
                .Filter.ByExcluding(Matching.FromSource("SearchBot.PostDBService"))
                .Filter.ByExcluding(Matching.FromSource("SearchBot.PostQueryService"))
                .Filter.ByExcluding(Matching.FromSource("SearchBot.AppCommandService.AppCommands"))
                // 使用正确的前缀匹配语法
                .Filter.ByIncludingOnly(Matching.WithProperty<string>("SourceContext", s => s != null && s.StartsWith("SearchBot.")))
                .WriteTo.File(
                    path: "logs/SearchBot/Runtime.log",
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: 5,
                    fileSizeLimitBytes: 1024 * 1024 * 10,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
            );
    }
}