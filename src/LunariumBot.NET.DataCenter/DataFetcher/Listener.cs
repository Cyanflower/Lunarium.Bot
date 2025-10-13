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
/// 它将相关事件暂存起来，在适当的时机（达到阈值或延迟时间到）统一更新到数据库，
/// 同时也能检测特定类型的消息（如包含特殊元数据的PNG或JSON文件）以触发用户订阅推送。
/// </summary>
public class Listener
{
    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;
    private readonly DataProcessService _dataProcessService;

    // 监控的频道ID列表
    private HashSet<ulong> ChannelIds => _configLoader.Config.GuildsConfig.ScanChannelIDs.Values.SelectMany(ids => ids).ToHashSet();
    // 论坛帖子ID缓存列表
    // K: ThreadId, V: FroumId
    private readonly ConcurrentDictionary<ulong, ulong> _forumThreadIds = new();

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
        _dataProcessService = dataProcessService;
    }

    /// <summary>
    /// 注册所有需要监听的 Discord 客户端事件处理器。
    /// 这些事件包括消息接收、帖子创建、更新、删除以及反应变化等。
    /// </summary>
    public void AddEventHandlers()
    {
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
                        _logger.Debug("⏭️ 收到非常规频道子区消息，跳过处理: [{GuildName}.{ParentChannelName}.{ThreadName}] 消息ID {MessageId}",
                            threadChannel.Guild.Name, threadChannel.ParentChannel.Name, threadChannel.Name, message.Id);
                        break;

                    // 最后的默认情况
                    default:
                        _logger.Warning("⚠️ 未能归类子区消息的父频道: [{GuildName}.{ParentChannelName}.{ThreadName}] 消息ID {MessageId}, 父频道类型 {ParentChannelType}",
                            threadChannel.Guild.Name, threadChannel.ParentChannel.Name, threadChannel.Name, message.Id, threadChannel.ParentChannel.GetType().Name);
                        break;
                }
                break;

            // 然后再处理基类 SocketTextChannel, 这里的顺序不能和 case SocketThreadChannel 反过来, 因为基类继承关系
            case SocketTextChannel textChannel when channel.GetType() == typeof(SocketTextChannel):
                // 使用 typeof 确保其运行时的确切类型必须是 SocketTextChannel
                // 而不是通过继承兼容 SocketTextChannel
                await _dataProcessService.TextChannelMessageProcess(message, textChannel);
                break;

            // 其他类型 - 非监控的频道类型
            case SocketStageChannel:
            case SocketVoiceChannel:
            case SocketNewsChannel:
                _logger.Debug("⏭️ 收到非常规频道消息，跳过处理: 消息ID {MessageId}, 频道ID {ChannelId}, 频道名称 {ChannelName}, 频道类型 {ChannelType}",
                    message.Id, channel.Id, (channel as IGuildChannel)?.Name, channel.GetType().Name);
                break;
            case SocketDMChannel:
                _logger.Debug("⏭️ 收到私聊消息，跳过处理: 作者ID {AuthorId}, 作者 {AuthorName}",
                    message.Author.Id, message.Author.GlobalName);
                break;

            // 最后的默认情况 - 未知频道类型
            default:
                _logger.Warning("⚠️ 未能归类消息所处的频道: 消息ID {MessageId}, 频道ID {ChannelId}, 频道名称 {ChannelName}, 频道类型 {ChannelType}",
                    message.Id, channel.Id, (channel as IGuildChannel)?.Name ?? "未知或无名称", channel.GetType().Name);
                break;
        }
    }
    #endregion

    #region 消息删除

    /// <summary>
    /// 处理单条消息删除事件。
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableMessageChannel">可能缓存的消息频道对象</param>
    public async Task MessageDeleteAsync(Cacheable<IMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        _logger.Debug("处理单条消息删除: 消息ID {MessageId}", cacheableMessage.Id);
        await _dataProcessService.MessagesDeleteProcess([cacheableMessage.Id]);
    }

    /// <summary>
    /// 处理批量消息删除事件（如频道清理）。
    /// </summary>
    /// <param name="cacheableMessages">可能缓存的消息集合</param>
    /// <param name="cacheableMessageChannel">可能缓存的消息频道对象</param>
    public async Task MessageBulkDeleteAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> cacheableMessages, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        var messageIds = cacheableMessages.Select(msg => msg.Id).ToList();
        _logger.Debug("处理批量消息删除: 数量 {Count}", messageIds.Count);
        await _dataProcessService.MessagesDeleteProcess(messageIds);
    }
    #endregion

    #region 帖子事件
    /// <summary>
    /// 处理帖子创建事件。
    /// 仅处理监控频道列表中的论坛频道。
    /// </summary>
    /// <param name="socketThreadChannel">创建的帖子频道对象</param>
    private async Task ThreadCreatedAsync(SocketThreadChannel socketThreadChannel)
    {
        // 检查父频道ID是否在监控的频道列表中
        if (ChannelIds.Contains(socketThreadChannel.ParentChannel.Id))
        {
            _logger.Debug("⚡ 检测到新帖子创建: [{GuildName}.{ChannelName}] 帖子ID {ThreadId}",
                socketThreadChannel.Guild.Name, socketThreadChannel.ParentChannel.Name, socketThreadChannel.Id);
            await _dataProcessService.CreateThreadProcessAsync(socketThreadChannel);
        }
    }

    /// <summary>
    /// 处理帖子更新事件。
    /// 仅处理监控频道列表中的论坛频道。
    /// </summary>
    /// <param name="cacheableThreadChannel">可能缓存的旧帖子对象</param>
    /// <param name="socketThreadChannel">更新后的帖子频道对象</param>
    private async Task ThreadUpdateAsync(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel, SocketThreadChannel socketThreadChannel)
    {
        // 检查父频道ID是否在监控的频道列表中
        if (ChannelIds.Contains(socketThreadChannel.ParentChannel.Id))
        {
            _logger.Debug("🔄 检测到帖子更新: [{GuildName}.{ChannelName}] 帖子ID {ThreadId}",
                socketThreadChannel.Guild.Name, socketThreadChannel.ParentChannel.Name, socketThreadChannel.Id);
            await _dataProcessService.UpdateThreadProcessAsync(socketThreadChannel);
        }
    }

    /// <summary>
    /// 处理帖子删除事件。
    /// </summary>
    /// <param name="cacheableChannel">可能缓存的帖子对象</param>
    public async Task DeleteThreadAsync(Cacheable<SocketThreadChannel, ulong> cacheableChannel)
    {
        _logger.Debug("🗑️ 检测到帖子删除: 帖子ID {ThreadId}", cacheableChannel.Id);
        await _dataProcessService.DeleteThreadProcessAsync(cacheableChannel.Id);
    }
    #endregion

    #region 反应事件
    /// <summary>
    /// 处理反应添加事件。
    /// 仅处理监控频道列表中帖子的首楼消息反应。
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableChannel">可能缓存的频道对象</param>
    /// <param name="socketReaction">添加的反应对象</param>
    public async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction socketReaction)
    {
        if (cacheableMessage.Id == cacheableChannel.Id)
        {
            if (_forumThreadIds.TryGetValue(cacheableChannel.Id, out var forumId))
            {
                if (!ChannelIds.Contains(forumId)) return;
            }
            else
            {
                var channel = cacheableChannel.HasValue ? cacheableChannel.Value : await cacheableChannel.DownloadAsync();

                

                _forumThreadIds.TryAdd(cacheableChannel.Id, channel.Id);
                if (!ChannelIds.Contains(channel.Id)) return;
            }
            _logger.Debug("👍 检测到首楼消息反应添加: 帖子ID {ThreadId}, 反应 {Emote}",
                cacheableChannel.Id, socketReaction.Emote.Name);
            await _dataProcessService.UpdateTheradReactionProcessAsync(cacheableChannel.Id);
        }
    }

    /// <summary>
    /// 处理反应移除事件。
    /// 仅处理监控频道列表中帖子的首楼消息反应。
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableChannel">可能缓存的频道对象</param>
    /// <param name="socketReaction">移除的反应对象</param>
    public async Task ReactionRemovedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction socketReaction)
    {
        // 获取消息所在频道对象
        var channel = cacheableChannel.HasValue ? cacheableChannel.Value : await cacheableChannel.DownloadAsync();

        // 如果是帖子频道且在监听目标集合范围内
        if (channel is SocketThreadChannel threadChannel && threadChannel.ParentChannel != null && ChannelIds.Contains(threadChannel.ParentChannel.Id))
        {
            // 仅处理首楼消息的反应（消息ID等于频道ID）
            if (cacheableMessage.Id == cacheableChannel.Id)
            {
                _logger.Debug("👎 检测到首楼消息反应移除: 帖子ID {ThreadId}, 反应 {Emote}",
                    threadChannel.Id, socketReaction.Emote.Name);
                await _dataProcessService.UpdateTheradReactionProcessAsync(threadChannel);
            }
        }
    }
    #endregion

    #region 频道事件
    /// <summary>
    /// 处理频道创建事件。
    /// 仅处理论坛频道的创建。
    /// </summary>
    /// <param name="socketChannel">创建的频道对象</param>
    public async Task ChannelCreatedAsync(SocketChannel socketChannel)
    {
        if (socketChannel is SocketForumChannel socketForumChannel)
        {
            _logger.Info("🆕 检测到新论坛频道创建: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
            await _dataProcessService.CreateOrUpdateChannelProcessAsync(socketForumChannel);
        }
    }

    /// <summary>
    /// 处理频道更新事件。
    /// 仅处理论坛频道的更新。
    /// </summary>
    /// <param name="socketBeforeChannel">更新前的频道对象</param>
    /// <param name="socketAfterChannel">更新后的频道对象</param>
    public async Task ChannelUpdatedAsync(SocketChannel socketBeforeChannel, SocketChannel socketAfterChannel)
    {
        if (socketAfterChannel is SocketForumChannel socketForumChannel)
        {
            _logger.Debug("🔄 检测到论坛频道更新: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
            await _dataProcessService.CreateOrUpdateChannelProcessAsync(socketForumChannel);
        }
    }

    /// <summary>
    /// 处理频道删除事件。
    /// 仅处理论坛频道的删除。
    /// </summary>
    /// <param name="socketChannel">删除的频道对象</param>
    public async Task ChannelDestroyedAsync(SocketChannel socketChannel)
    {
        if (socketChannel is SocketForumChannel socketForumChannel)
        {
            _logger.Info("🗑️ 检测到论坛频道删除: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
            await _dataProcessService.DeleteChannelProcessAsync(socketForumChannel);
        }
    }
    #endregion
}