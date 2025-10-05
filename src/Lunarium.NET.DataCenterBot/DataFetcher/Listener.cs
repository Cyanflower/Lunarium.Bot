// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;
using SixLabors.ImageSharp;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;
using LunariumBot.Net.DataCenter.DataProcess;

namespace LunariumBot.Net.DataCenter.DataFetcher;

/// <summary>
/// 负责监听 Discord 服务器中的实时事件（如消息、帖子创建/更新/删除等），
/// 并通过防抖机制（Debouncer）对这些事件进行高效、批量的处理。
/// 它将相关事件暂存起来，在适当的时机（达到阈值或延迟时间到）统一更新到数据库，
/// 同时也能检测特定类型的消息（如包含特殊元数据的PNG或JSON文件）以触发用户订阅推送。
/// </summary>
public class Listener
{
    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;

    // 监控的频道ID列表
    private readonly HashSet<ulong> _channelIds;

    private readonly DataProcessService _dataProcessService;

    public Listener(
        ILogger logger,
        DiscordSocketClient client,
        ConfigLoader configLoader,
        DataProcessService dataProcessService
    )
    {
        _logger = logger.ForContext("Listener");
        _client = client;
        _configLoader = configLoader;
        _channelIds = _configLoader.Config.GuildsConfig.ScanChannelIDS.Values.SelectMany(ids => ids).ToHashSet();
        _dataProcessService = dataProcessService;
    }

    /// <summary>
    /// 注册所有需要监听的 Discord 客户端事件处理器。
    /// 这些事件包括消息接收、帖子创建、更新、删除以及反应变化等。
    /// </summary>
    public void AddEventHandlers()
    {
        // _client.MessageReceived += CreatorAutomatic;
        _client.MessageReceived += MessageReceivedAsync;
        _client.MessageDeleted += MessageDeleteAsync;
        _client.MessagesBulkDeleted += MessageBulkDeleteAsync;
        _client.ThreadCreated += ThreadCreatedAsync;
        _client.ThreadUpdated += ThreadUpdateAsync;
        _client.ThreadDeleted += DeleteThreadAsync;
        _client.ReactionAdded += ReactionAddedAsync;
        _client.ReactionRemoved += ReactionRemovedAsync;
        _client.ChannelCreated += ChannelCreatedAsync;
        _client.ChannelUpdated += ChannelUpdatedAsync;
        _client.ChannelDestroyed += ChannelDestroyedAsync;
        _logger.Info("✅ 监听器事件处理方法已添加。");
    }

    /// <summary>
    /// 注销所有已注册的 Discord 客户端事件处理器。
    /// 在服务关闭或重启时调用，以避免内存泄漏和不必要的事件处理。
    /// </summary>
    public void RemoveEventHandlers()
    {
        _client.MessageReceived -= MessageReceivedAsync;
        _client.MessageDeleted -= MessageDeleteAsync;
        _client.MessagesBulkDeleted -= MessageBulkDeleteAsync;
        _client.ThreadCreated -= ThreadCreatedAsync;
        _client.ThreadUpdated -= ThreadUpdateAsync;
        _client.ThreadDeleted -= DeleteThreadAsync;
        _client.ReactionAdded -= ReactionAddedAsync;
        _client.ReactionRemoved -= ReactionRemovedAsync;
        _client.ChannelCreated -= ChannelCreatedAsync;
        _client.ChannelUpdated -= ChannelUpdatedAsync;
        _client.ChannelDestroyed -= ChannelDestroyedAsync;
        _logger.Info("✅ 监听器事件处理方法已移除。");
    }

    #region Message
    /// <summary>
    /// 处理接收到的消息事件。
    /// </summary>
    /// <param name="message">接收到的消息对象。</param>

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        var channel = message.Channel;

