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
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot.ThreadFetcher;

/// <summary>
/// 负责监听 Discord 服务器中的实时事件（如消息、帖子创建/更新/删除等），
/// 并通过防抖机制（Debouncer）对这些事件进行高效、批量的处理。
/// 它将相关事件暂存起来，在适当的时机（达到阈值或延迟时间到）统一更新到数据库，
/// 同时也能检测特定类型的消息（如包含特殊元数据的PNG或JSON文件）以触发用户订阅推送。
/// </summary>
class Listener
{
    private readonly ILogger _logger;
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

    /// <summary>
    /// 论坛频道缓存字典
    /// </summary>
    private readonly ConcurrentDictionary<ulong, IForumChannel> forumCache = new();

    public Listener(
        ILogger logger,
        DiscordSocketClient client,
        DatabaseService databaseService,
        ConfigLoader configLoader,
        Scanner scanner,
        ThreadProcessor threadProcessor,
        ProxyConfig proxyConfig
    )
    {
        _logger = logger.ForContext("Listener");
        _client = client;
        _databaseService = databaseService;
        _configLoader = configLoader;
        _scanner = scanner;
        _threadProcessor = threadProcessor;
        _channelIds = _configLoader.Config.GuildsConfig.ScanChannelIDS.Values.SelectMany(ids => ids).ToHashSet();

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
            TimeSpan.FromSeconds(10)     // 设定的秒延迟
        );
    }

    /// <summary>
    /// 注册所有需要监听的 Discord 客户端事件处理器。
    /// 这些事件包括消息接收、帖子创建、更新、删除以及反应变化等。
    /// </summary>
    public void AddEventHandlers()
    {
        // _client.MessageReceived += CreatorAutomatic;
        _client.MessageReceived += AddOrUpdateThreadAsync;
        _client.ThreadCreated += AddOrUpdateThreadAsync;
        _client.ThreadUpdated += AddOrUpdateThreadAsync;
        _client.ReactionAdded += AddOrUpdateThreadAsync;
        _client.ReactionRemoved += AddOrUpdateThreadAsync;
        _client.ThreadDeleted += RemoveThreadAsync;
        _logger.Info("✅ 监听器事件处理方法已添加。");
    }

    /// <summary>
    /// 注销所有已注册的 Discord 客户端事件处理器。
    /// 在服务关闭或重启时调用，以避免内存泄漏和不必要的事件处理。
    /// </summary>
    public void RemoveEventHandlers()
    {
        _client.MessageReceived -= AddOrUpdateThreadAsync;
        _client.ThreadCreated -= AddOrUpdateThreadAsync;
        _client.ThreadUpdated -= AddOrUpdateThreadAsync;
        _client.ReactionAdded -= AddOrUpdateThreadAsync;
        _client.ReactionRemoved -= AddOrUpdateThreadAsync;
        _client.ThreadDeleted -= RemoveThreadAsync;
        _logger.Info("✅ 监听器事件处理方法已移除。");
    }

    #region 自动处理作者发放

    // public async Task CreatorAutomatic(SocketMessage message)
    // {

    // }

    #endregion

    #region 更新事件处理(多个重载)

    #region MessageReceived
    /// <summary>
    /// 处理接收到的消息事件。
    /// 如果消息所在频道是我们监控的帖子频道，则将其加入待处理队列，并触发防抖/批量处理逻辑。
    /// 同时，会检查消息是否由帖子所有者发布且包含特定附件，以触发订阅推送。
    /// </summary>
    /// <param name="message">接收到的消息对象。</param>
    private async Task AddOrUpdateThreadAsync(SocketMessage message)
    {
        // === 前置条件检查  ============================
        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock)
        {
            return;
        }
        // 2. 检查是否来自监控的频道。
        var threadChannel = message.Channel as IThreadChannel;
        // 从帖子对象获取其父频道ID (CategoryId 在论坛帖子里就是父频道ID)
        // 然后检查父频道ID是否在我们监控的列表中。
        // 如果这个帖子不属于我们扫描的任何一个论坛频道, 忽略。
        if (threadChannel?.CategoryId == null || !_channelIds.Contains(threadChannel.CategoryId.Value)) return;
        // 3. 忽略机器人自身或任何其他机器人发出的消息。
        if (message.Author == _client.CurrentUser) return;
        // === 前置条件检查  ============================

        // === 更新数据逻辑 ============================
        // // _logger.Debug("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);
        // 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.Info("MessageReceived: 监听待处理队列添加帖子: {threadChannel.Name}({threadChannel.Id})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.Info("MessageReceived: 待处理帖子数量: {_pendingThreads.Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            _logger.Warning("MessageReceived: 待处理帖子达到阈值 ({_pendingThreads.Count})，立即触发处理。", _pendingThreads.Count);
            // 立即处理，并取消任何正在计时的防抖器
            _threadProcessorDebouncer.Cancel();
            await ProcessPendingThreadsAsync();
        }
        else
        {
            // 未达到阈值，则触发/重置防抖计时器
            _threadProcessorDebouncer.Trigger();
        }
        // _logger.Debug("message.Author.Id {message.Author.Id}", message.Author.Id);
        // _logger.Debug("threadChannel.OwnerId {threadChannel.OwnerId}", threadChannel.OwnerId);
        // === 更新数据逻辑 ============================

        // === 处理频道缓存和统计 ============================
        var now = DateTimeOffset.UtcNow.AddHours(4).ToString("yyyy-MM-dd");
        IForumChannel? forumChannel = null;
        if (threadChannel.CategoryId.HasValue && forumCache.TryGetValue(threadChannel.CategoryId.Value, out var forumChannelCache))
        {
            // 如果能进入这个 if 代码块，说明：
            // 1. thread.CategoryId 有值。
            // 2. 缓存中成功找到了这个ID。
            forumChannel = forumChannelCache;
        }
        else
        {
            // 如果缓存里没有，或者 CategoryId 本身就是 null，
            // 在这里执行获取频道、并存入缓存的逻辑。
            if (threadChannel.CategoryId.HasValue) forumChannel = await threadChannel.Guild.GetChannelAsync(threadChannel.CategoryId.Value, CacheMode.AllowDownload) as IForumChannel;
            if (forumChannel != null) forumCache.TryAdd(threadChannel.CategoryId.Value, forumChannel);
        }
        if (forumChannel != null) await _databaseService.IncrementReplyCountAsync(now, threadChannel.GuildId, threadChannel.Guild.Name, forumChannel.Id, forumChannel.Name);
        // === 处理频道缓存和统计 ============================

        // === 检查作品更新 ============================
        if (message.Author.Id == threadChannel.OwnerId)
        {
            // 傻逼 dc api 的 MessageReceived 不会附带附件, 附件是后续 Update 上去的, 得重新获取一下 Update 的消息
            // 先尝试仅从缓存获取
            var freshMessage = await threadChannel.GetMessageAsync(message.Id, CacheMode.CacheOnly);
            if (freshMessage == null)
            {
                // _logger.Debug("无法从频道缓存中获取到最新的消息对象 (ID: {MessageId})，将尝试从API获取。", message.Id);
                freshMessage = await threadChannel.GetMessageAsync(message.Id, CacheMode.AllowDownload);
                if (freshMessage == null)
                {
                    // 频繁触发上限处理时可以考虑缩短防抖时间
                    _logger.Warning("无法从频道缓存中获取到最新的消息对象 (ID: {message.Id})，将使用事件参数中的消息对象。", message.Id);
                    freshMessage = message; // 如果获取失败，回退到使用原始的 message 对象
                }
            }
            // 判断是否是更新
            var isUpdate = await CheckUpdateAsync(freshMessage);
            // _logger.Debug("isUpdate {isUpdate}", isUpdate);

            // 推送和插入更新数据
            if (isUpdate)
            {
                // 推送给订阅用户
                await PushToUserAsync(threadChannel, freshMessage);
                // 更新主表信息
                await UpdateMainTableInfoAsync(threadChannel, freshMessage);
            }
        }
        // === 检查作品更新 ============================
    }
    #endregion

    #region ThreadCreated
    /// <summary>
    /// 处理帖子创建或更新事件。
    /// 将发生变动的帖子加入待处理队列，并触发防抖/批量处理逻辑。
    /// </summary>
    /// <param name="socketThreadChannel">创建或更新的帖子频道对象。</param>
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

        // 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.Info("ThreadCreated: 监听待处理队列添加帖子: {threadChannel.Name}({threadChannel.Id})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.Info("ThreadCreated: 待处理帖子数量: {_pendingThreads.Count}", _pendingThreads.Count);

        // === 处理频道缓存和统计 ============================
        var now = DateTimeOffset.UtcNow.AddHours(4).ToString("yyyy-MM-dd");
        IForumChannel? forumChannel = null;
        if (threadChannel.CategoryId.HasValue && forumCache.TryGetValue(threadChannel.CategoryId.Value, out var forumChannelCache))
        {
            // 如果能进入这个 if 代码块，说明：
            // 1. thread.CategoryId 有值。
            // 2. 缓存中成功找到了这个ID。
            forumChannel = forumChannelCache;
        }
        else
        {
            // 如果缓存里没有，或者 CategoryId 本身就是 null，
            // 在这里执行获取频道、并存入缓存的逻辑。
            if (threadChannel.CategoryId.HasValue) forumChannel = await threadChannel.Guild.GetChannelAsync(threadChannel.CategoryId.Value, CacheMode.AllowDownload) as IForumChannel;
            if (forumChannel != null) forumCache.TryAdd(threadChannel.CategoryId.Value, forumChannel);
        }
        if (forumChannel != null) await _databaseService.IncrementCreatedPostsCountAsync(now, threadChannel.GuildId, threadChannel.Guild.Name, forumChannel.Id, forumChannel.Name);
        // === 处理频道缓存和统计 ============================

        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            // await Task.Delay(TimeSpan.FromSeconds(1));
            // 频繁触发上限处理时可以考虑缩短防抖时间
            _logger.Warning("ThreadCreated: 待处理帖子达到阈值 ({_pendingThreads.Count})，立即触发处理。", _pendingThreads.Count);
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

    #region ThreadUpdated
    /// <summary>
    /// 处理帖子更新事件的重载版本。
    /// 将发生变动的帖子加入待处理队列，并触发防抖/批量处理逻辑。
    /// </summary>
    /// <param name="cacheableThreadChannel">可能在缓存中的帖子对象。</param>
    /// <param name="socketThreadChannel">更新后的帖子频道对象。</param>
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

        // _logger.Debug("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);

        // 1. 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.Info("监听待处理队列添加帖子: {threadChannel.Name}({threadChannel.Id})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.Info("待处理帖子数量: {_pendingThreads.Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            // 频繁触发上限处理时可以考虑缩短防抖时间
            _logger.Warning("待处理帖子达到阈值 ({_pendingThreads.Count})，立即触发处理。", _pendingThreads.Count);
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

    #region ReactionAdded/ReactionRemoved
    /// <summary>
    /// 处理反应（Reaction）添加或移除事件。
    /// 将发生反应变化的帖子加入待处理队列，以更新其反应计数等信息，并触发防抖/批量处理逻辑。
    /// </summary>
    /// <param name="cacheableUserMessage">发生反应的消息。</param>
    /// <param name="cacheableMessageChannel">消息所在的频道（帖子）。</param>
    /// <param name="socketReaction">反应的详细信息。</param>
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

        // _logger.Debug("接收到帖子更新事件: {ThreadId}, 加入待处理队列。", threadChannel.Id);

        // 1. 将帖子添加到待处理队列 (或更新已有的)
        // AddOrUpdate 方法是线程安全的，它会原子性地完成操作。
        // 如果键已存在，它会使用新的 threadChannel 对象更新值。
        _logger.Info("ReactionAdd/Remove: 监听待处理队列添加帖子: {threadChannel.Name}({threadChannel.Id})", threadChannel.Name, threadChannel.Id);
        _pendingThreads[threadChannel.Id] = threadChannel;
        _logger.Info("ReactionAdd/Remove: 待处理帖子数量: {_pendingThreads.Count}", _pendingThreads.Count);
        // 2. 检查是否达到批量处理阈值
        if (_pendingThreads.Count >= BATCH_PROCESS_THRESHOLD)
        {
            // 频繁触发上限处理时可以考虑缩短防抖时间
            _logger.Warning("ReactionAdd/Remove: 待处理帖子达到阈值 ({_pendingThreads.Count})，立即触发处理。", _pendingThreads.Count);
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

    #endregion

    #region 删除事件处理

    /// <summary>
    /// 处理帖子删除事件。
    /// 当一个被监控的帖子被删除时，此方法会从数据库中移除对应的帖子记录。
    /// </summary>
    /// <param name="cacheableThreadChannel">被删除的帖子对象（可能在缓存中）。</param>
    private async Task RemoveThreadAsync(Cacheable<SocketThreadChannel, ulong> cacheableThreadChannel)
    {
        // === 前置条件检查 (Guards) ===

        // 1. 如果正在全量同步，则暂停所有增量更新，避免数据冲突。
        if (_scanner.FullSyncLock)
        {
            return;
        }
        _logger.Info("ThreadDeleted: 处理删除帖子: {cacheableThreadChannel.Id}", cacheableThreadChannel.Id);
        // 2. 查询数据库主表中是否有该帖子, 有则删除
        await _databaseService.RemovePostAsync(new List<ulong> { cacheableThreadChannel.Id });
    }

    #endregion

    #region 处理方法

    /// <summary>
    /// 实际处理暂存区中所有待更新帖子的核心方法。
    /// 此方法会被防抖器或阈值检查触发，它会原子性地取出所有待处理的帖子，
    /// 通过 ThreadProcessor 将它们转换为数据模型，然后批量写入数据库。
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
        // 注意：这里不是完全线程安全的，但足够了。
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

        _logger.Info("防抖/阈值触发: 开始处理 {threadsToProcess.Count} 个帖子。", threadsToProcess.Count);

        try
        {
            (List<ThreadDataModel> threadDataModels, _) = await _threadProcessor.ProcessToThreadDataModelAsync(threadsToProcess);

            if (threadDataModels.Any())
            {
                // 批量写入数据库
                await _databaseService.InsertPostAsync(threadDataModels);
                _logger.Info("✅ 成功批量更新 {threadsToProcess.Count} 个帖子到数据库。", threadsToProcess.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Error( "批量处理帖子时发生严重错误。", ex);
        }
    }
    #endregion

    #region 更新检测

    /// <summary>
    /// 检查一条消息是否包含可被视为“作品更新”的附件。
    /// 优化策略：
    /// 1. 使用 HEAD 请求预检文件大小，跳过超大附件（如 > 20MB）。
    /// 2. 流式下载附件到临时文件，避免将整个文件加载到内存。
    /// 3. 使用 Image.Identify 仅读取元数据，不解码整个图像，显著降低内存占用。
    /// </summary>
    /// <param name="message">要检查的消息对象。</param>
    /// <returns>如果消息包含更新，则返回 true；否则返回 false。</returns>
    public async Task<bool> CheckUpdateAsync(IMessage message)
    {
        _logger.Debug("CheckUpdateAsync started for message ID: {message.Id}", message.Id);
        try
        {
            if (message.Attachments.Count == 0)
            {
                _logger.Debug("No attachments found on message {message.Id}. Returning false.", message.Id);
                return false;
            }

            // --- 步骤 1: 检查 JSON 附件 (这个开销很小，可以先做) ---
            if (message.Attachments.Any(a => a.Filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Info("JSON 检查: 在消息 {message.Id} 中检测到 JSON 附件，判定为更新。", message.Id);
                return true;
            }
            
            // --- 步骤 2: 过滤并处理 PNG 附件 ---
            var pngAttachments = message.Attachments
                .Where(a => a.Filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pngAttachments.Count == 0)
            {
                _logger.Debug("Message {message.Id} has no PNG attachments. All checks complete.", message.Id);
                return false;
            }

            if (pngAttachments.Count >= 3)
            {
                _logger.Debug("Message {message.Id} has no PNG attachments. But >= 3, ", message.Id);
                return false;
            }

            _logger.Debug("Found {pngAttachments.Count} PNG attachments. Starting sequential processing with optimizations.", pngAttachments.Count);

            // 使用 for-loop 逐个处理，避免并行下载带来的瞬时内存/IO高峰
            foreach (var attachment in pngAttachments)
            {
                if (await IsUpdatePngAsync(attachment))
                {
                    _logger.Info("PNG 检查: 在附件 '{attachment.Filename}' 中检测到角色卡更新。", attachment.Filename);
                    return true; // 找到一个就够了，直接返回
                }
            }
            
            _logger.Debug("No update-triggering PNGs found after checking all. Returning false.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An unexpected error occurred in CheckUpdateAsync for message ID {message.Id}.", message.Id);
            return false;
        }
    }
    
    /// <summary>
    /// 辅助方法，用于检查单个PNG附件是否为更新。
    /// </summary>
    /// <param name="attachment">要检查的PNG附件。</param>
    /// <returns>如果是更新，返回 true。</returns>
    private async Task<bool> IsUpdatePngAsync(IAttachment attachment)
    {
        // 定义文件大小限制 (例如 20 MB)
        const long MAX_FILE_SIZE_BYTES = 20 * 1024 * 1024;
        string? tempFilePath = null;

        try
        {
            // 1. 【优化】预检文件大小
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, attachment.Url);
            using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);

            if (headResponse.Content.Headers.ContentLength.HasValue && headResponse.Content.Headers.ContentLength.Value > MAX_FILE_SIZE_BYTES)
            {
                var fileSizeMB = headResponse.Content.Headers.ContentLength.Value / 1024.0 / 1024.0;
                var sizeLimitMB = MAX_FILE_SIZE_BYTES / 1024.0 / 1024.0;
                _logger.Debug("Skipping attachment '{attachment.Filename}' because its size ({fileSizeMB} MB) exceeds the {sizeLimitMB} MB limit.",
                    attachment.Filename,
                    fileSizeMB.ToString("F2"),
                    sizeLimitMB
                    );
                return false;
            }

            // 2. 【优化】流式下载到临时文件
            tempFilePath = Path.GetTempFileName();
            _logger.Debug("Downloading '{attachment.Filename}' to temporary file: {tempFilePath}", attachment.Filename, tempFilePath);

            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var httpStream = await _httpClient.GetStreamAsync(attachment.Url))
            {
                await httpStream.CopyToAsync(fileStream);
            }
            _logger.Debug("Successfully downloaded '{attachment.Filename}'.", attachment.Filename);

            // 3. 【优化】使用 Image.Identify 仅读取元数据
            _logger.Debug("Identifying metadata for '{tempFilePath}' without decoding the image.",tempFilePath);
            var imageInfo = await SixLabors.ImageSharp.Image.IdentifyAsync(tempFilePath);

            if (imageInfo == null)
            {
                _logger.Warning("ImageSharp could not identify image info for '{attachment.Filename}'.", attachment.Filename);
                return false;
            }

            var pngMetadata = imageInfo.Metadata.GetPngMetadata();
            _logger.Debug("Found {pngMetadata.TextData.Count} text chunks in '{attachment.Filename}'.", pngMetadata.TextData.Count, attachment.Filename);

            foreach (var textData in pngMetadata.TextData)
            {
                if (textData.Keyword.Equals("ccv3", StringComparison.OrdinalIgnoreCase) ||
                    textData.Keyword.Equals("chara", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Success! Found update keyword '{textData.Keyword}' in '{attachment.Filename}'.", textData.Keyword, attachment.Filename);
                    return true;
                }
            }

            _logger.Debug("No update keywords found in '{attachment.Filename}'.", attachment.Filename);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process PNG attachment '{attachment.Filename}'. It will be skipped.",attachment.Filename);
            return false;
        }
        finally
        {
            // 4. 关键：清理临时文件
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                    _logger.Debug("Cleaned up temporary file: {tempFilePath}", tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to delete temporary file '{tempFilePath}'.",  tempFilePath);
                }
            }
        }
    }
    #endregion

    #region 用户订阅推送

    /// <summary>
    /// 将作品更新推送给所有订阅了该帖子的用户。
    /// 此方法会查询数据库获取订阅了指定帖子的用户列表，
    /// 然后将更新消息的信息添加到他们的推送队列中。
    /// </summary>
    /// <param name="thread">发生更新的帖子对象。</param>
    /// <param name="message">包含更新内容的消息对象。</param>
    public async Task PushToUserAsync(IThreadChannel thread, IMessage message)
    {
        // 检查哪些用户订阅了该帖子
        var userIds = await _databaseService.GetSubscribedUserIdsByThreadIdAsync(thread.Id);
        if (userIds.Count == 0)
        {
            return;
        }
        // 推送消息给所有订阅了该帖子的用户
        await _databaseService.InsertUserPushListAsync(userIds, thread, message);
    }

    #endregion

    #region 更新主表作品更新信息
    /// <summary>
    /// 将检测到的作品更新信息（更新时间和最新消息链接）同步到数据库的 posts_main 表中。
    /// </summary>
    /// <param name="thread">发生更新的帖子对象。</param>
    /// <param name="message">包含更新内容的消息对象。</param>
    public async Task UpdateMainTableInfoAsync(IThreadChannel thread, IMessage message)
    {
        _logger.Info("检测到帖子 '{thread.Name}'({thread.Id}) 的作品更新, 准备更新主表信息。", thread.Name, thread.Id);
        await _databaseService.UpdatePostUpdateInfoAsync(thread.Id, message);
    }
    #endregion
}