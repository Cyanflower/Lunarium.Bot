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
/// 负责监听 Discord 服务器中的实时事件(如消息、帖子创建/更新/删除等)，
/// 它将相关事件暂存起来，在适当的时机(达到阈值或延迟时间到)统一更新到数据库，
/// 同时也能检测特定类型的消息(如包含特殊元数据的PNG或JSON文件)以触发用户订阅推送。
/// </summary>
public class Listener
{
    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;
    private readonly DataProcessService _dataProcessService;

    /// <summary>
    /// 获取需要监控的所有频道ID集合。
    /// 从配置文件中读取并合并所有扫描频道ID。
    /// </summary>
    private HashSet<ulong> ChannelIds => _configLoader.ChannelSet;

    /// <summary>
    /// 论坛帖子ID缓存字典。
    /// 用于快速查找帖子所属的论坛频道ID，避免重复的异步下载操作。
    /// Key: 帖子(Thread)ID, Value: 所属论坛(Forum)ID
    /// </summary>
    private readonly ConcurrentDictionary<ulong, ulong> _forumThreadIds = new();
    
    /// <summary>
    /// 事件处理器标志位
    /// </summary>
    private bool _handlersAttached = false;

    /// <summary>
    /// 初始化监听器实例。
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="client">Discord WebSocket 客户端</param>
    /// <param name="configLoader">配置加载器</param>
    /// <param name="dataProcessService">数据处理服务</param>
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
        if (_handlersAttached) return;
        try
        {
            _handlersAttached = true;
            _client.MessageReceived += MessageReceivedHandler;
            _client.MessageUpdated += MessageUpdatedHandler;
            _client.MessageDeleted += MessageDeleteHandler;
            _client.MessagesBulkDeleted += MessageBulkDeleteHandler;
            _client.ThreadCreated += ThreadCreatedHandler;
            _client.ThreadUpdated += ThreadUpdateHandler;
            _client.ThreadDeleted += DeleteThreadHandler;
            _client.ReactionAdded += ReactionAddedHandler;
            _client.ReactionRemoved += ReactionRemovedHandler;
            _client.ChannelCreated += ChannelCreatedHandler;
            _client.ChannelUpdated += ChannelUpdatedHandler;
            _client.ChannelDestroyed += ChannelDestroyedHandler;
            _client.ThreadMemberJoined += ThreadMemberJoinedHandler;
            _client.ThreadMemberLeft += ThreadMemberLeftHandler;
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
    /// 注销所有已注册的 Discord 客户端事件处理器。
    /// 在服务关闭或重启时调用，以避免内存泄漏和不必要的事件处理。
    /// </summary>
    public void RemoveEventHandlers()
    {
        if (!_handlersAttached) return;
        try
        {
            _client.MessageReceived -= MessageReceivedHandler;
            _client.MessageUpdated -= MessageUpdatedHandler;
            _client.MessageDeleted -= MessageDeleteHandler;
            _client.MessagesBulkDeleted -= MessageBulkDeleteHandler;
            _client.ThreadCreated -= ThreadCreatedHandler;
            _client.ThreadUpdated -= ThreadUpdateHandler;
            _client.ThreadDeleted -= DeleteThreadHandler;
            _client.ReactionAdded -= ReactionAddedHandler;
            _client.ReactionRemoved -= ReactionRemovedHandler;
            _client.ChannelCreated -= ChannelCreatedHandler;
            _client.ChannelUpdated -= ChannelUpdatedHandler;
            _client.ChannelDestroyed -= ChannelDestroyedHandler;
            _client.ThreadMemberJoined -= ThreadMemberJoinedHandler;
            _client.ThreadMemberLeft -= ThreadMemberLeftHandler;
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

    #region Handler
    // ==================== 消息 ====================
    private Task MessageReceivedHandler(SocketMessage socketMessage)
    {
        _ = Task.Run(async () => { await MessageReceivedAsync(socketMessage); });
        return Task.CompletedTask;
    }
    private Task MessageUpdatedHandler(Cacheable<IMessage, ulong> cacheableMessage, SocketMessage socketMessage, ISocketMessageChannel socketMessageChannel)
    {
        _ = Task.Run(async () => { await MessageUpdatedAsync(cacheableMessage, socketMessage, socketMessageChannel); });
        return Task.CompletedTask;
    }
    private Task MessageDeleteHandler(Cacheable<IMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        _ = Task.Run(async () => { await MessageDeleteAsync(cacheableMessage, cacheableMessageChannel); });
        return Task.CompletedTask;
    }
    private Task MessageBulkDeleteHandler(IReadOnlyCollection<Cacheable<IMessage, ulong>> cacheableMessages, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        _ = Task.Run(async () => { await MessageBulkDeleteAsync(cacheableMessages, cacheableMessageChannel); });
        return Task.CompletedTask;
    }
    // ==================== 帖子 ====================
    private Task ThreadCreatedHandler(SocketThreadChannel socketThreadChannel)
    {
        _ = Task.Run(async () => { await ThreadCreatedAsync(socketThreadChannel); });
        return Task.CompletedTask;
    }
    private Task ThreadUpdateHandler(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel, SocketThreadChannel socketThreadChannel)
    {
        _ = Task.Run(async () => { await ThreadUpdateAsync(cacheableThreadChannel, socketThreadChannel); });
        return Task.CompletedTask;
    }
    private Task DeleteThreadHandler(Cacheable<SocketThreadChannel, ulong> cacheableChannel)
    {
        _ = Task.Run(async () => { await DeleteThreadAsync(cacheableChannel); });
        return Task.CompletedTask;
    }
    // ==================== 反应 ====================
    private Task ReactionAddedHandler(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction socketReaction)
    {
        _ = Task.Run(async () => { await ReactionAddedAsync(cacheableMessage, cacheableChannel, socketReaction); });
        return Task.CompletedTask;
    }

    private Task ReactionRemovedHandler(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction socketReaction)
    {
        _ = Task.Run(async () => { await ReactionRemovedAsync(cacheableMessage, cacheableChannel, socketReaction); });
        return Task.CompletedTask;
    }
    // ==================== 频道 ====================
    private Task ChannelCreatedHandler(SocketChannel socketChannel)
    {
        _ = Task.Run(async () => { await ChannelCreatedAsync(socketChannel); });
        return Task.CompletedTask;
    }
    private Task ChannelUpdatedHandler(SocketChannel socketBeforeChannel, SocketChannel socketAfterChannel)
    {
        _ = Task.Run(async () => { await ChannelUpdatedAsync(socketBeforeChannel, socketAfterChannel); });
        return Task.CompletedTask;
    }
    private Task ChannelDestroyedHandler(SocketChannel socketChannel)
    {
        _ = Task.Run(async () => { await ChannelDestroyedAsync(socketChannel); });
        return Task.CompletedTask;
    }
    // ==================== 成员 ====================
    private Task ThreadMemberJoinedHandler(SocketThreadUser socketThreadUser)
    {
        _ = Task.Run(async () => { await ThreadMemberJoinedAsync(socketThreadUser); });
        return Task.CompletedTask;
    }

    private Task ThreadMemberLeftHandler(SocketThreadUser socketThreadUser)
    {
        _ = Task.Run(async () => { await ThreadMemberLeftAsync(socketThreadUser); });
        return Task.CompletedTask;
    }
    #endregion


    #region ToProcess

    #region 消息事件
    /// <summary>
    /// 处理接收到的消息事件
    /// 根据消息所在频道的类型 (线程, 文字频道等) 分发到相应的处理逻辑
    /// </summary>
    /// <param name="socketMessage">接收到的消息对象。</param>
    private async Task MessageReceivedAsync(SocketMessage socketMessage)
    {
        // 判断消息所处频道的类型
        try
        {
            // 检查是否应该完全忽略此消息(根据排除规则)
            if (ShouldCompletelyIgnored(socketMessage))
            {
                _logger.Debug("⏭️ 消息已被过滤规则忽略: 消息ID {MessageId}", socketMessage.Id);
                return;
            }

            var channel = socketMessage.Channel;
            _logger.Debug("📨 收到消息: 消息ID {MessageId}, 频道ID {ChannelId}, 频道类型 {ChannelType}",
                socketMessage.Id, channel.Id, channel.GetType().Name);
            switch (channel)
            {
                // 如果消息所在频道是 SocketThreadChannel(线程/帖子)
                case SocketThreadChannel threadChannel:
                    _logger.Debug("🧵 处理线程消息: [{GuildName}.{ParentChannelName}.{ThreadName}]",
                        threadChannel.Guild.Name, threadChannel.ParentChannel?.Name ?? "未知父频道", threadChannel.Name);

                    switch (threadChannel.ParentChannel) // 嵌套处理父频道, 分辨其来自论坛/文字频道
                    {
                        // 父频道是论坛频道
                        case SocketForumChannel parentForumChannel:
                            _logger.Debug("📋 处理论坛帖子消息: 论坛 [{ForumName}], 帖子 [{ThreadName}]",
                                parentForumChannel.Name, threadChannel.Name);
                            await _dataProcessService.ThreadMessageProcessAsync(socketMessage, threadChannel, parentForumChannel);
                            break;

                        // 父频道是文字频道(确保精确类型匹配)
                        case SocketTextChannel parentTextChannel when parentTextChannel.GetType() == typeof(SocketTextChannel):
                            // 使用 typeof 确保其运行时的确切类型必须是 SocketTextChannel
                            // 而不是通过继承兼容 SocketTextChannel
                            _logger.Debug("💬 处理文字频道线程消息: 文字频道 [{TextChannelName}], 线程 [{ThreadName}]",
                                parentTextChannel.Name, threadChannel.Name);
                            await _dataProcessService.ThreadMessageProcessAsync(socketMessage, threadChannel, parentTextChannel);
                            break;

                        // 其他类型的父频道(语音、舞台、公告频道等)
                        case SocketStageChannel:
                        case SocketVoiceChannel:
                        case SocketNewsChannel:
                            _logger.Debug("⏭️ 收到非常规频道子区消息，视为无效: [{GuildName}.{ParentChannelName}.{ThreadName}] 消息ID {MessageId}",
                                threadChannel.Guild.Name, threadChannel.ParentChannel.Name, threadChannel.Name, socketMessage.Id);
                            break;

                        // 最后的默认情况 - 未识别的父频道类型
                        default:
                            _logger.Warning("⚠️ 未能归类子区消息的父频道: [{GuildName}.{ParentChannelName}.{ThreadName}] 消息ID {MessageId}, 父频道类型 {ParentChannelType}",
                                threadChannel.Guild.Name, threadChannel.ParentChannel?.Name ?? "未知", threadChannel.Name, socketMessage.Id, threadChannel.ParentChannel?.GetType().Name ?? "null");
                            break;
                    }
                    break;

                // 处理基类 SocketTextChannel(普通文字频道)
                // 注意：这里的顺序不能和 case SocketThreadChannel 反过来, 因为基类继承关系
                case SocketTextChannel textChannel when channel.GetType() == typeof(SocketTextChannel):
                    // 使用 typeof 确保其运行时的确切类型必须是 SocketTextChannel
                    // 而不是通过继承兼容 SocketTextChannel
                    _logger.Debug("💬 处理文字频道消息: [{GuildName}.{ChannelName}]",
                        textChannel.Guild.Name, textChannel.Name);
                    await _dataProcessService.TextChannelMessageProcess(socketMessage, textChannel);
                    break;

                // 其他类型 - 非监控的频道类型(语音、舞台、公告频道)
                case SocketStageChannel:
                case SocketVoiceChannel:
                case SocketNewsChannel:
                    _logger.Debug("⏭️ 收到非常规频道消息，视为无效: 消息ID {MessageId}, 频道ID {ChannelId}, 频道名称 {ChannelName}, 频道类型 {ChannelType}",
                        socketMessage.Id, channel.Id, (channel as IGuildChannel)?.Name ?? "未知", channel.GetType().Name);
                    break;

                // 私聊消息
                case SocketDMChannel:
                    _logger.Debug("⏭️ 收到私聊消息，视为无效: 作者ID {AuthorId}, 作者 {AuthorName}",
                        socketMessage.Author.Id, socketMessage.Author.GlobalName ?? socketMessage.Author.Username);
                    break;

                // 最后的默认情况 - 未知频道类型
                default:
                    _logger.Warning("⚠️ 未能归类消息所处的频道: 消息ID {MessageId}, 频道ID {ChannelId}, 频道名称 {ChannelName}, 频道类型 {ChannelType}",
                        socketMessage.Id, channel.Id, (channel as IGuildChannel)?.Name ?? "未知或无名称", channel.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理接收到的消息事件时发生错误, 消息ID: {MessageId}, 频道ID: {ChannelId}, 消息链接: {MessageJumpUrl}",
                socketMessage.Id, socketMessage.Channel.Id, socketMessage.GetJumpUrl());
        }
    }

    private async Task MessageUpdatedAsync(Cacheable<IMessage, ulong> cacheableMessage, SocketMessage socketMessage, ISocketMessageChannel socketMessageChannel)
    {
        try
        {
            // 只在消息id和所在线程id相等时处理 (帖子首楼消息id=帖子id)
            if (socketMessage.Id == socketMessageChannel.Id)
            {
                await _dataProcessService.ThreadFirstMessageUpdateProcess(socketMessage, socketMessageChannel.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理接收到的消息更新事件时发生错误, 消息ID: {MessageId}, 频道ID: {ChannelId}, 消息链接: {MessageJumpUrl}",
                socketMessage.Id, socketMessage.Channel.Id, socketMessage.GetJumpUrl());
        }
    }

    /// <summary>
    /// 处理单条消息删除事件
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableMessageChannel">可能缓存的消息频道对象</param>
    public async Task MessageDeleteAsync(Cacheable<IMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        try
        {
            _logger.Debug("🗑️ 处理单条消息删除: 消息ID {MessageId}, 频道ID {ChannelId}", 
                cacheableMessage.Id, cacheableMessageChannel.Id);
            await _dataProcessService.MessagesDeleteProcess([cacheableMessage.Id]);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理单条消息删除事件时发生错误, 消息ID: {MessageId}, 频道ID: {ChannelId}", 
                cacheableMessage.Id, cacheableMessageChannel.Id);
        }
    }

    /// <summary>
    /// 处理批量消息删除事件(如频道清理)
    /// </summary>
    /// <param name="cacheableMessages">可能缓存的消息集合</param>
    /// <param name="cacheableMessageChannel">可能缓存的消息频道对象</param>
    public async Task MessageBulkDeleteAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> cacheableMessages, Cacheable<IMessageChannel, ulong> cacheableMessageChannel)
    {
        try
        {
            var messageIds = cacheableMessages.Select(msg => msg.Id).ToList();
            _logger.Debug("🗑️ 处理批量消息删除: 数量 {Count}, 频道ID {ChannelId}", 
                messageIds.Count, cacheableMessageChannel.Id);
            await _dataProcessService.MessagesDeleteProcess(messageIds);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理批量消息删除事件时发生错误, 消息ID列表: {MessageIds}, 频道ID: {ChannelId}", 
                cacheableMessages.Select(cache => cache.Id).ToArray(), cacheableMessageChannel.Id);
        }
    }
    #endregion

    #region 帖子事件
    /// <summary>
    /// 处理帖子创建事件
    /// </summary>
    /// <param name="socketThreadChannel">创建的帖子频道对象</param>
    private async Task ThreadCreatedAsync(SocketThreadChannel socketThreadChannel)
    {
        try
        {
            _logger.Debug("⚡ 检测到新帖子创建: [{GuildName}.{ParentChannelName}] 帖子ID {ThreadId}, 帖子名称 [{ThreadName}]",
                socketThreadChannel.Guild.Name, socketThreadChannel.ParentChannel?.Name ?? "未知父频道", 
                socketThreadChannel.Id, socketThreadChannel.Name);
            
            // 获取父频道对象
            SocketChannel parentChannel;
            if (socketThreadChannel.ParentChannel is null)
            {
                _logger.Debug("父频道为空，尝试从 CategoryId 获取: CategoryId {CategoryId}", socketThreadChannel.CategoryId);
                
                if (!socketThreadChannel.CategoryId.HasValue)
                {
                    _logger.Warning("⚠️ 频道 {ThreadId} 的 CategoryId 无效, 放弃处理", socketThreadChannel.Id);
                    return;
                }
                parentChannel = _client.GetChannel(socketThreadChannel.CategoryId.Value);
                
                if (parentChannel is null)
                {
                    _logger.Warning("⚠️ 无法获取频道 {CategoryId}, 放弃处理", socketThreadChannel.CategoryId.Value);
                    return;
                }
            }
            else
            {
                parentChannel = socketThreadChannel.ParentChannel;
            }
            
            // 根据父频道类型分发处理
            if (parentChannel is SocketForumChannel socketForumChannel)
            {
                _logger.Debug("📋 父频道类型: 论坛频道 [{ForumName}]", socketForumChannel.Name);
                
                if (ShouldCompletelyIgnored(socketForumChannel))
                {
                    _logger.Debug("⏭️ 论坛频道 [{ForumName}] 被过滤规则忽略", socketForumChannel.Name);
                    return;
                }
                
                await _dataProcessService.CreateForumThreadProcessAsync(socketThreadChannel, socketForumChannel);
                _logger.Info("✅ 论坛帖子创建处理完成: [{GuildName}.{ForumName}.{ThreadName}]", 
                    socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name);
            }
            else if (parentChannel is SocketTextChannel socketTextChannel)
            {
                _logger.Debug("💬 父频道类型: 文字频道 [{TextChannelName}]", socketTextChannel.Name);
                
                if (ShouldCompletelyIgnored(socketTextChannel))
                {
                    _logger.Debug("⏭️ 文字频道 [{TextChannelName}] 被过滤规则忽略", socketTextChannel.Name);
                    return;
                }
                
                await _dataProcessService.CreateTextThreadProcessAsync(socketThreadChannel, socketTextChannel);
                _logger.Info("✅ 文字频道线程创建处理完成: [{GuildName}.{TextChannelName}.{ThreadName}]", 
                    socketThreadChannel.Guild.Name, socketTextChannel.Name, socketThreadChannel.Name);
            }
            else
            {
                _logger.Debug("⏭️ 父频道类型不支持: {ParentChannelType}", parentChannel.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理帖子创建事件时发生错误, 帖子ID: {ThreadId}, 频道ID: {ChannelId}", 
                socketThreadChannel.Id, socketThreadChannel.ParentChannel.Id, $"https://discord.com/channels/{socketThreadChannel.Guild.Id}/{socketThreadChannel.Id}");
        }
    }

    /// <summary>
    /// 处理帖子更新事件
    /// </summary>
    /// <param name="cacheableThreadChannel">可能缓存的旧帖子对象</param>
    /// <param name="socketThreadChannel">更新后的帖子频道对象</param>
    private async Task ThreadUpdateAsync(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel, SocketThreadChannel socketThreadChannel)
    {
        try
        {
            _logger.Debug("🔄 检测到帖子更新: [{GuildName}.{ParentChannelName}] 帖子ID {ThreadId}, 帖子名称 [{ThreadName}]",
                socketThreadChannel.Guild.Name, socketThreadChannel.ParentChannel?.Name ?? "未知父频道", 
                socketThreadChannel.Id, socketThreadChannel.Name);
            
            // 获取父频道对象
            SocketChannel parentChannel;
            if (socketThreadChannel.ParentChannel is null)
            {
                _logger.Debug("父频道为空，尝试从 CategoryId 获取: CategoryId {CategoryId}", socketThreadChannel.CategoryId);
                
                if (socketThreadChannel.CategoryId is null)
                {
                    _logger.Warning("⚠️ 频道 {ThreadId} 的 CategoryId 无效, 放弃处理", socketThreadChannel.Id);
                    return;
                }
                
                parentChannel = _client.GetChannel((ulong)socketThreadChannel.CategoryId);
                
                if (parentChannel is null)
                {
                    _logger.Warning("⚠️ 无法获取频道 {CategoryId}, 放弃处理", socketThreadChannel.CategoryId);
                    return;
                }
            }
            else
            {
                parentChannel = socketThreadChannel.ParentChannel;
            }

            // 根据父频道类型分发处理
            if (parentChannel is SocketForumChannel socketForumChannel)
            {
                _logger.Debug("📋 父频道类型: 论坛频道 [{ForumName}]", socketForumChannel.Name);
                
                if (ShouldCompletelyIgnored(socketForumChannel))
                {
                    _logger.Debug("⏭️ 论坛频道 [{ForumName}] 被过滤规则忽略", socketForumChannel.Name);
                    return;
                }
                
                await _dataProcessService.UpdateForumThreadProcessAsync(socketThreadChannel, socketForumChannel);
                _logger.Info("✅ 论坛帖子更新处理完成: [{GuildName}.{ForumName}.{ThreadName}]", 
                    socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name);
            }
            else if (parentChannel is SocketTextChannel socketTextChannel)
            {
                _logger.Debug("💬 父频道类型: 文字频道 [{TextChannelName}]", socketTextChannel.Name);
                
                if (ShouldCompletelyIgnored(socketTextChannel))
                {
                    _logger.Debug("⏭️ 文字频道 [{TextChannelName}] 被过滤规则忽略", socketTextChannel.Name);
                    return;
                }
                
                await _dataProcessService.UpdateTextThreadProcessAsync(socketThreadChannel, socketTextChannel);
                _logger.Info("✅ 文字频道线程更新处理完成: [{GuildName}.{TextChannelName}.{ThreadName}]", 
                    socketThreadChannel.Guild.Name, socketTextChannel.Name, socketThreadChannel.Name);
            }
            else
            {
                _logger.Debug("⏭️ 父频道类型不支持: {ParentChannelType}", parentChannel.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理帖子更新事件时发生错误, 帖子ID: {ThreadId}, 频道ID: {ChannelId}", 
                socketThreadChannel.Id, socketThreadChannel.ParentChannel.Id, $"https://discord.com/channels/{socketThreadChannel.Guild.Id}/{socketThreadChannel.Id}");
        }
    }

    /// <summary>
    /// 处理帖子删除事件
    /// </summary>
    /// <param name="cacheableChannel">可能缓存的帖子对象</param>
    public async Task DeleteThreadAsync(Cacheable<SocketThreadChannel, ulong> cacheableChannel)
    {
        try
        {
            _logger.Info("🗑️ 检测到帖子删除: 帖子ID {ThreadId}", cacheableChannel.Id);
            await _dataProcessService.DeleteThreadProcessAsync(cacheableChannel.Id);
            _logger.Info("✅ 帖子删除处理完成: 帖子ID {ThreadId}", cacheableChannel.Id);
            
            // 从缓存中移除该帖子
            _forumThreadIds.TryRemove(cacheableChannel.Id, out _);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理帖子删除事件时发生错误, 帖子ID: {ThreadId}", cacheableChannel.Id);
        }
    }
    #endregion

    #region 反应事件
    /// <summary>
    /// 处理反应添加事件
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableChannel">可能缓存的频道对象</param>
    /// <param name="_">添加的反应对象(未使用)</param>
    public async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction _)
    {
        try
        {
            _logger.Debug("➕ 检测到反应添加: 消息ID {MessageId}, 频道ID {ChannelId}", 
                cacheableMessage.Id, cacheableChannel.Id);
            await ReactionProcessAsync(cacheableMessage, cacheableChannel, NumericUpdateType.Increase);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理反应添加事件时发生错误, 消息ID: {Messaged}, 频道ID: {ChannelId}", cacheableChannel.Id, cacheableChannel.Id);
        }
    }

    /// <summary>
    /// 处理反应移除事件
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableChannel">可能缓存的频道对象</param>
    /// <param name="_">移除的反应对象(未使用)</param>
    public async Task ReactionRemovedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction _)
    {
        try
        {
            _logger.Debug("➖ 检测到反应移除: 消息ID {MessageId}, 频道ID {ChannelId}", 
                cacheableMessage.Id, cacheableChannel.Id);
            await ReactionProcessAsync(cacheableMessage, cacheableChannel, NumericUpdateType.Decrease);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理反应移除事件时发生错误, 消息ID: {Messaged}, 频道ID: {ChannelId}", cacheableChannel.Id, cacheableChannel.Id);
        }
    }

    /// <summary>
    /// 统一处理反应变化的核心逻辑。
    /// 仅当消息ID等于频道ID时(即帖子的首楼消息)，才会更新反应计数。
    /// 使用缓存字典避免重复的异步下载操作以提高性能。
    /// </summary>
    /// <param name="cacheableMessage">可能缓存的消息对象</param>
    /// <param name="cacheableChannel">可能缓存的频道对象</param>
    /// <param name="reactionUpdateType">反应更新类型(增加或减少)</param>
    private async Task ReactionProcessAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, NumericUpdateType reactionUpdateType)
    {
        try
        {
            // 判断是否为帖子首楼消息(Discord中帖子的首楼消息ID等于频道ID)
            if (cacheableMessage.Id == cacheableChannel.Id)
            {
                _logger.Debug("🎯 反应目标是帖子首楼消息: 帖子ID {ThreadId}", cacheableChannel.Id);
                
                // 尝试从缓存中获取论坛ID
                if (_forumThreadIds.TryGetValue(cacheableChannel.Id, out var forumId))
                {
                    _logger.Debug("📦 从缓存获取论坛ID: 帖子ID {ThreadId} -> 论坛ID {ForumId}", 
                        cacheableChannel.Id, forumId);
                    
                    // 检查论坛ID是否在监控列表中
                    if (!ChannelIds.Contains(forumId))
                    {
                        _logger.Debug("⏭️ 论坛ID {ForumId} 不在监控列表中，跳过处理", forumId);
                        return;
                    }
                }
                else
                {
                    // 缓存中没有，需要下载频道信息
                    _logger.Debug("🔍 缓存未命中，下载频道信息: 频道ID {ChannelId}", cacheableChannel.Id);
                    
                    var channel = cacheableChannel.HasValue ? cacheableChannel.Value : await cacheableChannel.DownloadAsync();
                    
                    if (channel is null)
                    {
                        _logger.Warning("⚠️ 无法获取频道信息: 频道ID {ChannelId}", cacheableChannel.Id);
                        return;
                    }
                    
                    // 将帖子ID和频道ID添加到缓存
                    _forumThreadIds.TryAdd(cacheableChannel.Id, channel.Id);
                    _logger.Debug("💾 已缓存论坛关系: 帖子ID {ThreadId} -> 频道ID {ChannelId}", 
                        cacheableChannel.Id, channel.Id);
                    
                    // 检查频道ID是否在监控列表中
                    if (!ChannelIds.Contains(channel.Id))
                    {
                        _logger.Debug("⏭️ 频道ID {ChannelId} 不在监控列表中，跳过处理", channel.Id);
                        return;
                    }
                }
                
                // 执行反应更新处理
                _logger.Debug("🔄 更新帖子反应计数: 帖子ID {ThreadId}, 更新类型 {UpdateType}", 
                    cacheableChannel.Id, reactionUpdateType);
                await _dataProcessService.UpdateTheradReactionProcessAsync(cacheableChannel.Id, reactionUpdateType);
                _logger.Info("✅ 帖子反应更新完成: 帖子ID {ThreadId}, 更新类型 {UpdateType}", 
                    cacheableChannel.Id, reactionUpdateType);
            }
            else
            {
                _logger.Debug("⏭️ 反应目标不是首楼消息，视为无效: 消息ID {MessageId}, 频道ID {ChannelId}", 
                    cacheableMessage.Id, cacheableChannel.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理反应事件时发生错误: 消息ID {MessageId}, 频道ID {ChannelId}, 更新类型 {UpdateType}", 
                cacheableMessage.Id, cacheableChannel.Id, reactionUpdateType);
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
        try
        {
            // 只处理论坛频道
            if (socketChannel is SocketForumChannel socketForumChannel && socketForumChannel.CategoryId.HasValue)
            {
                // 检查是否在排除的服务器列表中
                if (_configLoader.Config.DeployConfig.ExcludeGuildIds.Contains(socketForumChannel.Guild.Id))
                {
                    _logger.Debug("⏭️ 论坛频道所在服务器被排除: [{GuildName}]", socketForumChannel.Guild.Name);
                    return;
                }
                
                // 检查是否在排除的分类列表中
                if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(socketForumChannel.CategoryId.Value))
                {
                    _logger.Debug("⏭️ 论坛频道所在分类被排除: CategoryId {CategoryId}", socketForumChannel.CategoryId.Value);
                    return;
                }
                
                _logger.Info("🆕 检测到新论坛频道创建: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                    socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
                
                await _dataProcessService.CreateOrUpdateChannelProcessAsync(socketForumChannel);
                _logger.Info("✅ 论坛频道创建处理完成: [{GuildName}.{ChannelName}]", 
                    socketForumChannel.Guild.Name, socketForumChannel.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理频道创建事件时发生错误: 频道ID {ChannelId}", 
                socketChannel.Id);
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
        try
        {
            // 只处理论坛频道
            if (socketAfterChannel is SocketForumChannel socketForumChannel && socketForumChannel.CategoryId.HasValue)
            {
                // 检查是否在排除的服务器列表中
                if (_configLoader.Config.DeployConfig.ExcludeGuildIds.Contains(socketForumChannel.Guild.Id))
                {
                    _logger.Debug("⏭️ 论坛频道所在服务器被排除: [{GuildName}]", socketForumChannel.Guild.Name);
                    return;
                }
                
                // 检查是否在排除的分类列表中
                if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(socketForumChannel.CategoryId.Value))
                {
                    _logger.Debug("⏭️ 论坛频道所在分类被排除: CategoryId {CategoryId}", socketForumChannel.CategoryId.Value);
                    return;
                }
                
                _logger.Debug("🔄 检测到论坛频道更新: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                    socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
                
                await _dataProcessService.CreateOrUpdateChannelProcessAsync(socketForumChannel);
                _logger.Info("✅ 论坛频道更新处理完成: [{GuildName}.{ChannelName}]", 
                    socketForumChannel.Guild.Name, socketForumChannel.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理频道更新事件时发生错误: 频道ID {ChannelId}", 
                socketAfterChannel.Id);
        }
    }

    /// <summary>
    /// 处理频道删除事件。
    /// 仅处理论坛频道的删除。
    /// </summary>
    /// <param name="socketChannel">删除的频道对象</param>
    public async Task ChannelDestroyedAsync(SocketChannel socketChannel)
    {
        try
        {
            // 只处理论坛频道
            if (socketChannel is SocketForumChannel socketForumChannel && socketForumChannel.CategoryId.HasValue)
            {
                // 检查是否在排除的服务器列表中
                if (_configLoader.Config.DeployConfig.ExcludeGuildIds.Contains(socketForumChannel.Guild.Id))
                {
                    _logger.Debug("⏭️ 论坛频道所在服务器被排除: [{GuildName}]", socketForumChannel.Guild.Name);
                    return;
                }

                // 检查是否在排除的分类列表中
                if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(socketForumChannel.CategoryId.Value))
                {
                    _logger.Debug("⏭️ 论坛频道所在分类被排除: CategoryId {CategoryId}", socketForumChannel.CategoryId.Value);
                    return;
                }

                _logger.Info("🗑️ 检测到论坛频道删除: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                    socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);

                await _dataProcessService.DeleteChannelProcessAsync(socketForumChannel);
                _logger.Info("✅ 论坛频道删除处理完成: [{GuildName}.{ChannelName}]",
                    socketForumChannel.Guild.Name, socketForumChannel.Name);

                // 清理相关的缓存数据
                var threadsToRemove = _forumThreadIds.Where(kvp => kvp.Value == socketForumChannel.Id).Select(kvp => kvp.Key).ToList();
                foreach (var threadId in threadsToRemove)
                {
                    _forumThreadIds.TryRemove(threadId, out _);
                    _logger.Debug("🧹 清理缓存: 移除帖子ID {ThreadId}", threadId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理频道删除事件时发生错误: 频道ID {ChannelId}",
                socketChannel.Id);
        }
    }
    #endregion
    #endregion

    #region 辅助检查方法
    /// <summary>
    /// 检查消息是否应该被完全忽略(根据排除规则)。
    /// 检查顺序：服务器排除 -> 频道排除 -> 分类排除。
    /// </summary>
    /// <param name="socketMessage">要检查的消息对象</param>
    /// <returns>如果应该忽略返回 true，否则返回 false</returns>
    private bool ShouldCompletelyIgnored(SocketMessage socketMessage)
    {
        try
        {
            var channel = socketMessage.Channel as SocketGuildChannel;
            
            // 非服务器频道(如私聊)直接忽略
            if (channel is null)
            {
                _logger.Debug("🔍 过滤检查: 非服务器频道，忽略处理");
                return true;
            }
            
            // 检查服务器是否在排除列表中
            if (_configLoader.Config.DeployConfig.ExcludeGuildIds.Contains(channel.Guild.Id))
            {
                _logger.Debug("🔍 过滤检查: 服务器 [{GuildName}] (ID: {GuildId}) 在排除列表中", 
                    channel.Guild.Name, channel.Guild.Id);
                return true;
            }
            
            // 获取实际的频道(如果是线程则获取父频道)
            SocketGuildChannel actualChannel = channel is SocketThreadChannel threadChannel 
                ? threadChannel.ParentChannel 
                : channel;
            
            // 检查频道ID是否在排除列表中
            var channelId = actualChannel.Id;
            if (_configLoader.Config.DeployConfig.ExcludeChannelIds.Contains(channelId))
            {
                _logger.Debug("🔍 过滤检查: 频道 (ID: {ChannelId}) 在排除列表中", channelId);
                return true;
            }
            
            // 获取分类ID
            ulong? categoryId = null;
            if (actualChannel is SocketTextChannel textChannel)
            {
                categoryId = textChannel.CategoryId;
            }
            else if (actualChannel is SocketForumChannel forumChannel)
            {
                categoryId = forumChannel.CategoryId;
            }
            
            // 检查分类ID
            if (!categoryId.HasValue)
            {
                _logger.Debug("🔍 过滤检查: 频道无分类ID，忽略处理");
                return true;
            }
            
            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(categoryId.Value))
            {
                _logger.Debug("🔍 过滤检查: 分类 (ID: {CategoryId}) 在排除列表中", categoryId.Value);
                return true;
            }
            
            _logger.Debug("✅ 过滤检查: 消息通过所有过滤规则");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 检查消息过滤规则时发生错误: 消息ID {MessageId}", socketMessage.Id);
            return true; // 出错时默认忽略
        }
    }
    
    /// <summary>
    /// 检查论坛频道是否应该被完全忽略(根据排除规则)。
    /// 检查顺序：频道排除 -> 分类排除。
    /// </summary>
    /// <param name="socketForumChannel">要检查的论坛频道对象</param>
    /// <returns>如果应该忽略返回 true，否则返回 false</returns>
    private bool ShouldCompletelyIgnored(SocketForumChannel socketForumChannel)
    {
        try
        {
            // 检查频道ID是否在排除列表中
            var channelId = socketForumChannel.Id;
            if (_configLoader.Config.DeployConfig.ExcludeChannelIds.Contains(channelId))
            {
                _logger.Debug("🔍 过滤检查: 论坛频道 [{ChannelName}] (ID: {ChannelId}) 在排除列表中", 
                    socketForumChannel.Name, channelId);
                return true;
            }

            // 获取分类ID
            ulong? categoryId = socketForumChannel.CategoryId;

            // 检查分类ID
            if (!categoryId.HasValue)
            {
                _logger.Debug("🔍 过滤检查: 论坛频道 [{ChannelName}] 无分类ID，忽略处理", socketForumChannel.Name);
                return true;
            }
            
            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(categoryId.Value))
            {
                _logger.Debug("🔍 过滤检查: 论坛频道 [{ChannelName}] 的分类 (ID: {CategoryId}) 在排除列表中", 
                    socketForumChannel.Name, categoryId.Value);
                return true;
            }

            _logger.Debug("✅ 过滤检查: 论坛频道 [{ChannelName}] 通过所有过滤规则", socketForumChannel.Name);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 检查论坛频道过滤规则时发生错误: 频道ID {ChannelId}", socketForumChannel.Id);
            return true; // 出错时默认忽略
        }
    }

    /// <summary>
    /// 检查文字频道是否应该被完全忽略(根据排除规则)。
    /// 检查顺序：频道排除 -> 分类排除。
    /// </summary>
    /// <param name="socketTextChannel">要检查的文字频道对象</param>
    /// <returns>如果应该忽略返回 true，否则返回 false</returns>
    private bool ShouldCompletelyIgnored(SocketTextChannel socketTextChannel)
    {
        try
        {
            // 检查频道ID是否在排除列表中
            var channelId = socketTextChannel.Id;
            if (_configLoader.Config.DeployConfig.ExcludeChannelIds.Contains(channelId))
            {
                _logger.Debug("🔍 过滤检查: 文字频道 [{ChannelName}] (ID: {ChannelId}) 在排除列表中",
                    socketTextChannel.Name, channelId);
                return true;
            }

            // 获取分类ID
            ulong? categoryId = socketTextChannel.CategoryId;

            // 检查分类ID
            if (!categoryId.HasValue)
            {
                _logger.Debug("🔍 过滤检查: 文字频道 [{ChannelName}] 无分类ID，忽略处理", socketTextChannel.Name);
                return true;
            }

            if (_configLoader.Config.DeployConfig.ExcludeCategoryIds.Contains(categoryId.Value))
            {
                _logger.Debug("🔍 过滤检查: 文字频道 [{ChannelName}] 的分类 (ID: {CategoryId}) 在排除列表中",
                    socketTextChannel.Name, categoryId.Value);
                return true;
            }

            _logger.Debug("✅ 过滤检查: 文字频道 [{ChannelName}] 通过所有过滤规则", socketTextChannel.Name);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 检查文字频道过滤规则时发生错误: 频道ID {ChannelId}", socketTextChannel.Id);
            return true; // 出错时默认忽略
        }
    }
    #endregion
    

    #region 帖子成员事件

    public async Task ThreadMemberJoinedAsync(SocketThreadUser socketThreadUser)
    {
        try
        {
            await _dataProcessService.ThreadMemberJoinedProcessAsync(socketThreadUser);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理帖子成员加入时发生错误, 用户: {UserName}, 用户ID: {UserId}, 帖子: {ThreadName}, 帖子ID: {ThreadId}, 加入于: {ThreadJoinedAt}",
                socketThreadUser.GlobalName, socketThreadUser.Id, socketThreadUser.Thread.Name ,socketThreadUser.Thread.Id, socketThreadUser.ThreadJoinedAt);
        }
    }
    public async Task ThreadMemberLeftAsync(SocketThreadUser socketThreadUser)
    {
        try
        {
            await _dataProcessService.ThreadMemberLeftProcessAsync(socketThreadUser);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <Task>: 处理帖子成员离开时发生错误, 用户: {UserName}, 用户ID: {UserId}, 帖子: {ThreadName}, 帖子ID: {ThreadId}, 曾加入于: {ThreadJoinedAt}",
                socketThreadUser.GlobalName, socketThreadUser.Id, socketThreadUser.Thread.Name ,socketThreadUser.Thread.Id, socketThreadUser.ThreadJoinedAt);
        }

    }
    
    #endregion
}