        // 判断消息所处频道的类型
        switch (channel)
        {
            // 如果消息所在频道是 SocketThreadChannel
            case SocketThreadChannel threadChannel:
                switch (threadChannel.ParentChannel) // 嵌套处理父频道, 分辨其来自论坛/文字频道
                {
                    case SocketForumChannel parentForumChannel:
                        await _dataProcessService.ThreadMessageProcessAsync(message, threadChannel, parentForumChannel);
                        break;
                    case SocketTextChannel parentTextChannel when parentTextChannel.GetType() == typeof(SocketTextChannel):
                        // 使用 typeof 确保其运行时的确切类型必须是 SocketTextChannel
                        // 而不是通过继承兼容 SocketTextChannel
                        await _dataProcessService.ThreadMessageProcessAsync(message, threadChannel, parentTextChannel);
                        break;

                    // 其他类型
                    case SocketStageChannel:
                    case SocketVoiceChannel:
                    case SocketNewsChannel:
                        _logger.Debug("收到一条来自非常规频道消息, 略过处理, 消息ID:{MessageId}, 消息链接:{MessageUrl}, 消息频道ID:{ChannelId}, 消息频道名称:{ChannelName}, 消息父频道ID:{ParentChannelId}, 消息父频道名称:{ParentChannelName}, 消息父频道服务器ID:{GuildId}, 消息父频道服务器名称:{GuildName}",
                message.Id, message.GetJumpUrl(), threadChannel.Id, threadChannel.Name, threadChannel.ParentChannel.Id, threadChannel.ParentChannel.Name, threadChannel.Guild.Id, threadChannel.Guild.Name);
                        break;

                    // 最后的默认情况
                    default:
                        _logger.Warning("未能归类消息所处位置的父频道, 消息ID:{MessageId}, 消息链接:{MessageUrl}, 消息频道ID:{ChannelId}, 消息频道名称:{ChannelName}, 消息父频道ID:{ParentChannelId}, 消息父频道名称:{ParentChannelName}, 消息父频道类型:{ParentChannelType}, 消息父频道服务器ID:{GuildId}, 消息父频道服务器名称:{GuildName}",
                message.Id, message.GetJumpUrl(), threadChannel.Id, threadChannel.Name, threadChannel.ParentChannel.Id, threadChannel.ParentChannel.Name, threadChannel.ParentChannel.GetType().Name, threadChannel.Guild.Id, threadChannel.Guild.Name);
                        break;
                }
                break;

            // 然后再处理基类 SocketTextChannel, 这里的顺序不能和 case SocketThreadChannel 反过来, 因为基类继承关系
            case SocketTextChannel textChannel when channel.GetType() == typeof(SocketTextChannel):
                // 使用 typeof 确保其运行时的确切类型必须是 SocketTextChannel
                // 而不是通过继承兼容 SocketTextChannel
                await _dataProcessService.TextChannelMessageProcess(message, textChannel);
                break;

            // 其他类型
            case SocketStageChannel:
            case SocketVoiceChannel:
            case SocketNewsChannel:
                _logger.Debug("收到一条来自非常规频道消息, 略过处理, 消息ID:{MessageId}, 消息链接:{MessageUrl}, 消息频道ID:{ChannelId}, 消息频道名字:{ChannelName}, 消息频道类型:{ChannelType}",
            message.Id, message.GetJumpUrl(), channel.Id, (channel as IGuildChannel)?.Name, channel.GetType().Name);
                break;
            case SocketDMChannel:
                _logger.Debug("收到一条来自私聊消息, 略过处理, 消息作者ID:{MessageAuthorId}, 消息作者:{MessageAuthorName}", message.Author.Id, message.Author.GlobalName);
                break;

            // 最后的默认情况
            default:
                _logger.Warning("未能归类消息所处的频道, 消息ID:{MessageId}, 消息链接:{MessageUrl}, 消息频道ID:{ChannelId}, 消息频道名字:{ChannelName}, 消息频道类型:{ChannelType}",
            message.Id, message.GetJumpUrl(), channel.Id, (channel as IGuildChannel)?.Name ?? "未知或无名称", channel.GetType().Name);
                break;
        }
    }
    #endregion

    public async Task MessageDeleteAsync(Cacheable<IMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        await _dataProcessService.MessagesDeleteProcess([cacheableMessage.Id]);
    }

    public async Task MessageBulkDeleteAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> cacheableMessages, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        var messageIds = cacheableMessages.Select(msg => msg.Id).ToList();
        await _dataProcessService.MessagesDeleteProcess(messageIds);
    }

    #region ThreadCreated
    /// <summary>
    /// 处理帖子创建事件。
    /// </summary>
    /// <param name="socketThreadChannel">创建或更新的帖子频道对象。</param>
    private async Task ThreadCreatedAsync(SocketThreadChannel socketThreadChannel)
    {
        // === 前置条件检查 (Guards) ===
        // 检查父频道ID是否在资源区频道的列表中。
        if (_channelIds.Contains(socketThreadChannel.ParentChannel.Id))
        {

            await _dataProcessService.CreateThreadProcessAsync(socketThreadChannel);
        }
        // 这个帖子不属于资源区的任何一个论坛频道, 忽略。
    }
    #endregion

    #region ThreadUpdated
    /// <summary>
    /// 处理帖子更新事件的重载版本。
    /// 将发生变动的帖子加入待处理队列，并触发防抖/批量处理逻辑。
    /// </summary>
    /// <param name="cacheableThreadChannel">可能在缓存中的帖子对象。</param>
    /// <param name="socketThreadChannel">更新后的帖子频道对象。</param>
    private async Task ThreadUpdateAsync(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel, SocketThreadChannel socketThreadChannel)
    {
        // === 前置条件检查 (Guards) ===
        // 检查父频道ID是否在资源区频道的列表中。
        if (_channelIds.Contains(socketThreadChannel.ParentChannel.Id))
        {
            await _dataProcessService.UpdateThreadProcessAsync(socketThreadChannel);
        }
        // 这个帖子不属于资源区的任何一个论坛频道, 忽略。
    }
    #endregion

    #region TrheadDelete
    /// <summary>
    /// 处理帖子删除事件
    /// </summary>
    /// <param name=""></param>
    /// <returns></returns>
    public async Task DeleteThreadAsync(Cacheable<SocketThreadChannel, ulong> cacheableChannel)
    {
        await _dataProcessService.DeleteThreadProcessAsync(cacheableChannel.Id);
    }

    #endregion

    #region ReactionAddedAsync
    public async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction socketReaction)
    {
        // 获取消息所在频道对象
        var channel = cacheableChannel.HasValue ? cacheableChannel.Value : await cacheableChannel.DownloadAsync();
        // 如果是帖子频道且在监听目标集合范围内
        if (channel is SocketThreadChannel threadChannel && threadChannel.ParentChannel != null && _channelIds.Contains(threadChannel.ParentChannel.Id))
        {
            // 是帖子的首楼消息
            if (cacheableMessage.Id == cacheableChannel.Id)
            {
                await _dataProcessService.UpdateTheradReactionProcessAsync(threadChannel);
            }
        }
    }
    #endregion

    #region ReactionRemovedAsync
    public async Task ReactionRemovedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction socketReaction)
    {
        // 获取消息所在频道对象
        var channel = cacheableChannel.HasValue ? cacheableChannel.Value : await cacheableChannel.DownloadAsync();
        // 如果是帖子频道且在监听目标集合范围内
        if (channel is SocketThreadChannel threadChannel && threadChannel.ParentChannel != null && _channelIds.Contains(threadChannel.ParentChannel.Id))
        {
            // 是帖子的首楼消息
            if (cacheableMessage.Id == cacheableChannel.Id)
            {
                await _dataProcessService.UpdateTheradReactionProcessAsync(threadChannel);
            }
        }
    }
    #endregion

    #region ChannelCreatedAsync
    public async Task ChannelCreatedAsync(SocketChannel socketChannel)
    {
        if (socketChannel is SocketForumChannel socketForumChannel)
        {
            await _dataProcessService.CreateOrUpdateChannelProcessAsync(socketForumChannel);
        }
    }
    #endregion

    #region ChannelUpdatedAsync
    public async Task ChannelUpdatedAsync(SocketChannel socketBeforeChannel, SocketChannel socketAfterChannel)
    {
        if (socketAfterChannel is SocketForumChannel socketForumChannel)
        {
            await _dataProcessService.CreateOrUpdateChannelProcessAsync(socketForumChannel);
        }
    }
    #endregion

    #region ChannelDestroyedAsync
    public async Task ChannelDestroyedAsync(SocketChannel socketChannel)
    {
        if (socketChannel is SocketForumChannel socketForumChannel)
        {
            await _dataProcessService.DeleteChannelProcessAsync(socketForumChannel);
        }
    }
    #endregion
}