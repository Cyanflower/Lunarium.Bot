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
using LunariumBot.Net.ReleaseBot.AuthUtils;
using LunariumBot.Net.ReleaseBot.Models;
using LunariumBot.Net.ReleaseBot.AppInteractionService;
using LunariumBot.Net.ReleaseBot.InteractionsCache;
using LunariumBot.Net.ReleaseBot.ApplicationServices;
using LunariumBot.Net.ReleaseBot.Database;
using LunariumBot.Net.ReleaseBot.TokenBucketServices;
using LunariumBot.Net.ReleaseBot.Listener;
using LunariumBot.Net.ReleaseBot.CacheServices;
using LunariumBot.Net.ReleaseBot.Scheduler;
using LunariumBot.Net.ReleaseBot.Utils;

namespace LunariumBot.Net.ReleaseBot;

public class ReleaseBotAssistService : IHostedService
{
    private readonly string _token;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;

    private readonly IServiceProvider _privateServices;

    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    // private readonly DatabaseService _databaseService;
    private bool _isFirstTime = true;
    private readonly HealthMonitor _healthMonitoring;

    // 全局配置 ================================
    private readonly HttpClient _httpClient;
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly RateLimiter _rateLimiter;
    // ========================================
    

    // ======================================================================================
    #region 构造函数
    public ReleaseBotAssistService(BotTokens token, ILogger runtimeRootLogger, ProxyConfig proxyConfig)
    {
        _token = token.AssistToken!;
        // 本地日志上下文
        _logger = runtimeRootLogger.ForContext("ReleaseBotAssistService");
        _logger.Info("初始化 ReleaseBotAssistService 服务...");

        // 全局配置
        _rateLimiter = RateLimiterConfigure();
        _httpClient = HttpClientConfig(proxyConfig);
        _requestOptions = RequestOptionsConfig();
        _httpRetryPolicy = HttpRetryPolicyConfig();

        // 构建私有DI容器
        _privateServices = ConfigurePrivateServices(proxyConfig, _logger);

        // 3. 从私有容器中解析出核心服务以方便使用
        _configLoader = _privateServices.GetRequiredService<ConfigLoader>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();

        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();

        _logger.Info("✅ ReleaseBot 服务初始化完成。");
    }
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region DI 容器构建
    // 封装的私有服务配置方法
    private IServiceProvider ConfigurePrivateServices(ProxyConfig proxyConfig, LunariumBot.Net.Logger.ILogger logger)
    {
        _logger.Info("配置 ReleaseBot 的私有服务容器...");

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
                        WebSocketProvider = DefaultWebSocketProvider.Create(proxy),
                        GatewayIntents = GatewayIntents.All
                    };
                    return new DiscordSocketClient(config);
                }
                else
                {
                    var config = new DiscordSocketConfig
                    {
                        GatewayIntents = GatewayIntents.All
                    };
                    return new DiscordSocketClient(config);
                }
            })

            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader>()

            // 注册命令鉴权工具            
            .AddSingleton<TokenBucketService>(provider => 
            {
                var logger = provider.GetRequiredService<LunariumBot.Net.Logger.ILogger>();
                var configLoader = provider.GetRequiredService<ConfigLoader>();
                var tokenLimit = configLoader.Config.DeployConfig.ReleaseConfig.DailyLimit;
                return new TokenBucketService(logger, tokenLimit);
            })
            .AddSingleton<ApplicationService>()
            // 注册交互服务
            .AddSingleton<InteractionService>(provider =>
                new InteractionService(provider.GetRequiredService<DiscordSocketClient>())
            )

            // ==================== 业务逻辑 ====================
            // ==== [基础设施] ====
            // 注册全局并发控制器
            .AddSingleton(_rateLimiter)
            // 注册全局Http客户端
            .AddSingleton(_httpClient)
            // Discord.Net 请求选项
            .AddSingleton(_requestOptions)
            // http 请求重试策略
            .AddKeyedSingleton("HttpPolicy", _httpRetryPolicy)
            // 注册数据库服务
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
            // Token 恢复速率：每秒 5 个
            TokensPerPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),

            // Token 上限
            TokenLimit = 5,

            // 队列设置
            QueueLimit = 100000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            
            // 自动补充: 开启
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// 配置 HTTP 客户端。
    /// 如果启用了代理, 则创建使用代理的客户端；否则创建默认客户端。
    /// 该客户端主要用于下载附件（如检查作品更新的 PNG 图片）。
    /// </summary>
    /// <param name="proxyConfig">代理配置对象</param>
    /// <returns>配置好的 HttpClient 实例</returns>
    private static HttpClient HttpClientConfig(ProxyConfig proxyConfig)
    {
        if (proxyConfig.Enable && !string.IsNullOrEmpty(proxyConfig.ProxyUrl))
        {
            return new HttpClient(new HttpClientHandler
            {
                Proxy = new WebProxy(proxyConfig.ProxyUrl),
                UseProxy = true
            });
        }
        else
        {
            return new HttpClient();
        }
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
    #endregion
    // ======================================================================================

    // ======================================================================================
    #region 核心功能
    public async Task<IUserMessage?> SendWorkViaDM(ulong userId, MessageComponent component)
    {
        try
        {
            using var lease = await _rateLimiter.AcquireAsync(1);
            var message = await DMSendRelease.Send(_client, userId, component);
            return message;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "SendWorkViaDM Exception");
            return null;
        }
    }

    #endregion
    // ======================================================================================

    // ======================================================================================
    #region Host 服务接口: 生命周期管理
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 ReleaseBot 服务...");

        // 注册事件处理器
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        // 传统命令的消息事件
        // _client.MessageReceived += HandleMessageCommandAsync;

        // 加载命令模块，使用私有DI容器
        // await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _privateServices);
        await _interactions.AddModuleAsync<AssistBotInteraction>(_privateServices);

        _logger.Info("斜杠命令模块已加载。");

        // 登录和启动
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        _logger.Info("ReleaseBot 服务已启动，等待连接完成...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 ReleaseBot 服务...");

        // 取消所有事件订阅
        _client.Log -= LogAsync;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        // 传统命令的消息事件
        // _client.MessageReceived -= HandleMessageCommandAsync;

        _healthMonitoring.RemoveConnectEventHandler();

        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _logger.Info("✅ ReleaseBot 服务已停止。");
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
            default:
                _logger.Info("[Discord.Net] {log.Message}", log.Message);
                break;

        }
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        await Task.Delay(5000);
        try
        {
            _logger.Info("🎉 Bot 已就绪！已连接为: {_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}", _client.CurrentUser.Username, _client.CurrentUser.Discriminator);
            if (_isFirstTime == true)
            {
                // 现在 bot 完全就绪，可以安全地同步命令
                await SyncCommandsAsync();
                _healthMonitoring.AddConnectEventHandler();
                _isFirstTime = false;

                var loginEmbed = new EmbedBuilder()
                    .WithTitle("作品发布BOT (DM) 监控")
                    .WithAuthor(_client.CurrentUser)
                    .WithDescription($"""
                        ### 服务已启动
                        登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                        BOT Latency: {_client.Latency}
                        """)
                    .Build();
                var channelCache = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogThread) as IMessageChannel;
                if (channelCache is not null) await channelCache.SendMessageAsync(embed: loginEmbed);

                _client.ModalSubmitted += async (modal) =>
                {
                    _logger.Debug("收到 Modal 提交: {CustomId}", modal.Data.CustomId);
                };
                _interactions.ModalCommandExecuted += async (commandInfo, context, result) =>
                {
                    // commandInfo: 也就是你的 HandleUpFilesModalAsync 方法信息
                    // result: 执行结果，包含成功或失败的原因

                    if (!result.IsSuccess)
                    {
                        _logger.Error(
                            "❌ Modal 执行失败！\n" +
                            "命令: {CommandName}\n" +
                            "错误类型: {ErrorType}\n" +
                            "错误原因: {ErrorReason}",
                            commandInfo?.Name ?? "Unknown",
                            result.Error,
                            result.ErrorReason // 🔥 这里会告诉你为什么进不去方法体
                        );

                        // 如果是参数解析失败，这里通常会显示 "BadArgs" 或 "ParseFailed"
                        // 如果是类型转换失败（比如 IEnumerable 问题），这里会显示 "ConvertFailed"
                        if (result.Error == InteractionCommandError.ParseFailed ||
                            result.Error == InteractionCommandError.ConvertFailed ||
                            result.Error == InteractionCommandError.BadArgs)
                        {
                            // 你可以在这里做一些额外的调试输出
                        }
                    }
                    else
                    {
                        _logger.Debug("✅ Modal 执行成功: {CommandName}", commandInfo?.Name);
                    }
                };
            }
            else
            {
                var loginEmbed = new EmbedBuilder()
                    .WithTitle("作品发布BOT (DM) 监控")
                    .WithAuthor(_client.CurrentUser)
                    .WithDescription($"""
                        ### BOT 客户端重新连接
                        登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                        检测到 BOT 客户端在运行时重新连接 Discord API 网关事件
                        <@867658156510150716>
                        以下文件是最近延迟信息于网关事件
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

            // 或者在模块添加后
            var modules = _interactions.Modules;
            foreach (var module in modules)
            {
                _logger.Debug("模块: {ModuleName}", module.Name);
                foreach (var command in module.SlashCommands)
                {
                    _logger.Debug("  - 斜杠命令: {CommandName}", command.Name);
                }
                foreach (var command in module.ComponentCommands)
                {
                    _logger.Debug("  - 组件命令: {CustomId}", command.Name);
                }
                foreach (var command in module.ModalCommands)
                {
                    _logger.Debug("  - Modal 命令: {CustomId}", command.Name);
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
            _logger.Info("✅ 命令同步完成！ReleaseBot 服务已完全启动。");
        }
        catch (Exception ex)
        {
            _logger.Error("❌ 同步命令时发生错误", ex);
        }
    }
    #endregion
    // ======================================================================================
}