using Lunarium.Common;
using Lunarium.Common.Net;
using Lunarium.Logging;
using Lunarium.Bot.Framework.Health;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.RateLimiting;
using Lunarium.Bot.Framework;

using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

// 项目内命名空间
using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Archive.AppInteractionService;
using Lunarium.Bot.Archive.Archiver;
using ArchiveEngine = Lunarium.Bot.Archive.Archiver.Archiver;
using Lunarium.Bot.Archive.Notifier;
using Lunarium.Common.Caching;

using FrameworkNotifier = Lunarium.Bot.Framework.Notifier;

namespace Lunarium.Bot.Archive;

public sealed class ArchiveBotService : IHostedService
{

    private readonly EnvVar _token;
    private readonly ILogger _logger;
    private readonly IServiceProvider _privateServices;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly DiscordLogAdapter _discordLogAdapter;
    private readonly HealthMonitor _healthMonitoring;
    private readonly FrameworkNotifier _notifier;
    private readonly CommandSynchronizer _commandSynchronizer;
    private readonly ArchiverManager _archiverManager;
    private bool _isFirstTime = true;
    
    // 构造函数
    public ArchiveBotService(EnvVar token, ILogger logger, ProxyUrl? proxyUrl)
    {
        _token = token;
        // 私有容器里的 logger 统一带 RootContext 前缀，各服务再拼自己的上下文
        _logger = logger.ForContext(LogConfigurator.RootContext);
        _logger.Info("初始化 ArchiveBot 服务...");
        // 构建私有DI容器
        _privateServices = ConfigurePrivateServices(_logger, proxyUrl);

        // 从私有容器中解析出核心服务以方便使用
        // ==== 核心部分 ====
        _discordLogAdapter = _privateServices.GetRequiredService<DiscordLogAdapter>();
        _configLoader = _privateServices.GetRequiredService<ConfigLoader<ArchiveConfig>>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        _notifier = _privateServices.GetRequiredService<FrameworkNotifier>();
        _commandSynchronizer = _privateServices.GetRequiredService<CommandSynchronizer>();
        // ==== 业务部分 ====
        _archiverManager = _privateServices.GetRequiredService<ArchiverManager>();
        _logger.Info("✅ ArchiveBot 服务初始化完成。");
    }

    // 封装的私有服务配置方法
    private IServiceProvider ConfigurePrivateServices(ILogger logger, ProxyUrl? proxyUrl)
    {
        _logger.Info("配置 ArchiveBot 的私有服务容器...");

        var services = new ServiceCollection()
            // ==== 核心部分 ====
            // 注册日志
            .AddSingleton(logger)
            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader<ArchiveConfig>>()
            // 注册 Discord 客户端，并按需配置代理
            // 保持默认意图（AllUnprivileged）：Discord 对「直接提及 Bot」的消息始终下发正文，
            // 所以 @Bot 文本命令不需要 MessageContent 特权意图；
            // 只有要读「未提及 Bot」的消息正文时才需要在开发者后台开启该意图
            .AddDiscordSocketClient(proxyUrl)
            // 注册交互服务
            .AddInteractionService()
            // 注册可选支持服务
            .AddDiscordLogAdapter()
            .AddStatusNotifier()
            .AddCommandSynchronizer()
            .AddHealthMonitor()
            .AddSessionManager()
            // ==== 存储与限流 ====
            .AddSingleton(provider => new CacheStore(
                cacheNamespace: "archive-status-v2",
                logError: ex => provider.GetRequiredService<ILogger>().Error(ex, "状态报告 CacheStore 异常"),
                allowCorruptRebuild: true))
            .AddSingleton(provider => RateLimitedExecutor.CreateSmoothPerRequest(
                concurrency: 4,
                qps: 40,
                channelSize: 4096,
                onError: ex => provider.GetRequiredService<ILogger>().Error(ex, "归档限速器异常")))
            // ==== 业务部分 ====
            .AddSingleton(provider => new JsonDocumentStore<PinnedMonitoringDocument>(
                documentName: "PinnedMonitoring",
                typeInfo: PinnedJsonContext.Default.PinnedMonitoringDocument,
                logError: ex => provider.GetRequiredService<ILogger>().Error(ex, "置顶监控缓存持久化失败"),
                allowCorruptRebuild: true))
            .AddSingleton<NotifierManager>()
            .AddSingleton<PinnedMonitoring>()
            .AddSingleton<ArchiveEngine>()
            .AddSingleton<ArchiverManager>()
            // 构建 DI
            .BuildServiceProvider();
        
        return services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 ArchiveBot 服务...");

        // 注册事件处理器
        _client.Log += _discordLogAdapter.Handle;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        // 消息命令
        _client.MessageReceived += HandleMessageCommandAsync;

        // 加载命令模块，使用私有DI容器
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _privateServices);
        
        _logger.Info("斜杠命令模块已加载。");
        
        // 登录和启动
        await _client.LoginAsync(TokenType.Bot, _token.Value);
        await _client.StartAsync();

        _logger.Info("ArchiveBot 服务已启动，等待连接完成...");
        
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 ArchiveBot 服务...");

        // 取消所有事件订阅
        _client.Log -= _discordLogAdapter.Handle;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        _client.MessageReceived -= HandleMessageCommandAsync;

        _healthMonitoring.RemoveConnectEventHandler();

        await _archiverManager.StopAsync(CancellationToken.None);

        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        
        _logger.Info("✅ ArchiveBot 服务已停止。");
    }

    private async Task ReadyAsync()
    {
        try
        {
            _logger.Info("🎉 Bot 已就绪！已连接为: {Username}#{Discriminator}", _client.CurrentUser.Username, _client.CurrentUser.Discriminator);
            if (_isFirstTime == true)
            {
                // 注册连接事件处理器(否则连接状态队列恒为空)
                _healthMonitoring.AddConnectEventHandler();

                // 启动归档调度（内部会先启动通知器，再进入周期循环）
                await _archiverManager.StartAsync(CancellationToken.None);
                _isFirstTime = false;

                // 首次登录：把已加载的斜杠命令同步到各部署服务器
                await _commandSynchronizer.SyncCommandsToDeployGuildsAsync(
                    _configLoader.Value.DeployGuildConfig.Keys.ToList());

                // 登录消息
                await _notifier.SendLoginNotificationAsync(
                    _configLoader.Value.DeployerConfig.StatusChannelId,
                    _configLoader.Value.DeployGuildConfig.Keys.ToList());
            }
            else
            {
                await _notifier.ReConnect(
                    _configLoader.Value.DeployerConfig.MaintainerId,
                    _healthMonitoring);
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ReadyAsync 出现异常:");
        }

    }

    private async Task HandleMessageCommandAsync(SocketMessage message)
    {
        try
        {
            await _commandSynchronizer.HandleMessageAsync(
                message,
                _configLoader.Value.DeployerConfig.MaintainerId,
                _configLoader.Value.DeployGuildConfig.Keys.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理文本消息命令失败");
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            var result = await _interactions.ExecuteCommandAsync(context, _privateServices);

            if (!result.IsSuccess)
            {
                // InteractionService 对未匹配/解析失败不会主动响应，也不会抛异常；
                // 这里显式记录，避免出现"客户端报错但日志无输出"的情况。
                _logger.Warning("交互执行失败: Type={Type}, Error={Error}, Reason={Reason}",
                    interaction.Type, result.Error, result.ErrorReason ?? "(none)");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理交互时发生错误");
        }
    }
}