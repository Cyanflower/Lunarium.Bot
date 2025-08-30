// Bots/SearchBot/SearchBot.cs (最终形态)
using System.Reflection;
using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;

using SearchBot.ThreadFetcher;
using SearchBot.Database;
using SearchBot.Utils;
using SearchBot.Scheduler;
using SearchBot.AppInteractionService;


namespace SearchBot;

public class SearchBotService : IHostedService
{
    // --- 私有字段 ---
    private readonly ILogger<SearchBotService> _logger;
    private readonly IServiceProvider _privateServices;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly ConfigLoader _configLoader;
    private readonly DatabaseService _databaseService;
    private readonly Scanner _scanner;
    private readonly Listener _listener;
    // [测试] 分析器
    private readonly DataAnalysis _analysis;
    
    // 构造函数
    public SearchBotService(ILogger<SearchBotService> logger, ILoggerFactory loggerFactory, IOptions<ProxyConfig> proxyOptions)
    {
        // 1. Logger
        _logger = logger;
        _logger.LogInformation("初始化 SearchBot 模块...");

        // 2. 构建私有DI容器
        _privateServices = ConfigurePrivateServices(proxyOptions.Value, loggerFactory);

        // 3. 从私有容器中解析出核心服务以方便使用
        _configLoader = _privateServices.GetRequiredService<ConfigLoader>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _databaseService = _privateServices.GetRequiredService<DatabaseService>();
        _scanner = _privateServices.GetRequiredService<Scanner>();
        _listener = _privateServices.GetRequiredService<Listener>();
        // [测试] 分析器
        _analysis = _privateServices.GetRequiredService<DataAnalysis>();
        _logger.LogInformation("✅ SearchBot 模块初始化完成。");
    }

    // 封装的私有服务配置方法
    private IServiceProvider ConfigurePrivateServices(ProxyConfig proxyConfig, ILoggerFactory loggerFactory)
    {
        _logger.LogInformation("配置 SearchBot 的私有服务...");

        var services = new ServiceCollection();

        services
            // 注册日志工厂，让私有容器可以创建Logger
            .AddSingleton(loggerFactory)
            .AddLogging()
            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader>()
            // 注册 Discord 客户端，并使用从全局传入的代理配置
            .AddSingleton<DiscordSocketClient>(provider =>
            {
                _logger.LogInformation("创建 DiscordSocketClient... 代理启用: {Enabled}", proxyConfig.Enable);
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

            // 注册分页管理器
            .AddSingleton<PaginatorManager>()
            // 注册命令冷却管理器
            .AddSingleton<UserCooldown>()
            // 注册搜索命令构建器
            .AddSingleton<SearchCriteriaBuilder>()

            // 注册交互服务
            .AddSingleton<InteractionService>(provider =>
                new InteractionService(provider.GetRequiredService<DiscordSocketClient>())
            )

            // 注册扫描服务
            .AddSingleton<Scanner>()
            // 注册监听器
            .AddSingleton<Listener>(provider => new Listener(
                provider.GetRequiredService<ILogger<Listener>>(),
                provider.GetRequiredService<DiscordSocketClient>(),
                provider.GetRequiredService<DatabaseService>(),
                provider.GetRequiredService<ConfigLoader>(),
                provider.GetRequiredService<Scanner>(),
                provider.GetRequiredService<ThreadProcessor>(),
                proxyConfig
            ))
            // 注册线程处理器
            .AddSingleton<ThreadProcessor>()
            // 注册数据库服务
            .AddSingleton<DatabaseService>()
            // 注册鉴权服务
            .AddSingleton<Authentication>()

            // [测试] 注册分析器
            .AddSingleton<DataAnalysis>()

            // 注册分词器
            .AddSingleton<ITokenizerService, JiebaTokenizerService>();


        
        return services.BuildServiceProvider();
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动 SearchBot 服务...");
        
        // 注册事件处理器
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        
        // 加载命令模块，使用私有DI容器
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _privateServices);
        _logger.LogInformation("斜杠命令模块已加载。");
        
        // 登录和启动
        await _client.LoginAsync(TokenType.Bot, _configLoader.Token);
        await _client.StartAsync();

        _logger.LogInformation("SearchBot 服务已启动，等待连接完成...");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止 SearchBot 服务...");
        _listener.RemoveEventHandlers();
        await _listener.ProcessPendingThreadsAsync();
        _databaseService.SaveDB();
        await _client.StopAsync();
    }

    private Task LogAsync(LogMessage log)
    {
        // 将 Discord.Net 的日志转换为 Microsoft.Extensions.Logging 日志
        switch (log.Severity)
        {
            case LogSeverity.Critical:
                _logger.LogCritical(log.Exception, "[Discord.Net] {Message}", log.Message);
                break;
            case LogSeverity.Error:
                _logger.LogError(log.Exception, "[Discord.Net] {Message}", log.Message);
                break;
            case LogSeverity.Warning:
                _logger.LogWarning("[Discord.Net] {Message}", log.Message);
                break;
            case LogSeverity.Info:
                _logger.LogInformation("[Discord.Net] {Message}", log.Message);
                break;
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                _logger.LogDebug("[Discord.Net] {Message}", log.Message);
                break;
        }
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        try
        {
            _logger.LogInformation("🎉 Bot 已就绪！已连接为: {Username}#{Discriminator}",
                _client.CurrentUser.Username, _client.CurrentUser.Discriminator);

            // 现在 bot 完全就绪，可以安全地同步命令
            await SyncCommandsAsync();


            var needSync = _databaseService.NeedSync;
            if (needSync)
            {
                _logger.LogInformation("⏳ 数据库时间戳过期，准备执行全量同步。");
                _scanner.FullSync();
            }
            else
            {
                _logger.LogInformation("✅ 数据库时间戳未过期，跳过全量同步。");
            }
            _listener.AddEventHandlers();
            // [测试] 分析器
            await _analysis.RunAndSaveAnalysisReportAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("ReadyAsync 出现异常: {ex}", ex);
        }

    }
    
    /// <summary>
    /// 同步命令到部署服务器列表
    /// </summary>
    private async Task SyncCommandsAsync()
    {
        try
        {
            _logger.LogInformation("正在同步命令到部署服务器列表...");
            _logger.LogInformation("同步命令列表:");
            foreach (var command in _interactions.SlashCommands)
            {
                _logger.LogInformation("  - {Name}: {Description}", command.Name, command.Description);
            }
            _logger.LogInformation("命令总数: {CommandCount}", _interactions.SlashCommands.Count);
            
            // 同步命令到部署服务器列表
            foreach (var guildId in _configLoader.DeployGuildIds)
            {
                var guild = _client.GetGuild(guildId);
                if (guild != null)
                {
                    _logger.LogInformation("正在同步命令到服务器: {GuildId} - {GuildName}", guildId, guild.Name);
                    await _interactions.RegisterCommandsToGuildAsync(guildId, true);
                    _logger.LogInformation("服务器 {GuildName} 命令同步完成。", guild.Name);
                }
                else
                {
                    _logger.LogWarning("⚠️ 无法找到服务器 {GuildId}，Bot可能不在该服务器中或服务器ID不正确", guildId);
                }
            }
            _logger.LogInformation("✅ 命令同步完成！SearchBot 服务已完全启动。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 同步命令时发生错误");
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
            _logger.LogError(ex, "处理交互时发生错误。");
        }
    }
}