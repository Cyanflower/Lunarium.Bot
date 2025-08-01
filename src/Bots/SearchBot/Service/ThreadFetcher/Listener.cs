using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using SearchBot.Database;
using System.Collections.Concurrent;
using SearchBot.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System.Net;

namespace SearchBot.ThreadFetcher;
class Listener
{
    private readonly ILogger<Listener> _logger;
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _databaseService;
    private readonly ConfigLoader _configLoader;
    private readonly Scanner _scanner;
    private readonly ThreadProcessor _threadProcessor;
    
    // 监控的频道ID列表
    private readonly HashSet<ulong> _channelIds;
    private readonly HttpClient _httpClient;
    
    /// <summary>
    /// 线程安全的暂存区，用于存放待处理的帖子。
    /// Key: 帖子ID (ThreadId), Value: 帖子对象 (IThreadChannel)
    /// 使用 ConcurrentDictionary 保证了同一帖子的更新是原子性的。
    /// </summary>
    private readonly ConcurrentDictionary<ulong, IThreadChannel> _pendingThreads = new();
    
    /// <summary>
    /// 防抖器，用于延迟和批量处理帖子更新。
    /// </summary>
    private readonly Debouncer _threadProcessorDebouncer;
    
    /// <summary>
    /// 批量处理的阈值。当待处理帖子数量达到此值时，立即触发处理。
    /// </summary>
    private const int BATCH_PROCESS_THRESHOLD = 100;
    public Listener(
        ILogger<Listener> logger, 
        DiscordSocketClient client, 
        DatabaseService databaseService, 
        ConfigLoader configLoader, 
        Scanner scanner, 
        ThreadProcessor threadProcessor,
        ProxyConfig proxyConfig
    )
    {
        _logger = logger;
        _client = client;
        _databaseService = databaseService;
        _configLoader = configLoader;
        _scanner = scanner;
        _threadProcessor = threadProcessor;
        _channelIds = _configLoader.GuildsConfig.ScanChannelIDS.Values.SelectMany(ids => ids).ToHashSet();

        if (proxyConfig.Enable && !string.IsNullOrEmpty(proxyConfig.ProxyUrl))
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                Proxy = new WebProxy(proxyConfig.ProxyUrl),
                UseProxy = true
            });
        }
        else
        {
            _httpClient = new HttpClient();
        }
        // 初始化防抖器，设置1秒延迟，并将要执行的操作指向我们的处理方法
        _threadProcessorDebouncer = new Debouncer(
            ProcessPendingThreadsAsync, // 防抖后要调用的方法
            TimeSpan.FromSeconds(10)     // 设定的1秒延迟
        );
    }

    public void AddEventHandlers()
    {
        _client.MessageReceived += AddOrUpdateThreadAsync;
        _client.ThreadCreated += AddOrUpdateThreadAsync;
        _client.ThreadUpdated += AddOrUpdateThreadAsync;
        _client.ReactionAdded += AddOrUpdateThreadAsync;
        _client.ReactionRemoved += AddOrUpdateThreadAsync;
        _client.ThreadDeleted += RemoveThreadAsync;
        _logger.LogInformation("✅ 监听器事件处理方法已添加。");
    }

    public void RemoveEventHandlers()
    {
        _client.MessageReceived -= AddOrUpdateThreadAsync;
        _client.ThreadCreated -= AddOrUpdateThreadAsync;
        _client.ThreadUpdated -= AddOrUpdateThreadAsync;
        _client.ReactionAdded -= AddOrUpdateThreadAsync;
        _client.ReactionRemoved -= AddOrUpdateThreadAsync;
        _client.ThreadDeleted -= RemoveThreadAsync;
        _logger.LogInformation("✅ 监听器事件处理方法已移除。");
    }

