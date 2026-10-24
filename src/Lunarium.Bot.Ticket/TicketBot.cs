using Lunarium.Common;
using Lunarium.Common.Net;
using Lunarium.Logging;
using Lunarium.Bot.Framework.Health;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Ticket.TicketService;
using Lunarium.Bot.Ticket.AppInteraction;
using Lunarium.Bot.AI;

using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

namespace Lunarium.Bot.Ticket;

public class TicketBotService : IHostedService
{
    private readonly EnvVar _token;
    private readonly ILogger _logger;
    private readonly IServiceProvider _privateServices;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly DiscordLogAdapter _discordLogAdapter;
    private readonly HealthMonitor _healthMonitoring;
    private readonly Notifier _notifier;
    private readonly CommandSynchronizer _commandSynchronizer;
    private readonly TicketManager _ticketManager;
    private readonly TicketMaintenanceService _maintenance;
    private readonly IAiClient _aiClient;
    private bool _isFirstTime = true;

    public TicketBotService(EnvVar token, ILogger logger, ProxyUrl? proxyUrl)
    {
        _token = token;
        _logger = logger.ForContext(LogConfigurator.RootContext);
        _logger.Info("初始化 TicketBotService...");
        // 构建私有DI容器
        _privateServices = ConfigurePrivateServices(proxyUrl, _logger);
        // 从私有DI容器中取出服务
        _discordLogAdapter = _privateServices.GetRequiredService<DiscordLogAdapter>();
        _configLoader = _privateServices.GetRequiredService<ConfigLoader<TicketConfig>>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        _notifier = _privateServices.GetRequiredService<Notifier>();
        _commandSynchronizer = _privateServices.GetRequiredService<CommandSynchronizer>();
        _ticketManager = _privateServices.GetRequiredService<TicketManager>();
        _maintenance = _privateServices.GetRequiredService<TicketMaintenanceService>();
        _aiClient = _privateServices.GetRequiredService<IAiClient>();
    }

    private IServiceProvider ConfigurePrivateServices(ProxyUrl? proxyUrl, ILogger logger)
    {
        _logger.Info("配置 TicketBotService 的私有服务容器...");

        var services = new ServiceCollection()
            // ==== 核心部分 ====
            // 注册日志
            .AddSingleton(logger)
            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader<TicketConfig>>()
            // 注册 Discord 客户端，并按需配置代理
            // MessageContent 是特权意图（需在开发者后台开启），工单归档/对话抓取必须读取正文
            .AddDiscordSocketClient(proxyUrl, config =>
            {
                config.GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent;
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
            // 工单本地存储（纯 IO）与业务编排（发号 / 索引 / 落盘）
            .AddSingleton<TicketLocalData>()
            .AddSingleton<TicketCooldownStore>()
            .AddSingleton<TicketManager>()
            // 工单卡片投递与 LLM 审核建议
            .AddSingleton<TicketSuggestionService>()
            .AddSingleton<TicketCardService>()
            // 审核通过 / 拒绝的生命周期编排
            .AddSingleton<TicketWorkflowService>()
            // 归档：资源下载 / 对话抓取 / HTML+ZIP 打包
            .AddSingleton<TicketResourceDownloader>()
            .AddSingleton<TicketCrawler>()
            .AddSingleton<TicketArchiveService>()
            // 归档频道删除队列（跨重启）
            .AddSingleton<TicketDeletionQueue>()
            // 自动通过后的“无人回复”安抚（固定文案，定时驱动）
            .AddSingleton<TicketSootheService>()
            // 启动对账 + 自动通过 + 自动安抚后台任务
            .AddSingleton<TicketMaintenanceService>()
            .AddLunariumAi(proxyUrl, option =>
            {
                option.DefaultModel = "deepseek-flash";
                option.BaseUrl = new Uri("https://api.deepseek.com");
                option.ApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";
            })
            // 构建 DI
            .BuildServiceProvider();

        return services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 TicketBotService...");

        // 先加载本地工单（发号器对账 / 内存索引重建），再登录接交互
        await _ticketManager.InitializeAsync();

        // 明确打印 LLM 状态，避免"审核卡片没有 AI 建议"无从排查
        if (_aiClient.IsAvailable)
        {
            _logger.Info("LLM 审核建议: 已启用");
        }
        else
        {
            _logger.Warning(
                "LLM 审核建议: 未启用（未找到 API Key）。请设置环境变量 DEEPSEEK_API_KEY，或使用 /维护 设置ai密钥 在运行期设置");
        }

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

        _logger.Info("TicketBotService 已启动，等待连接完成...");

    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 TicketBot 服务...");

        // 取消所有事件订阅
        _client.Log -= _discordLogAdapter.Handle;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        _client.MessageReceived -= HandleMessageCommandAsync;

        _healthMonitoring.RemoveConnectEventHandler();

        await _maintenance.StopAsync(CancellationToken.None);

        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _logger.Info("✅ TicketBotService 服务已停止。");
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

                // 启动对账 + 自动通过后台任务（首次就绪后才启动，确保 guild 缓存已填充）
                await _maintenance.StartAsync(CancellationToken.None);

                // 登录消息
                await _notifier.SendLoginNotificationAsync(
                    _configLoader.Value.DeployerConfig.StatusChannel,
                    _configLoader.Value.DeployGuildConfig.Keys.ToList());

                // 未配置 AI Key 时，在状态频道留一个仅部署者/维护者可用的输入入口
                await NotifyAiKeyEntryIfNeededAsync();
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

    /// <summary>未配置 AI Key 时，在状态频道发一条带"输入"按钮的提示（仅首次就绪）。</summary>
    private async Task NotifyAiKeyEntryIfNeededAsync()
    {
        if (_aiClient.IsAvailable)
        {
            return;
        }

        await _notifier.SendKeyEntryMessageAsync(
            _configLoader.Value.DeployerConfig.StatusChannel,
            AiKeyEntry.ButtonCustomId,
            """
            LLM 审核建议需要 DeepSeek API Key。
            仅部署者/维护者可点击下方按钮输入；密钥只保存在进程内存中，重启后会自动回落到环境变量。
            """);
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
