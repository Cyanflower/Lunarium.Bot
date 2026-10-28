using Lunarium.Common;
using Lunarium.Common.Net;
using Lunarium.Logging;
using Lunarium.Bot.Framework.Health;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Lum.Features.AutoWelcome;
using Lunarium.Bot.Lum.Model;

using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

namespace Lunarium.Bot.Lum;

public class LumBotService : IHostedService
{
    private readonly EnvVar _token;
    private readonly ILogger _logger;
    private readonly IServiceProvider _privateServices;
    private readonly ConfigLoader<LumConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly DiscordLogAdapter _discordLogAdapter;
    private readonly HealthMonitor _healthMonitoring;
    private readonly Notifier _notifier;
    private readonly CommandSynchronizer _commandSynchronizer;
    private readonly AutoWelcomeService _autoWelcomeService;
    private bool _isFirstTime = true;

    public LumBotService(EnvVar token, ILogger logger, ProxyUrl? proxyUrl)
    {
        _token = token;
        _logger = logger.ForContext("LumBotService");
        _logger.Info("初始化 LumBotService...");
        // 构建私有DI容器
        _privateServices = ConfigurePrivateServices(proxyUrl, _logger);
        // 从私有DI容器中取出服务
        _discordLogAdapter = _privateServices.GetRequiredService<DiscordLogAdapter>();
        _configLoader = _privateServices.GetRequiredService<ConfigLoader<LumConfig>>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        _notifier = _privateServices.GetRequiredService<Notifier>();
        _commandSynchronizer = _privateServices.GetRequiredService<CommandSynchronizer>();
        _autoWelcomeService = _privateServices.GetRequiredService<AutoWelcomeService>();
    }

    private IServiceProvider ConfigurePrivateServices(ProxyUrl? proxyUrl, ILogger logger)
    {
        _logger.Info("配置 LumBotService 的私有服务容器...");

        var services = new ServiceCollection()
            // ==== 核心部分 ====
            // 注册日志
            .AddSingleton(logger)
            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader<LumConfig>>()
            // 注册 Discord 客户端，并按需配置代理
            // MessageContent 是特权意图（需在开发者后台开启），工单归档/对话抓取必须读取正文
            .AddDiscordSocketClient(proxyUrl, config =>
            {
                // 个人小服务器专用, 意图无需申请可直接在开发者后台开启
                config.GatewayIntents = GatewayIntents.All;
            })
            // 注册交互服务
            .AddInteractionService()
            // 注册可选支持服务
            .AddDiscordLogAdapter()
            .AddStatusNotifier()
            .AddCommandSynchronizer()
            .AddHealthMonitor()
            .AddSessionManager()
            // ==== 业务部分 ====
            // 出入自动化（入群欢迎 / 自动身份组 / 离群通知）
            .AddSingleton<AutoWelcomeService>()

            // 构建 DI
            .BuildServiceProvider();

        return services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 LumBotService...");

        // 注册事件处理器
        _client.Log += _discordLogAdapter.Handle;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        // 消息命令
        _client.MessageReceived += HandleMessageCommandAsync;
        // 出入自动化
        _client.UserJoined += _autoWelcomeService.HandleUserJoinedAsync;
        _client.UserLeft += _autoWelcomeService.HandleUserLeftAsync;

        // 加载命令模块，使用私有DI容器
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _privateServices);

        _logger.Info("斜杠命令模块已加载。");

        // 登录和启动
        await _client.LoginAsync(TokenType.Bot, _token.Value);
        await _client.StartAsync();

        _logger.Info("LumBotService 已启动，等待连接完成...");

    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 LumBot 服务...");

        // 取消所有事件订阅
        _client.Log -= _discordLogAdapter.Handle;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        _client.MessageReceived -= HandleMessageCommandAsync;
        _client.UserJoined -= _autoWelcomeService.HandleUserJoinedAsync;
        _client.UserLeft -= _autoWelcomeService.HandleUserLeftAsync;

        _healthMonitoring.RemoveConnectEventHandler();

        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _logger.Info("✅ LumBotService 服务已停止。");
    }

    private async Task ReadyAsync()
    {
        try
        {
            _logger.Info("🎉 Bot 已就绪！已连接为: {_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}", _client.CurrentUser.Username, _client.CurrentUser.Discriminator);
            if (_isFirstTime == true)
            {
                // 现在 bot 完全就绪，可以安全地同步命令

                // 注册连接事件处理器(否则连接状态队列恒为空)
                _healthMonitoring.AddConnectEventHandler();
                _isFirstTime = false;

                // 首次登录：把已加载的斜杠命令同步到各部署服务器
                await _commandSynchronizer.SyncCommandsToDeployGuildsAsync(
                    _configLoader.Value.DeployGuildConfig.Keys.ToList());

                // 登录消息
                await _notifier.SendLoginNotificationAsync(
                    _configLoader.Value.DeployerConfig.StatusChannel,
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