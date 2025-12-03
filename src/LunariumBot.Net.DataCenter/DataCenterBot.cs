// .NET 核心库
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

// NuGet 包依赖
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Polly;
using Polly.Retry;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

// 解决方案内引用项目
using LunariumBot.Net.Logger;
using LunariumBot.Net.Logger.Extensions;
using LunariumBot.Net.Scheduler;
using LunariumBot.Net.Extensions;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.DataProcess;
using LunariumBot.Net.DataCenter.Scheduler;
using LunariumBot.Net.DataCenter.DataFetcher;
using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.DataCenter.Models;
using LunariumBot.Net.DataCenter.AppInteractionService;
using LunariumBot.Net.DataCenter.UrlSignedInfoCache;

namespace LunariumBot.Net.DataCenter;

public class DataCenterBotService : IHostedService
{
    private readonly string _token;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;

    private readonly IServiceProvider _privateServices;

    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly DatabaseService _databaseService;
    private readonly Listener _listener;
    private bool _isFirstTime = true;
    private readonly AuthUtils _authUtils;
    private readonly LunariumScheduler _scheduler;
    private readonly HealthMonitor _healthMonitoring;
    private readonly UrlSignedInfoCacheSyncAsyncJob _urlSignedInfoCacheSyncAsyncJob;

    // 全局配置 ================================
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    private readonly RateLimiter _rateLimiter;
    // ========================================
    
