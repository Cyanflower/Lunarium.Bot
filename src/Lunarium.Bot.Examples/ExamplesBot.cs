// -----------------------------------------------------------------------------
// 文件: ExamplesBot.cs
// 描述: Bot 的核心业务逻辑服务。
//       实现了 IHostedService 接口，由 .NET Generic Host 负责管理其生命周期。
//       内部封装了一个私有的依赖注入容器，用于管理仅与 Bot 自身相关的服务，
//       如 Discord 客户端、交互服务、配置加载器等，实现了与主程序的解耦。
// -----------------------------------------------------------------------------

// .NET 核心库
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

// 解决方案内引用项目
using Lunarium.Bot.Logger;
using Lunarium.Bot.Framework;

// 项目内命名空间 (请确保创建 Model 和 AppInteractionService 文件夹及相应文件)
using Lunarium.Bot.Examples.Model;
using Lunarium.Bot.Examples.AppInteractionService;


namespace Lunarium.Bot.Examples;

public class ExamplesBotService : IHostedService
{
    // --- 核心配置与状态 ---
    private readonly string _token;
    private bool _isFirstTimeReady = true;

    // --- 核心服务 ---
    private readonly ILogger _logger; // 本地上下文的日志记录器
    private readonly IServiceProvider _privateServices; // 私有依赖注入容器
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;

    // --- 工具类与辅助服务 ---
    // (在这里声明从私有容器中解析出的服务，例如)
    private readonly ConfigLoader _configLoader;
    private readonly AuthUtils _authUtils;
    private readonly HealthMonitor _healthMonitoring;