#region 更新事件处理(多个重载)
    private async Task AddOrUpdateThreadAsync(SocketMessage message)
    {
        // === 前置条件检查 (Guards) ===
        
        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock )
        {
            return;
        }
        // 2. 检查是否来自监控的频道。

        var threadChannel = message.Channel as IThreadChannel;

        // 从帖子对象获取其父频道ID (CategoryId 在论坛帖子里就是父频道ID)
        // 然后检查父频道ID是否在我们监控的列表中。
        if (threadChannel?.CategoryId == null || !_channelIds.Contains(threadChannel.CategoryId.Value))
        {
            // 这个帖子不属于我们扫描的任何一个论坛频道, 忽略。
            return;
        }
        // 3. 忽略机器人自身或任何其他机器人发出的消息。
        if (message.Author == _client.CurrentUser)
        {
            return;
        }

        // === 核心调度逻辑 ===

        _logger.LogTrace("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);

        // 1. 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.LogInformation("监听待处理队列添加帖子: {ThreadName}({ThreadId})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.LogInformation("待处理帖子数量: {Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            _logger.LogInformation("待处理帖子达到阈值 ({Count})，立即触发处理。", _pendingThreads.Count);
            // 立即处理，并取消任何正在计时的防抖器
            _threadProcessorDebouncer.Cancel();
            await ProcessPendingThreadsAsync();
        }
        else
        {
            // 3. 未达到阈值，则触发/重置防抖计时器
            _threadProcessorDebouncer.Trigger();
        }

        if (message.Author.Id == threadChannel.OwnerId)
        {
            var isUpdate = await CheckUpdateAsync(message);
            if (isUpdate)
            {
                await PushToUserAsync(threadChannel, message);
            }
        }
    }
    private async Task AddOrUpdateThreadAsync(SocketThreadChannel socketThreadChannel)
    {
        // === 前置条件检查 (Guards) ===
        
        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock)
        {
            return;
        }
        // 2. 检查是否来自监控的频道。

        var threadChannel = socketThreadChannel as IThreadChannel;

        // 从帖子对象获取其父频道ID (CategoryId 在论坛帖子里就是父频道ID)
        // 然后检查父频道ID是否在我们监控的列表中。
        if (threadChannel?.CategoryId == null || !_channelIds.Contains(threadChannel.CategoryId.Value))
        {
            // 这个帖子不属于我们扫描的任何一个论坛频道, 忽略。
            return;
        }

        // === 核心调度逻辑 ===

        _logger.LogTrace("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);

        // 1. 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.LogInformation("监听待处理队列添加帖子: {ThreadName}({ThreadId})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.LogInformation("待处理帖子数量: {Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            _logger.LogInformation("待处理帖子达到阈值 ({Count})，立即触发处理。", _pendingThreads.Count);
            // 立即处理，并取消任何正在计时的防抖器
            _threadProcessorDebouncer.Cancel();
            await ProcessPendingThreadsAsync();
        }
        else
        {
            // 3. 未达到阈值，则触发/重置防抖计时器
            _threadProcessorDebouncer.Trigger();
        }
    }
    private async Task AddOrUpdateThreadAsync(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel, SocketThreadChannel socketThreadChannel)
    {
        // === 前置条件检查 (Guards) ===
        
        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock)
        {
            return;
        }
        // 2. 检查是否来自监控的频道。

        var threadChannel = socketThreadChannel as IThreadChannel;

        // 从帖子对象获取其父频道ID (CategoryId 在论坛帖子里就是父频道ID)
        // 然后检查父频道ID是否在我们监控的列表中。
        if (threadChannel?.CategoryId == null || !_channelIds.Contains(threadChannel.CategoryId.Value))
        {
            // 这个帖子不属于我们扫描的任何一个论坛频道, 忽略。
            return;
        }

        // === 核心调度逻辑 ===

        _logger.LogTrace("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);

        // 1. 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.LogInformation("监听待处理队列添加帖子: {ThreadName}({ThreadId})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.LogInformation("待处理帖子数量: {Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            _logger.LogInformation("待处理帖子达到阈值 ({Count})，立即触发处理。", _pendingThreads.Count);
            // 立即处理，并取消任何正在计时的防抖器
            _threadProcessorDebouncer.Cancel();
            await ProcessPendingThreadsAsync();
        }
        else
        {
            // 3. 未达到阈值，则触发/重置防抖计时器
            _threadProcessorDebouncer.Trigger();
        }
    }
    private async Task AddOrUpdateThreadAsync(Cacheable<IUserMessage, ulong> cacheableUserMessage, Cacheable<IMessageChannel, ulong> cacheableMessageChannel, SocketReaction socketReaction)
    {
        // === 前置条件检查 (Guards) ===
        
        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock)
        {
            return;
        }
        // 2. 检查是否来自监控的频道。

        var threadChannel = cacheableMessageChannel.GetOrDownloadAsync() as IThreadChannel;

        // 从帖子对象获取其父频道ID (CategoryId 在论坛帖子里就是父频道ID)
        // 然后检查父频道ID是否在我们监控的列表中。
        if (threadChannel?.CategoryId == null || !_channelIds.Contains(threadChannel.CategoryId.Value))
        {
            // 这个帖子不属于我们扫描的任何一个论坛频道, 忽略。
            return;
        }

        // === 核心调度逻辑 ===

        _logger.LogTrace("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);

        // 1. 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.LogInformation("监听待处理队列添加帖子: {ThreadName}({ThreadId})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.LogInformation("待处理帖子数量: {Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            _logger.LogInformation("待处理帖子达到阈值 ({Count})，立即触发处理。", _pendingThreads.Count);
            // 立即处理，并取消任何正在计时的防抖器
            _threadProcessorDebouncer.Cancel();
            await ProcessPendingThreadsAsync();
        }
        else
        {
            // 3. 未达到阈值，则触发/重置防抖计时器
            _threadProcessorDebouncer.Trigger();
        }
    }
