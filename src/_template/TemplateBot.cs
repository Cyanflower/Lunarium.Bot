using System.Reflection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Lunarium.Common;
using Lunarium.Common.Net;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.Health;
using Lunarium.Bot.Template.Model;

using Lunarium.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lunarium.Bot.Template;

/// <summary>
/// Bot 宿主服务。采用双层 DI：
/// <list type="bullet">
///   <item>Host 容器（<see cref="Program"/>）：只放全局无关业务的东西。</item>
///   <item>私有容器（<see cref="ConfigurePrivateServices"/>）：Discord 客户端、配置、命令模块等。</item>
/// </list>
/// </summary>
public class TemplateBotService : IHostedService
{
    private readonly EnvVar _token;
    private readonly ILogger _logger;
    private readonly IServiceProvider _privateServices;
    private readonly ConfigLoader<TemplateConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly DiscordLogAdapter _discordLogAdapter;
    private readonly HealthMonitor _healthMonitoring;
    private readonly Notifier _notifier;
    private readonly CommandSynchronizer _commandSynchronizer;
    private bool _isFirstTime = true;

    public TemplateBotService(EnvVar token, ILogger logger, ProxyUrl? proxyUrl)
    {
        _token = token;
        _logger = logger.ForContext("TemplateBotService");
        _logger.Info("初始化 TemplateBotService...");

        _privateServices = ConfigurePrivateServices(proxyUrl, _logger);

        _discordLogAdapter = _privateServices.GetRequiredService<DiscordLogAdapter>();
        _configLoader = _privateServices.GetRequiredService<ConfigLoader<TemplateConfig>>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        _notifier = _privateServices.GetRequiredService<Notifier>();
        _commandSynchronizer = _privateServices.GetRequiredService<CommandSynchronizer>();
    }

    private IServiceProvider ConfigurePrivateServices(ProxyUrl? proxyUrl, ILogger logger)
    {
        _logger.Info("配置私有服务容器...");

        return new ServiceCollection()
            // ==== 框架 ====
            .AddSingleton(logger)
            .AddSingleton<ConfigLoader<TemplateConfig>>()
            .AddDiscordSocketClient(proxyUrl, config =>
            {
                // 需要读消息正文时必须用 MessageContent/MessageContentIntent（开发者后台开启的特权意图）
                config.GatewayIntents = GatewayIntents.All;
            })
            .AddInteractionService()
            .AddDiscordLogAdapter()
            .AddStatusNotifier()
            .AddCommandSynchronizer()
            .AddHealthMonitor()
            .AddSessionManager()
            // ==== 业务（在此登记）====
            // .AddSingleton<MyFeatureService>()
            .BuildServiceProvider();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 TemplateBotService...");

        _client.Log += _discordLogAdapter.Handle;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        _client.MessageReceived += HandleMessageCommandAsync;

        // 命令模块从当前程序集加载，使用私有容器注入依赖
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _privateServices);
        _logger.Info("斜杠命令模块已加载。");

        await _client.LoginAsync(TokenType.Bot, _token.Value);
        await _client.StartAsync();

        _logger.Info("TemplateBotService 已启动，等待连接完成...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 TemplateBot 服务...");

        _client.Log -= _discordLogAdapter.Handle;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        _client.MessageReceived -= HandleMessageCommandAsync;

        _healthMonitoring.RemoveConnectEventHandler();

        await _client.StopAsync();

        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _logger.Info("✅ TemplateBotService 已停止。");
    }

    private async Task ReadyAsync()
    {
        try
        {
            _logger.Info("🎉 Bot 已就绪: {User}#{Discriminator}",
                _client.CurrentUser.Username, _client.CurrentUser.Discriminator);

            if (_isFirstTime)
            {
                // 必须注册一次，否则连接状态队列恒为空（健康日志会返回空）
                _healthMonitoring.AddConnectEventHandler();
                _isFirstTime = false;

                var guildIds = _configLoader.Value.DeployGuildConfig.Keys.ToList();

                await _commandSynchronizer.SyncCommandsToDeployGuildsAsync(guildIds);

                await _notifier.SendLoginNotificationAsync(
                    _configLoader.Value.DeployerConfig.StatusChannel,
                    guildIds);
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
            _logger.Error(ex, "ReadyAsync 出现异常");
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
                // 显式记录，避免"客户端报错但日志无输出"。
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
