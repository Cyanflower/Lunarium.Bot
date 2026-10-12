// .NET 核心库
using System.Text;
using System.Net;
using System.Reflection;
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
using Lunarium.Logging;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.Extensions.CacheStore;

// 项目内命名空间
using LunariumBot.Net.Archive.Model;
using LunariumBot.Net.Archive.AppInteractionService;
using LunariumBot.Net.Archive.Utils;
using LunariumBot.Net.Archive.Archiver;

namespace LunariumBot.Net.Archive;

public sealed class ArchiveBotService : IHostedService
{
    // --- token ---
    private readonly string _token;
    // --- 私有依赖服务 ---
    private readonly ILogger _logger;
    private readonly IServiceProvider _privateServices;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly ConfigLoader _configLoader;
    private bool _isFirstTime = true;
    private readonly AuthUtils _authUtils;
    private readonly ArchiverService _archiverService;
    private readonly HealthMonitor _healthMonitoring;
    private readonly HashSet<string> _reRegisterCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "重新注册",
        "re-registration",
        "reregister"
    };
    
    // 构造函数
    public ArchiveBotService(string token, ILogger runtimeRootLogger, ProxyConfig proxyConfig)
    {
        _token = token;
        // 本地日志上下文
        _logger = runtimeRootLogger.ForContext("ArchiveBotService");
        _logger.Info("初始化 ArchiveBot 服务...");
        // 构建私有DI容器
        _privateServices = ConfigurePrivateServices(proxyConfig, _logger);

        // 3. 从私有容器中解析出核心服务以方便使用
        _configLoader = _privateServices.GetRequiredService<ConfigLoader>();
        _client = _privateServices.GetRequiredService<DiscordSocketClient>();
        _interactions = _privateServices.GetRequiredService<InteractionService>();
        _authUtils = _privateServices.GetRequiredService<AuthUtils>();
        _archiverService = _privateServices.GetRequiredService<ArchiverService>();
        _healthMonitoring = _privateServices.GetRequiredService<HealthMonitor>();
        _logger.Info("✅ ArchiveBot 服务初始化完成。");
    }

    // 封装的私有服务配置方法
    private IServiceProvider ConfigurePrivateServices(ProxyConfig proxyConfig, ILogger logger)
    {
        _logger.Info("配置 ArchiveBot 的私有服务容器...");

        var services = new ServiceCollection()

            // 注册日志
            .AddSingleton(logger)
            // 注册 ConfigLoader
            .AddSingleton<ConfigLoader>()
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

            // 注册交互服务
            .AddSingleton<InteractionService>(provider =>
                new InteractionService(provider.GetRequiredService<DiscordSocketClient>())
            )

            .AddSingleton<PriorityArchiveDataLoader>()

            // 状态报告消息账本：
            // - CacheStore 必须由容器创建（传入现成实例的话容器不会释放它，防抖队列里的最后写入会丢失）
            // - 两者都注册在 ArchiverService 之前：容器按注册逆序释放，从而「先停归档、后刷盘」
            .AddSingleton(provider => new CacheStore(
                cacheNamespace: "archive-status-log",
                logError: ex => provider.GetRequiredService<ILogger>().Error(ex, "状态报告账本 CacheStore 异常"),
                allowCorruptRebuild: true))
            .AddSingleton<GuildLogMessageRegistry>()

            .AddSingleton<ArchiverService>()

            // 注册鉴权服务
            .AddSingleton<AuthUtils>()
            
            .AddSingleton<HealthMonitor>()

            .BuildServiceProvider();
        
        return services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("启动 ArchiveBot 服务...");

        // 注册事件处理器
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        // 消息命令
        _client.MessageReceived += HandleMessageCommandAsync;

        // 加载命令模块，使用私有DI容器
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _privateServices);
        
        _logger.Info("斜杠命令模块已加载。");
        
        // 登录和启动
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        _logger.Info("ArchiveBot 服务已启动，等待连接完成...");
        
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("正在停止 ArchiveBot 服务...");

        // 取消所有事件订阅
        _client.Log -= LogAsync;
        _client.Ready -= ReadyAsync;
        _client.InteractionCreated -= HandleInteractionAsync;
        _client.MessageReceived -= HandleMessageCommandAsync;

        _healthMonitoring.RemoveConnectEventHandler();

        await _client.StopAsync();

        // 释放私有服务容器
        if (_privateServices is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        
        _logger.Info("✅ ArchiveBot 服务已停止。");
    }

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
                // 注册连接事件处理器(否则连接状态队列恒为空)
                _healthMonitoring.AddConnectEventHandler();
                _isFirstTime = false;
                // 启动归档服务
                _archiverService.EnsureTimerIsRunning();
                // 登录消息
                await SendLoginNotificationAsync();
            }
            else
            {
                var loginEmbed = new EmbedBuilder()
                    .WithTitle("自动归档服务")
                    .WithAuthor(_client.CurrentUser)
                    .WithDescription($"""
                        ### BOT 客户端重新连接
                        登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                        检测到 BOT 客户端在运行时重新连接 Discord API 网关事件
                        """)
                    .Build();

                // 获取监控日志字符串
                var latencysLog = _healthMonitoring.GetLatencysLog(5);
                var connectStatesLog = _healthMonitoring.GetConnectStatesLog(5);

                _logger.Warning("BOT 客户端重新连接, 登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})");
                _logger.Warning("检测到 BOT 客户端在运行时重新连接 Discord API 网关事件");
                _logger.Warning(latencysLog);
                _logger.Warning(connectStatesLog);

                // 向维护者发送私信通知
                try
                {
                    var maintainer = await _client.GetUserAsync(_configLoader.Config.AuthConfig.MaintainerId);
                    if (maintainer is not null)
                    {
                        await maintainer.SendMessageAsync(embed: loginEmbed);
                        _logger.Info("已向维护者({MaintainerId})发送重连通知私信", _configLoader.Config.AuthConfig.MaintainerId);
                    }
                    else
                    {
                        _logger.Warning("无法获取维护者用户({MaintainerId}), 重连通知私信未发送", _configLoader.Config.AuthConfig.MaintainerId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "向维护者({MaintainerId})发送重连通知私信失败", _configLoader.Config.AuthConfig.MaintainerId);
                }
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ReadyAsync 出现异常:");
        }

    }
    
    private async Task SendLoginNotificationAsync()
    {
        var statusChannel = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.StatusChannel);
        if (statusChannel is IMessageChannel statusMessageChannel)
        {
            var guildInfoList = new List<(IGuild guild, int limit)>();
            var invalidConfig = new List<(ulong, int)>();
            foreach (var pair in _configLoader.Config.DeployGuildConfig)
            {
                var guild = _client.GetGuild(pair.Key);

                if (guild != null)
                {
                    guildInfoList.Add((guild, pair.Value));
                }
                else
                {
                    _logger.Warning($"警告: 机器人不在配置的服务器 {pair.Key} 中，已跳过。");
                    invalidConfig.Add((pair.Key, pair.Value));
                }
            }
            var loginLogStatusSB = new StringBuilder();
            loginLogStatusSB.AppendLine($"""
                ### 服务已启动
                登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                归档服务部署服务器:
                """);
            foreach (var guildInfo in guildInfoList)
            {
                loginLogStatusSB.AppendLine($"""
                    > - {guildInfo.guild.Name}: {guildInfo.limit}
                    > -# ID: {guildInfo.guild.Id}
                    """);
            }
            if (invalidConfig.Count > 0)
            {
                loginLogStatusSB.AppendLine("发现以下无效配置, 当前所登录 BOT 并不处于这些服务器中");
                foreach (var guildInfo in guildInfoList)
                {
                    loginLogStatusSB.AppendLine($"> - ID: {guildInfo.guild.Id}: {guildInfo.limit}");
                }
            }

            var embed = new EmbedBuilder()
                .WithTitle("自动归档服务")
                .WithDescription(loginLogStatusSB.ToString())
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await statusMessageChannel.SendMessageAsync(embed: embed);
            _logger.Info("成功发送登录消息通知");
        }
        else
        {
            _logger.Warning("ReadyAsync 出现异常: 配置的 Status 频道({channelId})不是可发消息频道类型(IMessageChannel)", _configLoader.Config.AuthConfig.StatusChannel);
        }
    }
    
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
        if (guildUser == null) return;
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
            foreach (var guildId in _configLoader.Config.DeployGuildConfig.Keys)
            {
                try
                {
                    _logger.Info("正在清除服务器 {guildId} 的命令...", userMessage.Author.Username);
                    var guild = _client.GetGuild(guildId);
                    if (guild != null)
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
            _logger.Error(ex, "执行重新注册命令时发生错误。");
            await userMessage.Channel.SendMessageAsync("执行重新注册命令时发生错误。");
        }
    }

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
                _logger.Info("  - {command.Name}: {command.Description}",command.Name, command.Description);
            }
            _logger.Info("命令总数: {_interactions.SlashCommands.Count}", _interactions.SlashCommands.Count);

            // 同步命令到部署服务器列表
            foreach (var guildId in _configLoader.Config.DeployGuildConfig.Keys)
            {
                var guild = _client.GetGuild(guildId);
                if (guild != null)
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
            _logger.Info("✅ 命令同步完成！ArchiveBot 服务已完全启动。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 同步命令时发生错误");
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
            _logger.Error(ex, "处理交互时发生错误。");
        }
    }
}