#endregion

#region 删除事件处理

    private async Task RemoveThreadAsync(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel)
    {
        // === 前置条件检查 (Guards) ===

        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock)
        {
            return;
        }
        // 2. 查询数据库主表中是否有该帖子, 有则删除
        await _databaseService.RemovePostAsync(new List<ulong> { cacheableThreadChannel.Id });
        // 3. 删除数据库后备表中的帖子
        await _databaseService.RemoveFavoriteAndSubscriptionPostsAsync(new List<ulong> { cacheableThreadChannel.Id });
    }

#endregion

#region 处理方法

    /// <summary>
    /// 实际处理暂存区中所有帖子的方法。
    /// 这个方法会被防抖器调用，或者在达到阈值时直接调用。
    /// </summary>
    public async Task ProcessPendingThreadsAsync()
    {
        // 创建一个局部变量来持有当前需要处理的帖子列表，并清空全局暂存区。
        // 这是关键一步，可以防止在处理过程中又有新帖子加进来。
        List<IThreadChannel> threadsToProcess;
        
        // IsEmpty 是线程安全的
        if (_pendingThreads.IsEmpty)
        {
            return; // 可能在阈值触发和防抖触发之间有竞态，导致队列已空
        }
        
        // 把暂存区的内容“快照”出来处理
        // 注意：这里不是完全线程安全的，但对于我们的场景足够了。
        // 一个更严谨的做法是加锁，但...也够用了 (懒了。
        var currentKeys = _pendingThreads.Keys.ToList();
        threadsToProcess = new List<IThreadChannel>();
        foreach (var key in currentKeys)
        {
            // TryRemove 是线程安全的原子操作
            if (_pendingThreads.TryRemove(key, out var thread))
            {
                threadsToProcess.Add(thread);
            }
        }

        if (threadsToProcess.Count == 0)
        {
            return;
        }

        _logger.LogInformation("防抖/阈值触发: 开始处理 {Count} 个帖子。", threadsToProcess.Count);

        try
        {
            List<ThreadDataModel> threadDataModels = await _threadProcessor.ProcessToThreadDataModelAsync(threadsToProcess);

            if (threadDataModels.Any())
            {
                // 批量写入数据库
                await _databaseService.InsertPostAsync(threadDataModels);
                _logger.LogInformation("✅ 成功批量更新 {Count} 个帖子到数据库。", threadDataModels.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量处理帖子时发生严重错误。");
        }
    }
#endregion

#region 更新检测

    public async Task<bool> CheckUpdateAsync(IMessage message)
    {
        if (message.Attachments.Count == 0)
        {
            return false;
        }
        var attachment = message.Attachments as IEnumerable<IAttachment>;
        
        // 寻找png结尾的附件
        var pngAttachments = attachment.Where(a => a.Filename.EndsWith(".png"));
        if (pngAttachments.Any())
        {
            // 遍历创建png附件下载任务, 然后执行处理逻辑
            var tasks = pngAttachments.Select(async a =>
            {
                try
                {
                    // 下载PNG文件
                    var imageBytes = await _httpClient.GetByteArrayAsync(a.Url);
                    
                    // 使用ImageSharp读取PNG元数据
                    using var image = SixLabors.ImageSharp.Image.Load(imageBytes);
                    var pngMetadata = image.Metadata.GetPngMetadata();
                    
                    // 检查tEXt chunks
                    foreach (var textData in pngMetadata.TextData)
                    {
                        // 检查关键字是否为"ccv3"或"chara"
                        if (textData.Keyword.Equals("ccv3", StringComparison.OrdinalIgnoreCase) ||
                            textData.Keyword.Equals("chara", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    
                    return false;
                }
                catch (Exception ex)
                {
                    // 处理异常，比如文件损坏或网络错误
                    Console.WriteLine($"处理PNG文件时出错: {ex.Message}");
                    return false;
                }
            });
            
            // 等待所有任务完成
            var results = await Task.WhenAll(tasks);
            
            // 只要有任意一个文件包含目标chunk就返回true
            return results.Any(result => result);
        }
        
        // 寻找json结尾的附件
        var jsonAttachments = attachment.Where(a => a.Filename.EndsWith(".json"));
        if (jsonAttachments.Any())
        {
            // json无需内容检测, 通常代表着预设/世界书/正则的更新, 这些文件直接就是json
            return true;
        }
        
        return false;
    }

#endregion

#region 用户订阅推送

    public async Task PushToUserAsync(IThreadChannel thread, IMessage message)
    {
        // 检查哪些用户订阅了该帖子
        var userIds = await _databaseService.GetAllUserIdsByThreadIdAsync(thread.Id);
        if (userIds.Count == 0)
        {
            return;
        }
        // 推送消息给所有订阅了该帖子的用户
        await _databaseService.InsertUserPushListAsync(userIds, thread, message);
    }

#endregion
}