    // 传统命令文本 =============================
    private readonly HashSet<string> _reRegisterCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "重新注册",
        "re-registration",
        "reregister"
    };
    // ========================================

    // ======================================================================================
    #region 构造函数
    public DataCenterBotService(string token, ILogger runtimeRootLogger, ProxyConfig proxyConfig)
    {
        _token = token;
        // 本地日志上下文
        _logger = runtimeRootLogger.ForContext("DataCenterBotService");
        _logger.Info("初始化 DataCenterBot 服务...");

        // 全局配置
        _rateLimiter = RateLimiterConfigure();
        _requestOptions = RequestOptionsConfig();
        _httpRetryPolicy = HttpRetryPolicyConfig();
        _dbRetryPolicy = DbRetryPolicy();

        // 构建私有DI容器
        _privateServices = ConfigurePrivateServices(proxyConfig, _logger);

        // 3. 从私有容器中解析出核心服务以方便使用
        _configLoader = _privateServices.GetRequiredService<ConfigLoader>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _databaseService = _privateServices.GetRequiredService<DatabaseService>();
        _listener = _privateServices.GetRequiredService<Listener>();
        _authUtils = _privateServices.GetRequiredService<AuthUtils>();

        _scheduler = _privateServices.GetRequiredService<LunariumScheduler>();

        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        _urlSignedInfoCacheSyncAsyncJob = _privateServices.GetRequiredService<UrlSignedInfoCacheSyncAsyncJob>();

        _logger.Info("✅ DataCenterBot 服务初始化完成。");
    }
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region DI 容器构建
    // 封装的私有服务配置方法
    private IServiceProvider ConfigurePrivateServices(ProxyConfig proxyConfig, LunariumBot.Net.Logger.ILogger logger)
    {
        _logger.Info("配置 DataCenterBot 的私有服务容器...");

        var services = new ServiceCollection()

            // ==================== BOT 基础功能与配置 ====================
            // 注册日志
            .AddSingleton(logger)
            .AddSingleton(proxyConfig)

            // 注册 Discord 客户端，并使用从全局传入的代理配置
            .AddSingleton<DiscordSocketClient>(provider =>
            {
                _logger.Info("创建 DiscordSocketClient... 代理启用: {proxyConfig.Enable}", proxyConfig.Enable);
                if (proxyConfig.Enable && !string.IsNullOrEmpty(proxyConfig.ProxyUrl))
                {
                    var proxy = new WebProxy(proxyConfig.ProxyUrl);
                    var config = new DiscordSocketConfig
                    {
                        RestClientProvider = DefaultRestClientProvider.Create(useProxy: true, webProxy: proxy),
                        WebSocketProvider = DefaultWebSocketProvider.Create(proxy)
                    };
                    return new DiscordSocketClient(config);
                }
                else
                {
                    return new DiscordSocketClient();
                }
            })

            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader>()

            // 注册命令鉴权工具
            .AddSingleton<AuthUtils>()
            // 注册交互服务
            .AddSingleton<InteractionService>(provider =>
                new InteractionService(provider.GetRequiredService<DiscordSocketClient>())
            )

            // ==================== 业务逻辑 ====================
            // ==== [基础设施] ====
            // 注册全局并发控制器
            .AddSingleton(_rateLimiter)
            // Discord.Net 请求选项
            .AddSingleton(_requestOptions)
            // http 请求重试策略
            .AddKeyedSingleton("HttpPolicy", _httpRetryPolicy)
            // 数据库请求重试策略
            .AddKeyedSingleton("DbPolicy", _dbRetryPolicy)
            // 注册数据库服务
            .AddSingleton<DatabaseService>()
            // 注册数据库处理服务
            .AddSingleton<DataProcessService>()
            .AddSingleton<UrlSignedInfoCacheService>()

            // ==== [服务] ====
            // 注册帖子扫描模块
            .AddSingleton<ThreadScanner>()
            .AddSingleton<MembersScanner>()
            // 注册监听器
            .AddSingleton<Listener>()
            // 注册分析器
            .AddSingleton<DataAnalysis>()
            // 注册命令服务
            .AddSingleton<AdminCommands>()

            // ==== [定时任务] ====
            .AddSingleton<AnalysisAsyncJob>()
            .AddSingleton<FullSyncAsyncJob>()
            .AddSingleton<SyncMembersAndRolesJob>()
            .AddSingleton<UrlSignedInfoCacheSyncAsyncJob>()
            .AddSingleton<UrlSignedInfoCacheCheckAsyncJob>()
            .AddSingleton<SystemMonitorJob>()
            .AddSingleton<LunariumScheduler>(provider =>
            {
                return new LunariumScheduler(provider, logger.ToMicrosoftLogger("Scheduler"));
            })
            .AddSingleton<HealthMonitor>()

            // 构建 DI 容器
            .BuildServiceProvider();

        return services;
    }
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region 全局配置部分
    private TokenBucketRateLimiter RateLimiterConfigure()
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            // Token 恢复速率：每秒 45 个
            TokensPerPeriod = 45,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),

            // Token 上限
            TokenLimit = 45,

            // 队列设置
            QueueLimit = 100000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            
            // 自动补充: 开启
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// 配置 Discord API 请求选项。
    /// 设置较长的超时时间以配合 Polly 重试策略, 并启用 Discord.Net 的内置重试机制。
    /// </summary>
    /// <returns>配置好的 RequestOptions 实例</returns>
    public static RequestOptions RequestOptionsConfig()
    {
        // RequestOptions: 维持 Discord.Net 的默认重试机制
        // Timeout 设置为 20 分钟, 确保 Polly 有足够时间完成重试
        return new RequestOptions
        {
            Timeout = 20 * 60 * 1000, // 设置总超时为 20 分钟
            RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试
        };
    }

    /// <summary>
    /// 配置 HTTP 请求重试策略（基于 Polly）。
    /// 该策略专门处理 Discord API 和网络相关的临时性错误。
    /// 使用指数退避策略：2秒、4秒、8秒。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        return Policy
            // 定义需要处理的异常:
            // - Discord.HttpException: 处理 5xx 服务器错误和 429 速率限制
            //   不重试: 404(资源不存在)、403(权限不足)、400(请求格式错误)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 && (int)ex.HttpCode != 403 && (int)ex.HttpCode != 400)
            // - HttpRequestException: 处理网络层级异常（SSL 握手失败、连接被重置等）
            .Or<HttpRequestException>()
            // - TaskCanceledException: 处理 HttpClient 超时
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            // - 处理 JSON 解析异常 (针对返回 HTML 错误页的情况)
            .Or<Newtonsoft.Json.JsonReaderException>() 

            // 定义重试逻辑：指数退避
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2秒、4秒、8秒
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // 在日志中清晰显示触发重试的异常类型
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Warning("⚠️ HTTP 请求失败 ({ErrorType}), 将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }

    /// <summary>
    /// 配置数据库操作重试策略（基于 Polly）。
    /// 该策略专门处理 PostgreSQL 数据库的临时性错误。
    /// 使用指数退避策略加随机抖动：2秒、4秒、8秒 + 0-100ms 随机延迟。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    private AsyncRetryPolicy DbRetryPolicy()
    {
        return Policy
            // 定义需要处理的异常:
            // - NpgsqlException: 处理 Npgsql 的暂时性故障（如连接池耗尽）
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            // - TimeoutException: 处理连接或查询超时
            .Or<TimeoutException>()
            // - SocketException: 处理底层网络连接问题
            .Or<SocketException>()

            // 使用指数退避策略 + 随机抖动
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                onRetry: (exception, timespan, attempt, context) =>
                {
                    _logger.Warning("⚠️ 数据库操作失败, 将在 {Seconds:F2} 秒后进行第 {Attempt}/3 次重试",
                        timespan.TotalSeconds, attempt);
                }
            );
    }
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region Host 服务接口: 生命周期管理
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 DataCenterBot 服务...");

        // 注册事件处理器
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        // 传统命令的消息事件
        // _client.MessageReceived += HandleMessageCommandAsync;

        // 加载命令模块，使用私有DI容器
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _privateServices);

        _logger.Info("斜杠命令模块已加载。");

        // 调度器和任务配置
        // 示例：每天凌晨 4:00 执行 FullSyncJob
        // Cron 表达式 "0 4 * * 1" 的意思是：
        // 分钟=0, 小时=4, 日=任意, 月=任意, 星期几=1
        _logger.Info("⚙️ 正在配置定时任务调度器...");
        var TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        // 每周
        _scheduler.ScheduleWeeklyAsync<FullSyncAsyncJob>(dayOfWeek: DayOfWeek.Tuesday, hour: 4, minute: 30, name: "同步扫描", timeZone: TimeZone);
        // 每日
        _scheduler.ScheduleDailyAsync<AnalysisAsyncJob>(hour: 5, name: "每日分析", timeZone: TimeZone);
        _scheduler.ScheduleDailyAsync<UrlSignedInfoCacheSyncAsyncJob>(hour: 4, minute: 30, name: "Url缓存同步", timeZone: TimeZone);
        // 每小时
        _scheduler.ScheduleHourlyAsync<SyncMembersAndRolesJob>(minute: 50, name: "用户信息与身份组同步", timeZone: TimeZone);
        _scheduler.ScheduleHourlyAsync<UrlSignedInfoCacheCheckAsyncJob>(minute: 45, name: "Url缓存检查", timeZone: TimeZone);
        // 间隔
        _scheduler.ScheduleIntervalAsync<SystemMonitorJob>(5, "系统监控", timeZone: TimeZone);
        // 启动调度器
        _scheduler.Start();
        _logger.Info("✅ 定时任务已配置完成。");
        _logger.Info($"\n{_scheduler.GetAllJobsInfo()}");

        // 登录和启动
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        _logger.Info("DataCenterBot 服务已启动，等待连接完成...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 DataCenterBot 服务...");

        // 取消所有事件订阅
        _client.Log -= LogAsync;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        // 传统命令的消息事件
        // _client.MessageReceived -= HandleMessageCommandAsync;

        _healthMonitoring.RemoveConnectEventHandler();
        _listener.RemoveEventHandlers();

        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _logger.Info("✅ DataCenterBot 服务已停止。");
    }
    #endregion
    // ======================================================================================


    // ======================================================================================
    #region 框架回调与Bot实例生命周期管理
    private Task LogAsync(LogMessage log)
    {
        // 将 Discord.Net 的日志转换为 Microsoft.Extensions.Logging 日志
        switch (log.Severity)
        {
            case LogSeverity.Critical:
                _logger.Critical(log.Exception, "[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Error:
                _logger.Error(log.Exception, "[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Warning:
                _logger.Warning("[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Info:
                _logger.Info("[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                _logger.Debug("[Discord.Net] {log.Message}", log.Message);
                break;
        }
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        try
        {
            _logger.Info("🎉 Bot 已就绪！已连接为: {_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}", _client.CurrentUser.Username, _client.CurrentUser.Discriminator);
            if (_isFirstTime == true)
            {
                // 现在 bot 完全就绪，可以安全地同步命令
                await SyncCommandsAsync();
                _listener.AddEventHandlers();
                _healthMonitoring.AddConnectEventHandler();
                _isFirstTime = false;

                var loginEmbed = new EmbedBuilder()
                    .WithTitle("数据中心监控")
                    .WithAuthor(_client.CurrentUser)
                    .WithDescription($"""
                        ### 服务已启动
                        登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                        BOT Latency: {_client.Latency}
                        {await _databaseService.GetDatabaseState()}
                        {_scheduler.GetAllJobsInfo()}
                        """)
                    .Build();
                var channelCache = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogThread) as IMessageChannel;
                if (channelCache is not null) await channelCache.SendMessageAsync(embed: loginEmbed);

                // 立即从数据库同步一次缓存
                await _urlSignedInfoCacheSyncAsyncJob.ExecuteAsync();
            }
            else
            {
                var loginEmbed = new EmbedBuilder()
                    .WithTitle("数据中心监控")
                    .WithAuthor(_client.CurrentUser)
                    .WithDescription($"""
                        ### BOT 客户端重新连接
                        登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                        检测到 BOT 客户端在运行时重新连接 Discord API 网关事件
                        <@867658156510150716>
                        以下文件是最近延迟信息于网关事件
                        {await _databaseService.GetDatabaseState()}
                        {_scheduler.GetAllJobsInfo()}
                        """)
                    .Build();
                // 获取监控日志字符串
                var latencysLog = _healthMonitoring.GetLatencysLog();
                var connectStatesLog = _healthMonitoring.GetConnectStatesLog();

                // 把字符串转换成内存流
                using var latencyStream = new MemoryStream(Encoding.UTF8.GetBytes(latencysLog));
                using var connectStateStream = new MemoryStream(Encoding.UTF8.GetBytes(connectStatesLog));

                // 为每个流创建一个 FileAttachment
                var latencyFile = new FileAttachment(latencyStream, "latency-log.log");
                var connectStateFile = new FileAttachment(connectStateStream, "connection-state-log.log");

                var channelCache = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogThread) as IMessageChannel;
                if (channelCache is not null)
                {
                    await channelCache.SendMessageAsync(embed: loginEmbed);
                    await channelCache.SendFilesAsync(text: "<@867658156510150716>", attachments: new[] { latencyFile, connectStateFile });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("ReadyAsync 出现异常:", ex);
        }

    }


    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _privateServices);
        }
        catch (Exception ex)
        {
            _logger.Error("处理交互时发生错误。", ex);
        }
    }
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region 传统命令: 注册斜杠命令
    private async Task HandleMessageCommandAsync(SocketMessage message)
    {
        // 1. 过滤掉不关心的消息
        //    - 消息不是用户发的 (例如系统消息)
        //    - 消息是机器人自己或其他机器人发的
        if (message is not SocketUserMessage userMessage || message.Author.IsBot)
        {
            return;
        }

        // 2. 检查消息是否 @ 了Bot
        if (!userMessage.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
        {
            return;
        }

        // 3. 解析出命令文本
        //    用正则表达式把 @mention 的部分去掉，然后去掉前后的空格
        var commandText = Regex.Replace(userMessage.Content, @"<@!?\d+>\s*", "").Trim();

        // 忽略大小写的匹配
        if (!_reRegisterCommands.Any(cmd => string.Equals(cmd, commandText, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // 记录操作
        _logger.Info("用户 {userMessage.Author.Username} 触发了传统命令: {commandText}", userMessage.Author.Username, commandText);

        // 5. 鉴权
        var guildUser = userMessage.Author as IGuildUser;
        if (guildUser is null) return;
        if (!_authUtils.IsAdmin(guildUser.RoleIds))
        {
            _logger.Warning("鉴权失败: 用户 {userMessage.Author.Username} 尝试执行管理员命令失败。", userMessage.Author.Username);
            await userMessage.ReplyAsync("权限不足: 该命令为系统维护命令");
            return;
        }

        _logger.Info("鉴权成功: 用户 {userMessage.Author.Username} 正在执行命令。", userMessage.Author.Username);
        await userMessage.ReplyAsync("鉴权成功: 正在刷新所有斜杠命令... 请稍等片刻");

        try
        {
            var sendMessage = new StringBuilder();
            // 6. 清除全局命令
            _logger.Info("正在清除全局命令...");
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[0]);
            _logger.Info("全局命令已清除。");
            sendMessage.Append("- 全局命令已清除。");

            // 7. 清除各个服务器中的命令
            foreach (var guildId in _configLoader.Config.DeployConfig.DeployGuildIds)
            {
                try
                {
                    _logger.Info("正在清除服务器 {guildId} 的命令...", userMessage.Author.Username);
                    var guild = _client.GetGuild(guildId);
                    if (guild is not null)
                    {
                        // 使用空数组覆盖服务器命令
                        await guild.BulkOverwriteApplicationCommandAsync(new ApplicationCommandProperties[0]);
                        _logger.Info("服务器 {guildId} 的命令已清除。", userMessage.Author.Username);
                        sendMessage.Append($"- 服务器 {guildId} 的命令已清除。");
                    }
                    else
                    {
                        _logger.Warning("无法找到服务器 {guildId}", guildId);
                        sendMessage.Append($"- 无法找到服务器 {guildId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "清除服务器 {guildId} 命令时发生错误", guildId);
                    sendMessage.Append($"- 清除服务器 {guildId} 命令时发生错误\n```{ex.ToString().Slice(150, "...")}```");
                    // 继续处理其他服务器
                }
            }

            // 重新调用同步方法
            await SyncCommandsAsync();

            await userMessage.Channel.SendMessageAsync($"✅ 操作完成！\n{sendMessage.ToString()}");
        }
        catch (Exception ex)
        {
            _logger.Error("执行重新注册命令时发生错误。", ex);
            await userMessage.Channel.SendMessageAsync("执行重新注册命令时发生错误。");
        }
    }
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region 辅助方法
    /// <summary>
    /// 同步命令到部署服务器列表
    /// </summary>
    private async Task SyncCommandsAsync()
    {
        try
        {
            _logger.Info("正在同步命令到部署服务器列表...");
            _logger.Info("同步命令列表:");
            foreach (var command in _interactions.SlashCommands)
            {
                _logger.Info("  - {command.Name}: {command.Description}", command.Name, command.Description);
            }
            _logger.Info("命令总数: {_interactions.SlashCommands.Count}", _interactions.SlashCommands.Count);

            // 同步命令到部署服务器列表
            foreach (var guildId in _configLoader.Config.DeployConfig.DeployGuildIds)
            {
                var guild = _client.GetGuild(guildId);
                if (guild is not null)
                {
                    _logger.Info("正在同步命令到服务器: {guildId} - {guild.Name}", guildId, guild.Name);
                    await _interactions.RegisterCommandsToGuildAsync(guildId, true);
                    _logger.Info("服务器 {guild.Name} 命令同步完成。", guild.Name);
                }
                else
                {
                    _logger.Warning("⚠️ 无法找到服务器 {guildId}，Bot可能不在该服务器中或服务器ID不正确", guildId);
                }
            }
            _logger.Info("✅ 命令同步完成！DataCenterBot 服务已完全启动。");
        }
        catch (Exception ex)
        {
            _logger.Error("❌ 同步命令时发生错误", ex);
        }
    }
    #endregion
    // ======================================================================================
}