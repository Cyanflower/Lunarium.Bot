// .NET 核心库
using System.Collections.Concurrent;
using System.ComponentModel;
// 引入 System.IO
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
// NuGet 包依赖
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
// 解决方案内引用项目
using LunariumBot.Net.Logger;
// 项目内命名空间
using LunariumBot.Net.VoteBot.AuthUtils;
using LunariumBot.Net.VoteBot.InteractionsCache;
using LunariumBot.Net.VoteBot.Models;
using LunariumBot.Net.VoteBot.Utils;
using Polly;
using Polly.Retry;

namespace LunariumBot.Net.VoteBot.Listener;

public class ListenerService
{
    private readonly DiscordSocketClient _socketClient;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private int _messageCount = 0;

    private IUserMessage? _oldMessage;

    /// <summary>
    /// 事件处理器标志位
    /// </summary>
    private bool _handlersAttached = false;

    public ListenerService(ILogger logger, DiscordSocketClient socketClient, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("ListenerService");
        _socketClient = socketClient;
        _configLoader = configLoader;
        _httpRetryPolicy = Util.HttpRetryPolicyConfig(_logger);
    }

    /// <summary>
    /// 注册所有需要监听的 Discord 客户端事件处理器。
    /// </summary>
    public void AddEventHandlers()
    {
        if (_handlersAttached)
        {
            _logger.Debug("事件处理器已存在，跳过注册。");
            return;
        }
        try
        {
            _handlersAttached = true;
            _socketClient.MessageReceived += MessageReceivedHandler;
            _logger.Info("✅ 监听器事件处理方法已添加");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 监听器事件处理方法添加失败, 正在执行清理...");
            RemoveEventHandlers();
            throw;
        }
    }

    /// <summary>
    /// 移除所有需要监听的 Discord 客户端事件处理器。
    /// </summary>
    public void RemoveEventHandlers()
    {
        if (!_handlersAttached)
        {
            _logger.Debug("事件处理器未注册，无需移除。");
            return;
        }
        try
        {
            _socketClient.MessageReceived -= MessageReceivedHandler;
            _logger.Info("✅ 监听器事件处理方法已移除");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 监听器事件处理方法移除失败");
            throw;
        }
        finally
        {
            _handlersAttached = false;
        }
    }

    /// <summary>
    /// 设置旧消息
    /// </summary>
    /// <param name="message"></param>
    public void SetMessage(IUserMessage message)
    {
        _oldMessage = message;
    }

    private async Task MessageReceivedHandler(SocketMessage socketMessage)
    {
        if (socketMessage.Channel.Id != _configLoader.Config.DeployConfig.PublicChannel) return;
        if (socketMessage.Flags?.HasFlag(MessageFlags.Ephemeral) == true) return;
        var user = (IGuildUser)socketMessage.Author;
        if (socketMessage.Author.Id == _socketClient.CurrentUser.Id || user.RoleIds.Any(_configLoader.Config.AuthConfig.AdminRoleIds.Contains))
        {
            if (_messageCount >= 3)
            {
                _logger.Debug($"收到 MessageReceived 事件: ID: {socketMessage.Id}");
                _ = Task.Run(async () => { await MessageReceivedAsync(socketMessage); });
                _messageCount = 0;
            }
            else
            {
                _messageCount += 1;
                return;
            }
        }
        else
        {
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await socketMessage.DeleteAsync();
            });
        }
    }

    public async Task MessageReceivedAsync(SocketMessage? socketMessage = null)
    {
        try
        {
            var publicMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IMessageChannel)await _socketClient.GetChannelAsync(_configLoader.Config.DeployConfig.PublicChannel);
            });

            var newMessage = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return await publicMessageChannel.SendMessageAsync(components: Util.GetSubmitMessageComponent());
            });

            if (_oldMessage is null)
            {
                _oldMessage = newMessage;
            }
            else
            {
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await _oldMessage.DeleteAsync();
                });
                _oldMessage = newMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"MessageReceivedAsync Exception");
        }
    }
}