    // --- 命令常量 ---
    private readonly HashSet<string> _reRegisterCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "重新注册",
        "re-registration",
        "reregister"
    };
    
    /// <summary>
    /// 构造函数，在服务被创建时由主 DI 容器调用。
    /// </summary>
    /// <param name="token">Bot 的 token。</param>
    /// <param name="runtimeRootLogger">从主 DI 容器注入的根日志记录器。</param>
    /// <param name="proxyConfig">从主 DI 容器注入的代理配置。</param>
    public ExamplesBotService(string token, ILogger runtimeRootLogger, ProxyConfig proxyConfig)
    {
        _token = token;
        
        // 1. 创建一个带有特定上下文的日志记录器实例，用于此服务内部。
        _logger = runtimeRootLogger.ForContext("ExamplesBotService");
        _logger.Info("初始化 ExamplesBot 服务...");

        // 2. 配置并构建一个仅供此服务内部使用的私有依赖注入容器。
        _privateServices = ConfigurePrivateServices(proxyConfig, _logger);

        // 3. 从私有容器中解析出核心服务，方便在整个类中访问。
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _configLoader = _privateServices.GetRequiredService<ConfigLoader>();
        _authUtils = _privateServices.GetRequiredService<AuthUtils>();
        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        
        _logger.Info("✅ ExamplesBot 服务初始化完成。");
    }

    /// <summary>
    /// 配置私有依赖注入容器。
    /// 这个容器只包含与 Bot 逻辑直接相关的服务。
    /// </summary>
    private IServiceProvider ConfigurePrivateServices(ProxyConfig proxyConfig, ILogger logger)
    {
        _logger.Info("正在配置 ExamplesBot 的私有服务容器...");

        var services = new ServiceCollection()
            // 注册从外部传入的日志记录器
            .AddSingleton(logger)
            
            // --- 在这里注册你的 Bot 专属服务 ---
            .AddSingleton<ConfigLoader>()
            .AddSingleton<AuthUtils>()
            // 示例: .AddSingleton<YourCustomService>()
            // --- ---------------------------- ---

            // 注册 Discord 客户端，并根据传入的 proxyConfig 应用代理设置。
            .AddSingleton<DiscordSocketClient>(provider =>
            {
                _logger.Info("正在创建 DiscordSocketClient... 代理启用: {EnableProxy}", proxyConfig.Enable);
                if (proxyConfig.Enable && !string.IsNullOrEmpty(proxyConfig.ProxyUrl))
                {
                    var proxy = new WebProxy(proxyConfig.ProxyUrl);
                    var config = new DiscordSocketConfig
                    {
                        // 分别为 REST 请求和 WebSocket 连接设置代理
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

            // 注册交互服务 (用于处理斜杠命令等)
            .AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>()))

            .AddSingleton<HealthMonitor>()
            
            .BuildServiceProvider();
        
        _logger.Info("✅ 私有服务容器配置完成。");
        return services;
    }

    /// <summary>
    /// IHostedService 接口的实现，在主机启动时调用。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在启动 ExamplesBot 服务...");

        // 注册 Discord 客户端的事件处理器
        _client.Log += LogAsync; // 将 Discord.Net 的日志桥接到 LunariumLogger
        _client.Ready += ReadyAsync; // 当 Bot 连接并准备就绪时触发
        _client.InteractionCreated += HandleInteractionAsync; // 处理所有交互事件 (如斜杠命令、按钮点击)
        _client.MessageReceived += HandleMessageCommandAsync; // 处理传统的 @bot 命令

        // 从当前程序集中加载所有交互命令模块，并使用私有 DI 容器来解析它们的依赖。
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _privateServices);
        _logger.Info("斜杠命令模块已加载。");
        
        // 登录并启动 Bot
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        _logger.Info("ExamplesBot 服务已启动，正在等待连接完成...");
    }

    /// <summary>
    /// IHostedService 接口的实现，在主机请求关闭时调用。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 ExamplesBot 服务...");
        
        // 取消事件订阅，防止在关闭过程中处理新的事件
        _client.MessageReceived -= HandleMessageCommandAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        _client.Ready -= ReadyAsync;
        _client.Log -= LogAsync;

        // 停止客户端连接
        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        _logger.Info("✅ ExamplesBot 服务已停止。");
    }

    /// <summary>
    /// 将 Discord.Net 的日志消息转发到 LunariumLogger。
    /// </summary>
    private Task LogAsync(LogMessage log)
    {
        // 根据 Discord.Net 的日志级别，调用 LunariumLogger 对应的方法。
        var logLevel = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Info,
            _ => LogLevel.Debug
        };
        _logger.Log(logLevel, "[Discord.Net] {Message}", context:"DiscordNet", ex:log.Exception, propertyValues:log.Message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 在 Bot 准备就绪后执行的初始化操作。
    /// </summary>
    private async Task ReadyAsync()
    {
        try
        {
            _logger.Info("🎉 Bot 已就绪！当前登录为: {Username}#{Discriminator}", _client.CurrentUser.Username, _client.CurrentUser.Discriminator);

            // 确保同步命令等一次性初始化操作只在首次连接成功时执行。
            if (_isFirstTimeReady)
            {
                // 将所有已加载的斜杠命令注册到 Discord。
                await SyncCommandsAsync();
                _isFirstTimeReady = false;
            }
            else
            {
                var loginEmbed = new EmbedBuilder()
                    .WithTitle("ExamplesBot")
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
                
                var channelCache = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogChannel) as IMessageChannel;
                if (channelCache is not null)
                {
                    await channelCache.SendMessageAsync(embed: loginEmbed);
                    await channelCache.SendFilesAsync(text:"<@867658156510150716>", attachments: new[] { latencyFile, connectStateFile });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("ReadyAsync 执行期间发生异常", ex);
        }
    }
    
    /// <summary>
    /// 处理所有传入的交互事件。
    /// </summary>
    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // 创建一个交互上下文
            var context = new SocketInteractionContext(_client, interaction);
            // 执行匹配的命令
            await _interactions.ExecuteCommandAsync(context, _privateServices);
        }
        catch (Exception ex)
        {
            _logger.Error("处理交互时发生异常", ex);
        }
    }

    /// <summary>
    /// 处理传统的、通过 @bot 触发的文本命令，主要用于管理员维护。
    /// </summary>
    private async Task HandleMessageCommandAsync(SocketMessage message)
    {
        // 过滤掉非用户消息和非 @bot 的消息
        if (message is not SocketUserMessage userMessage || 
            message.Author.IsBot || 
            !userMessage.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
        {
            return;
        }

        // 解析出命令文本
        var commandText = Regex.Replace(userMessage.Content, @"<@!?\d+>\s*", "").Trim();

        // 检查是否是 "重新注册" 命令
        if (!_reRegisterCommands.Contains(commandText))
        {
            return;
        }

        _logger.Info("用户 {Username} 触发了传统命令: {Command}", userMessage.Author.Username, commandText);

        // --- 权限检查 ---
        // 使用注入的 AuthUtils 服务进行权限验证。
        var guildUser = userMessage.Author as IGuildUser;
        if (guildUser == null || !_authUtils.IsAdmin(guildUser.RoleIds))
        {
            _logger.Warning("权限不足: 用户 {Username} 尝试执行管理员命令失败。", userMessage.Author.Username);
            await userMessage.ReplyAsync("权限不足，此为管理员命令。");
            return;
        }
        
        await userMessage.ReplyAsync("权限检查通过，正在刷新所有斜杠命令... 这可能需要一点时间。");

        // 完全重新注册所有命令
        try
        {
            var resultMessage = new StringBuilder();
            
            // 7. 清除各个服务器中的命令
            // 如果希望命令只部署到特定服务器，请使用此逻辑。
            // 否则，请注释掉此部分，并使用 RegisterCommandsGloballyAsync。
            // 因为本项目为示例
            // 这里只作为 Example
            // 实际逻辑可以参考解决方案内的其他具体项目的真正实现
            // foreach (var guildId in _configLoader.Config.DeployGuildConfig.Keys)
            // {
            //     try
            //     {
            //         var guild = _client.GetGuild(guildId);
            //         if (guild != null)
            //         {
            //             await guild.BulkOverwriteApplicationCommandAsync([]);
            //             _logger.Info("服务器 {GuildName} ({GuildId}) 的命令已清除。", guild.Name, guildId);
            //             resultMessage.AppendLine($"- 服务器 `{guild.Name}` 的命令已清除。");
            //         }
            //         else
            //         {
            //              _logger.Warning("无法找到配置的服务器 {GuildId}，跳过命令清除。", guildId);
            //         }
            //     }
            //     catch(Exception ex)
            //     {
            //         _logger.Error(ex, "清除服务器 {GuildId} 命令时发生错误。", guildId);
            //         resultMessage.AppendLine($"- 清除服务器 `{guildId}` 命令时出错: `{ex.Message.Slice(100)}`");
            //     }
            // }

            await SyncCommandsAsync();
            
            await userMessage.Channel.SendMessageAsync($"✅ 操作完成！\n{resultMessage}");
        }
        catch (Exception ex)
        {
            _logger.Error("执行“重新注册”命令时发生严重错误。", ex);
            await userMessage.Channel.SendMessageAsync("❌ 执行命令时发生错误，请检查日志。");
        }
    }

    /// <summary>
    /// 同步斜杠命令到 Discord。
    /// </summary>
    private async Task SyncCommandsAsync()
    {
        try
        {
            _logger.Info("正在同步斜杠命令...");
            
            // --- 根据 BOT 部署策略选择一种方式 ---

            // 策略1: 全局部署
            // 命令会注册到所有 Bot 所在的服务器，但更新可能需要长达1小时。
            await _interactions.RegisterCommandsGloballyAsync(true);

            // 策略2: 按服务器部署
            // 命令会立即注册到指定的服务器。
            // if (_configLoader.Config.DeployGuildConfig == null || !_configLoader.Config.DeployGuildConfig.Any())
            // {
            //     _logger.Warning("配置文件中未指定任何部署服务器 (DeployGuildConfig)，将不会注册任何服务器命令。");
            // }
            // else
            // {
            //     foreach (var guildId in _configLoader.Config.DeployGuildConfig.Keys)
            //     {
            //         _logger.Info("正在向服务器 {GuildId} 注册命令...", guildId);
            //         await _interactions.RegisterCommandsToGuildAsync(guildId, true);
            //         _logger.Info("服务器 {GuildId} 命令注册完成。", guildId);
            //     }
            // }
            
            _logger.Info("✅ 斜杠命令同步完成！");
        }
        catch (Exception ex)
        {
            _logger.Error("❌ 同步斜杠命令时发生错误", ex);
        }
    }
}