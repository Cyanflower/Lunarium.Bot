using System.Net;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;

using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.API;
using Polly;
using Polly.Retry;

using Lunarium.Logging;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.Extensions.DiscordNetExtension;

using LunariumBot.Net.Archive.Model;
using Discord.Rest;

namespace LunariumBot.Net.Archive.Archiver;

/// <summary>
/// Discord 线程归档服务
/// 负责定期清理和归档 Discord 服务器中的非活跃线程，维护置顶帖状态
/// </summary>
public sealed class ArchiverService : IAsyncDisposable
{
    #region 私有字段

    private readonly ILogger _logger;
    private readonly ConfigLoader _archiveConfig;
    private readonly DiscordSocketClient _discordClient;
    private readonly PriorityArchiveDataLoader _priorityArchiveDataLoader;

    /// <summary>
    /// 状态报告消息账本（跨重启持久化消息 id，用于认领既有报告）
    /// </summary>
    private readonly GuildLogMessageRegistry _logMessageRegistry;

    /// <summary>
    /// 定时执行归档任务的计时器
    /// </summary>
    private Timer? _archiveTimer;

    /// <summary>
    /// 用于防止并发执行归档任务的锁对象
    /// <para>
    /// 该信号量同时精确编码了「是否有归档周期在飞」：能拿到锁即代表当前没有周期在跑。
    /// </para>
    /// </summary>
    private readonly SemaphoreSlim _archiveTaskLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// 关闭信号：取消后定时器回调不再启动新的归档周期，释放时也用它等待在飞周期结束。
    /// </summary>
    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>
    /// 缓存的置顶帖信息
    /// 结构: [服务器ID][频道ID] = 线程对象
    /// </summary>
    private ConcurrentDictionary<ulong, Dictionary<ulong, Discord.Rest.RestThreadChannel>> _pinnedThreadsCache = new();

    /// <summary>
    /// 有限归档帖子列表
    /// 结构: [服务器ID][优先归档帖子信息对象]
    /// </summary>
    private ConcurrentDictionary<ulong, PriorityThreadInfo> _priorityArchive = new();

    /// <summary>
    /// 信号量，用于限制并发的Discord API请求数量（最大24个并发请求）
    /// </summary>
    private readonly RateLimiter _apiRequestRateLimiter;

    /// <summary>
    /// HTTP请求重试策略，处理网络异常和临时性错误
    /// </summary>
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    /// <summary>
    /// 状态频道，用于发送归档状态报告
    /// </summary>
    private IMessageChannel? _statusChannel = null;

    /// <summary>
    /// 每个服务器的状态消息缓存，用于更新而不是重复发送消息
    /// 结构: [服务器ID] = 状态消息对象
    /// </summary>
    private Dictionary<ulong, IUserMessage?> _guildStatusMessageCache = new();

    #endregion

    #region 构造函数和初始化

    /// <summary>
    /// 初始化归档服务
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="configLoader">配置加载器</param>
    /// <param name="discordClient">Discord客户端</param>
    /// <param name="priorityArchiveDataLoader">优先归档数据加载器</param>
    /// <param name="logMessageRegistry">状态报告消息账本（持久化消息 id，用于跨重启认领）</param>
    public ArchiverService(ILogger logger, ConfigLoader configLoader, DiscordSocketClient discordClient, PriorityArchiveDataLoader priorityArchiveDataLoader, GuildLogMessageRegistry logMessageRegistry)
    {
        _logger = logger.ForContext("ArchiverService");
        _archiveConfig = configLoader;
        _discordClient = discordClient;
        _priorityArchiveDataLoader = priorityArchiveDataLoader;
        _logMessageRegistry = logMessageRegistry;
        _priorityArchive = new(_priorityArchiveDataLoader.LoadData());

        // 配置HTTP重试策略
        _apiRequestRateLimiter = RateLimiterConfigure();
        _httpRetryPolicy = HttpRetryPolicyConfig();
    }

    public void EnsureTimerIsRunning()
    {
        if (_archiveTimer == null)
        {
            _logger.Info("归档定时器尚未启动，现在开始初始化...");
            _archiveTimer = new Timer(
                callback: ExecuteArchiveTaskCallback,
                state: null,
                // 首次启动时可以立即执行或稍作延迟，此时 Bot 已经 Ready
                dueTime: TimeSpan.FromSeconds(5), // 给一个很短的延迟，避免和 Ready 事件的其他任务抢占资源
                period: TimeSpan.FromMinutes(_archiveConfig.Config.IntervalMin));
        }
        else
        {
            _logger.Info("归档定时器已在运行，状态正常。");
            // 可选：如果担心“任务漂移”，可以在这里重置计时器，让它立即执行一次
            // _archiveTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(_archiveConfig.Config.IntervalMin));
            // 但对于间隔性而非定时性的归档任务，通常没必要，保持原样就好。
        }
    }

    private TokenBucketRateLimiter RateLimiterConfigure()
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            // Token 恢复速率：每秒 45 个
            TokensPerPeriod = 45,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),

            // Token 上限
            TokenLimit = 45,

            // 队列设置
            QueueLimit = 100000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            
            // 自动补充: 开启
            AutoReplenishment = true
        });
    }
    #endregion

    #region 公共方法 (用于外部调用)

    /// <summary>
    /// 更新定时器的执行间隔
    /// </summary>
    public void UpdateTimerInterval()
    {
        _logger.Info("正在更新定时器执行间隔为 {Interval} 分钟", _archiveConfig.Config.IntervalMin);
        if (_archiveTimer != null)
        {
            _archiveTimer.Change(
                dueTime: TimeSpan.Zero,
                period: TimeSpan.FromMinutes(_archiveConfig.Config.IntervalMin));
        }
        else
        {
            _logger.Warning($"归档触发器尚不存在, 将重新初始化新计时器");
            _archiveTimer = new Timer(
                callback: ExecuteArchiveTaskCallback,
                state: null,
                dueTime: TimeSpan.Zero,
                period: TimeSpan.FromMinutes(_archiveConfig.Config.IntervalMin));
        }
    }
    
    /// <summary>
    /// 添加优先归档帖子
    /// </summary>
    /// <param name="threadChannel">要从缓存中移除的线程频道对象</param>
    /// <returns>一个元组，包含操作是否成功 (bool) 和描述原因 (string)</returns>
    public (bool result, string reason) AddPriorityArchive(PriorityThreadInfo priorityThreadInfo)
    {
        try
        {
            // 是否有旧值
            bool keyExists = _priorityArchive.ContainsKey(priorityThreadInfo.ThreadId);

            // 直接使用索引添加进行覆盖
            _priorityArchive[priorityThreadInfo.ThreadId] = priorityThreadInfo;

            _priorityArchiveDataLoader.SaveData(new(_priorityArchive));

            // 如果已存在
            if (keyExists == true)
            {
                return (true, "优先归档列表已存在该帖, 已更新该贴权重");
            }
            else
            {
                return (true, "添加成功");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            return (false, $"添加失败: {ex.ToString().Slice(150, "...")}");
        }
    }

    /// <summary>
    /// 查看优先归档帖子列表
    /// </summary>
    /// <returns>返回ConcurrentDictionary的当前Dictionary快照</returns>
    public Dictionary<ulong, PriorityThreadInfo> GetPriorityArchive()
    {
        return new(_priorityArchive);
    }

    /// <summary>
    /// 移除优先归档帖子
    /// </summary>
    /// <param name="priorityThreadInfo">要从优先归档列表中移除的帖子信息对象</param>
    /// <returns>一个元组，包含操作是否成功 (bool) 和描述原因 (string)</returns>
    public (bool result, string reason) RemovePriorityArchive(PriorityThreadInfo priorityThreadInfo)
    {
        try
        {
            var result = _priorityArchive.TryRemove(priorityThreadInfo.ThreadId, out _);
            if (result)
            {
                _priorityArchiveDataLoader.SaveData(new(_priorityArchive));
                return (true, "移除成功");
            }
            else
            {
                return (false, "移除失败");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            return (false, $"移除失败: {ex.ToString().Slice(150, "...")}");
        }
    }

    /// <summary>
    /// 尝试从置顶线程缓存中移除一个指定的线程。
    /// 这通常在用户手动取消置顶帖子时调用，以确保缓存与实际状态同步。
    /// </summary>
    /// <param name="threadChannel">要从缓存中移除的线程频道对象</param>
    /// <returns>一个元组，包含操作是否成功 (bool) 和描述原因 (string)</returns>
    public (bool result, string reason) TryRemovePinnedThread(IThreadChannel threadChannel)
    {
        try
        {
            if (threadChannel == null)
            {
                return (false, "传入的线程频道对象为空。");
            }
    
            ulong parentChannelId;
    
            var guildId = threadChannel.GuildId;
            if (threadChannel is SocketThreadChannel socketThreadChannel)
            {
                parentChannelId = socketThreadChannel.ParentChannel.Id;
            }
            else if (threadChannel is RestThreadChannel restThreadChannel)
            {
                parentChannelId = restThreadChannel.ParentChannelId;
            }
            else
            {
                return (false, "传入的线程频道对象无法转为有效真实类型");
            }
    
            // 检查服务器缓存是否存在
            if (!_pinnedThreadsCache.TryGetValue(guildId, out var channelCache))
            {
                // 服务器的缓存不存在，说明服务从未在该服务器缓存过任何置顶帖，无需操作。
                return (true, "服务器没有置顶帖缓存记录，无需移除。");
            }
    
            // 检查特定频道的置顶帖缓存是否存在
            if (!channelCache.TryGetValue(parentChannelId, out var cachedThread))
            {
                // 该频道的缓存不存在，说明这个帖子没有被缓存为置顶帖，无需操作。
                return (true, "该频道没有置顶帖缓存记录，无需移除。");
            }
    
            // 确认缓存中的帖子ID与当前要移除的帖子ID是否一致
            if (cachedThread.Id != threadChannel.Id)
            {
                // 例如，手动置顶了另一个帖子，这种情况下无需移除缓存中的旧置顶帖，其已经被覆盖了，后续维护的置顶帖按缓存字典的内容为准
                return (true, $"缓存中的置顶帖 (ID: {cachedThread.Id}) 与当前帖子不匹配，无需移除。");
            }
    
            // 执行移除操作
            if (channelCache.Remove(parentChannelId))
            {
                _logger.Info("手动取消置顶，已从缓存中移除线程 {ThreadId} (父频道: {ParentChannelId})", threadChannel.Id, parentChannelId);
                return (true, "已成功从置顶缓存中移除。");
            }
            else
            {
                _logger.Warning("尝试从置顶缓存中移除线程 {ThreadId} 失败，即使在缓存中找到了它。", threadChannel.Id);
                return (false, "从缓存中移除失败，发生未知错误。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            return (false, $"从缓存中移除失败，发生错误: \n{ex.ToString().Slice(1500)}");
        }
    }

    #endregion

    #region 主要归档逻辑

    /// <summary>
    /// 定时器回调方法，安全地执行归档任务
    /// 使用锁机制防止重叠执行
    /// </summary>
    /// <param name="state">定时器状态参数（未使用）</param>
    private void ExecuteArchiveTaskCallback(object? state)
    {
        // Timer 回调必须是 void：把实际工作交给可等待的 Task 实现，异常在内部捕获
        _ = RunArchiveTaskAsync();
    }

    /// <summary>
    /// 执行一个归档周期。<see cref="_archiveTaskLock"/> 既是互斥锁，也精确编码了「是否有周期在飞」。
    /// </summary>
    private async Task RunArchiveTaskAsync()
    {
        // 尝试立即获取锁 (等待时间为0毫秒)
        if (!await _archiveTaskLock.WaitAsync(0))
        {
            _logger.Warning("上一次归档任务仍在执行中，本次执行已跳过");
            return;
        }

        // 已进入关闭流程：立刻放锁退出，避免在状态频道/缓存释放后继续写入
        if (_shutdownCts.IsCancellationRequested)
        {
            _archiveTaskLock.Release();
            return;
        }

        _logger.Info("====== 定时归档任务开始 ======");
        try
        {
            // 这里的 await 可以安全地切换线程了
            await ProcessAllGuildsArchiveTask();
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            _logger.Warning("归档任务因服务关闭而中断");
        }
        catch (Exception ex)
        {
            // 捕获所有异常，防止定时器线程崩溃
            _logger.Error(ex, "执行归档任务时发生未处理的异常");
        }
        finally
        {
            _logger.Info("====== 定时归档任务结束 ======");
            // 释放锁，让下一次任务可以进入
            _archiveTaskLock.Release();
        }
    }

    /// <summary>
    /// 处理所有配置服务器的归档任务
    /// </summary>
    private async Task ProcessAllGuildsArchiveTask()
    {
        // 初始化状态频道（如果尚未初始化）
        await InitializeStatusChannelIfNeeded();

        // 遍历所有配置的服务器
        foreach ((var guildId, var activeThreadLimit) in _archiveConfig.Config.DeployGuildConfig)
        {
            var taskStartTime = DateTimeOffset.UtcNow;

            _logger.Info("开始处理服务器 {GuildId} (活跃线程上限: {Limit})", guildId, activeThreadLimit);

            try
            {
                await ProcessSingleGuildArchive(guildId, activeThreadLimit, taskStartTime);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理服务器 {GuildId} 的归档任务时发生异常", guildId);
            }

            var taskDuration = DateTimeOffset.UtcNow - taskStartTime;
            _logger.Info("服务器 {GuildId} 处理完成, 总耗时: {Duration:F2} 秒", guildId, taskDuration.TotalSeconds);
        }
    }

    /// <summary>
    /// 处理单个服务器的归档任务
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="activeThreadLimit">活跃线程数量限制</param>
    /// <param name="taskStartTime">任务开始时间</param>
    private async Task ProcessSingleGuildArchive(ulong guildId, int activeThreadLimit, DateTimeOffset taskStartTime)
    {
        // 1. 获取服务器的所有相关频道
        (var allActiveThreads, var guildName) = await GetEligibleChannelsFromGuild(guildId);
        _logger.Info("[{guildName}({GuildId})] 步骤1/6: 从服务器中共获取到 {ThreadCount} 个活跃线程", guildName, guildId, allActiveThreads.Count, allActiveThreads.Count);

        // 2. 分类处理置顶线程和普通线程
        var (pinnedThreads, regularThreads) = ClassifyThreadsByPinnedStatus(allActiveThreads, guildId);
        _logger.Info("[{guildName}({GuildId})] 步骤2/6: 线程分类完成 -> {PinnedCount} 个置顶线程, {RegularCount} 个普通线程", guildName, guildId, pinnedThreads.Count, regularThreads.Count);

        // 3. 处理需要归档的线程 (获取后活跃时间信息)
        var threadsWithLastActivity = await GetThreadsWithLastActivityTime(regularThreads, guildId, guildName);

        // 4. 处理需要归档的线程 (进行归档)
        var archiveResults = await ArchiveExcessiveThreads(threadsWithLastActivity, activeThreadLimit, guildId, guildName);

        // 5. 维护置顶线程状态
        await MaintainPinnedThreadsStatus();

        // 6. 发送或更新状态报告
        await UpdateArchiveStatusReport(guildId, guildName, allActiveThreads.Count, activeThreadLimit, archiveResults, taskStartTime);
    }

    #endregion

    #region 线程获取和分类方法

    /// <summary>
    /// 从服务器中获取所有符合条件的频道（文本频道和论坛频道）
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <returns>符合条件的频道列表</returns>
    private async Task<(List<Discord.Rest.RestThreadChannel>, string)> GetEligibleChannelsFromGuild(ulong guildId)
    {
        var guild = _discordClient.GetGuild(guildId);
        if (guild == null)
        {
            var ex = new ArgumentException($"找不到ID为 '{guildId}' 的服务器。它可能不存在，或 BOT 不在那个服务器里。", nameof(guildId));
            _logger.Error(ex, "获取服务器失败，因为提供的 guildId 无效或 BOT 不在那个服务器里。"); 
            throw ex;
        }
        try
        {
            List<Discord.Rest.RestThreadChannel> allActiveThreads = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                List<Discord.Rest.RestThreadChannel> allActiveThreads = [.. await _discordClient.Rest.GetAllActiveThreadsAsync(guild)];
                return allActiveThreads;
            });
            return (allActiveThreads, guild.Name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "获取服务器 {GuildName}({GuildId}) 的活跃归档贴失败");
            throw;
        }
    }

    /// <summary>
    /// 根据是否置顶对线程进行分类
    /// </summary>
    /// <param name="threads">所有线程</param>
    /// <param name="guildId">服务器ID</param>
    /// <returns>元组：(置顶线程列表, 普通线程列表)</returns>
    private (List<Discord.Rest.RestThreadChannel> pinnedThreads, List<Discord.Rest.RestThreadChannel> regularThreads) ClassifyThreadsByPinnedStatus(List<Discord.Rest.RestThreadChannel> threads, ulong guildId)
    {
        var pinnedThreads = new List<Discord.Rest.RestThreadChannel>();
        var regularThreads = new List<Discord.Rest.RestThreadChannel>();

        // 确保服务器的置顶线程缓存已初始化
        if (!_pinnedThreadsCache.ContainsKey(guildId))
        {
            _pinnedThreadsCache[guildId] = new Dictionary<ulong, Discord.Rest.RestThreadChannel>();
        }

        foreach (var thread in threads)
        {
            if (thread.Flags.HasFlag(ChannelFlags.Pinned))
            {
                pinnedThreads.Add(thread);
                // 更新置顶线程缓存
                _pinnedThreadsCache[thread.GuildId][thread.ParentChannelId] = thread;
            }
            else
            {
                // 添加到普通帖子列表
                regularThreads.Add(thread);
            }
        }

        return (pinnedThreads, regularThreads);
    }

    #endregion

    #region 线程处理方法

    /// <summary>
    /// 维护所有已缓存的置顶线程状态，确保它们保持非归档状态
    /// </summary>
    private async Task MaintainPinnedThreadsStatus()
    {
        // 创建一个新的临时缓存，用于存放本轮成功维护的置顶线程。
        // 这样可以安全地移除已删除或处理失败的线程，而不会影响遍历过程。
        var updatedPinnedCache = new ConcurrentDictionary<ulong, Dictionary<ulong, Discord.Rest.RestThreadChannel>>();

        // 遍历所有服务器的置顶线程缓存
        foreach ((var guildId, var channelThreadPairs) in _pinnedThreadsCache)
        {
            var guild = _discordClient.GetGuild(guildId);
            if (guild is null) continue; // 如果服务器对象获取不到，跳过

            // 为新缓存初始化当前服务器的字典
            if (!updatedPinnedCache.ContainsKey(guildId))
            {
                updatedPinnedCache[guildId] = new Dictionary<ulong, Discord.Rest.RestThreadChannel>();
            }

            // 打印日志，表明开始维护指定服务器的置顶帖
            _logger.Info("[{guildName}({GuildId})] 步骤5/6: 开始维护 {PinnedCount} 个已缓存的置顶线程状态...", 
                guild.Name, guildId, channelThreadPairs.Count);
            
            // 遍历该服务器内所有“记忆中”的置顶线程
            foreach ((var channelId, var pinnedThread) in channelThreadPairs)
            {
                // 等待API请求信号量，防止请求过于频繁导致速率限制
                using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
                if (!apiRateLimitToken.IsAcquired) 
                {
                    _logger.Error("_apiRequestRateLimiter 队列已满");
                    // 只记录日志
                    // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
                }
                try
                {
                    // 使用重试策略执行API请求，增加网络波动时的成功率
                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        // 这是核心操作：修改线程属性
                        await pinnedThread.ModifyAsync(properties =>
                        {
                            // 1. 强制设为非归档状态。这是“续期”的关键，能重置Discord的归档计时器。
                            properties.Archived = false;
                            // 2. 再次声明其置顶标志。这对于从“掉置顶”状态恢复的线程至关重要。
                            properties.Flags = ChannelFlags.Pinned;
                        });
                    });

                    // 维护成功，将此线程加入到新的缓存中，以便下一轮继续维护
                    updatedPinnedCache[guildId][channelId] = pinnedThread;
                    _logger.Debug("[{guildName}({GuildId})] 步骤5/6: 成功维持置顶线程 {ThreadName}({ThreadId}) 的活跃状态", 
                        guild.Name, guildId, pinnedThread.Name, pinnedThread.Id);
                }
                catch (HttpException httpEx) when (httpEx.HttpCode == HttpStatusCode.NotFound)
                {
                    // 捕获“404 Not Found”异常，这意味着该线程已被用户或管理员删除
                    _logger.Warning("[{guildName}({GuildId})] 步骤5/6: 置顶线程 {ThreadId} 已不存在，从维护列表中移除", 
                        guild.Name, guildId, pinnedThread.Id);
                    // 不将此线程添加到新缓存中，相当于实现了自动清理
                }
                catch (Exception ex)
                {
                    // 捕获其他所有异常（如权限问题、网络超时等）
                    _logger.Error(ex, "[{guildName}({GuildId})] 步骤5/6: 维护置顶线程 {ThreadId} 状态时发生异常", 
                        guild.Name, guildId, pinnedThread.Id);
                    // 发生未知错误时，选择保守策略：依然将它保留在缓存中，以便下次任务周期时重试
                    updatedPinnedCache[guildId][channelId] = pinnedThread;
                }
            }
            _logger.Info("[{guildName}({GuildId})] 步骤5/6: 置顶线程维护完成，共 {updatedPinnedCacheCount} 个线程状态被确认", 
                guild.Name, guildId, updatedPinnedCache[guildId].Count);
        }
        // 用本轮验证过的新缓存，完全替换掉旧的缓存
        _pinnedThreadsCache = updatedPinnedCache;
    }

    /// <summary>
    /// 获取线程的最后活跃时间信息
    /// </summary>
    /// <param name="threads">线程列表</param>
    /// <returns>包含最后活跃时间的线程信息数组</returns>
    private async Task<(DateTimeOffset LastMessageAt, Discord.Rest.RestThreadChannel Thread)[]> GetThreadsWithLastActivityTime(List<Discord.Rest.RestThreadChannel> threads, ulong guildId, string guildName)
    {
        _logger.Info("[{guildName}({GuildId})] 步骤3/6: 开始获取 {ThreadCount} 个普通线程的最后活跃时间...", guildName, guildId, threads.Count);
        int count = 0;
        var lastActivityTasks = threads.Select(async thread =>
        {
            using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
            if (!apiRateLimitToken.IsAcquired) 
            {
                _logger.Error("_apiRequestRateLimiter 队列已满");
                // 只记录日志
                // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
            }
            try
            {
                Interlocked.Increment(ref count);
                DateTimeOffset lastMessageTime = thread.CreatedAt;
                _logger.Debug("[{guildName}({GuildId})] 步骤3/6: 开始获取第 {count}/{ThreadCount} 个普通线程的最后活跃时间...", guildName, guildId, count, threads.Count);
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    // 优先归档帖子
                    if (_priorityArchive.TryGetValue(thread.Id, out var priorityThreadInfo))
                    {
                        // 为优先归档帖子设置时间戳: UnixEpoch 减去 归档权重值
                        lastMessageTime = DateTimeOffset.UnixEpoch.AddSeconds(-priorityThreadInfo.Priority);
                    }
                    // 普通帖子
                    else
                    {
                        // 获取线程中的最后一条消息
                        var recentMessages = await thread.GetMessagesAsync(1).FlattenAsync();
                        var lastMessage = recentMessages.FirstOrDefault();

                        lastMessageTime = lastMessage?.CreatedAt ?? DateTimeOffset.UnixEpoch;
                    }
                });

                return (LastMessageAt: lastMessageTime, Thread: thread);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{guildName}({GuildId})] 步骤3/6: 获取线程 {ThreadId} 最后消息时间时发生异常", guildName, guildId, thread.Id);
                // 出错时使用线程创建时间作为备选
                return (LastMessageAt: thread.CreatedAt, Thread: thread);
            }
        });

        var threadsWithActivity = await Task.WhenAll(lastActivityTasks);

        // 按最后活跃时间降序排序（最活跃的在前）
        Array.Sort(threadsWithActivity, (a, b) => b.LastMessageAt.CompareTo(a.LastMessageAt));

        _logger.Info("[{guildName}({GuildId})] 步骤3/6: 获取线程最后活跃时间完成, 并已按降序排序", guildName, guildId);

        return threadsWithActivity;
    }

    /// <summary>
    /// 归档超出限制数量的线程
    /// </summary>
    /// <param name="sortedThreads">按活跃时间排序的线程数组</param>
    /// <param name="activeThreadLimit">活跃线程数量限制</param>
    /// <returns>归档操作的结果信息</returns>
    private async Task<ArchiveOperationResult> ArchiveExcessiveThreads((DateTimeOffset LastMessageAt, Discord.Rest.RestThreadChannel Thread)[] sortedThreads, int activeThreadLimit, ulong guildId, string guildName)
    {
        _pinnedThreadsCache.TryGetValue(guildId, out var pinnedThreads);
        var pinnedCount = pinnedThreads?.Count ?? 0;

        var totalActiveThreads = sortedThreads.Length + pinnedCount;

        // 如果线程总数未超过限制，无需归档
        if (totalActiveThreads <= activeThreadLimit)
        {
            _logger.Info("[{guildName}({GuildId})] 步骤4/6: 活跃线程数 {ActiveCount} 未超过上限 {Limit}, 无需归档", guildName, guildId, totalActiveThreads, activeThreadLimit);
            return new ArchiveOperationResult
            {
                TotalActiveThreads = totalActiveThreads,
                ThreadsToArchive = 0,
                ArchiveResults = Array.Empty<(bool Success, DateTimeOffset LastActiveAt, Discord.Rest.RestThreadChannel Thread)>(),
                HasExcessThreads = false
            };
        }

        // 需要归档的线程（排序后超出限制的部分）
        var threadsToArchive = sortedThreads.Skip(activeThreadLimit - pinnedCount).ToList();
        _logger.Info("[{guildName}({GuildId})] 步骤4/6: {ActiveCount} 个活跃线程超出上限 {Limit}, 准备归档 {ArchiveCount} 个最不活跃的线程", guildName, guildId, totalActiveThreads, activeThreadLimit, threadsToArchive.Count);

        var archiveTasks = threadsToArchive.Select(async threadInfo =>
        {
            using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
            if (!apiRateLimitToken.IsAcquired) 
            {
                _logger.Error("_apiRequestRateLimiter 队列已满");
                // 只记录日志
                // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
            }
            try
            {
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await threadInfo.Thread.ModifyAsync(properties =>
                    {
                        properties.Archived = true;
                    });
                });

                _logger.Info("[{guildName}({GuildId})] -> 成功归档线程 {ThreadId} (最后活跃于: {LastActiveAt})", guildName, guildId, threadInfo.Thread.Id, threadInfo.LastMessageAt);

                return (Success: true, threadInfo.LastMessageAt, threadInfo.Thread);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "归档线程 {ThreadId} 时发生异常", threadInfo.Thread.Id);
                return (Success: false, threadInfo.LastMessageAt, threadInfo.Thread);
            }
        });

        var archiveResults = await Task.WhenAll(archiveTasks);

        return new ArchiveOperationResult
        {
            TotalActiveThreads = totalActiveThreads,
            ThreadsToArchive = threadsToArchive.Count,
            ArchiveResults = archiveResults,
            HasExcessThreads = true
        };
    }

    #endregion

    #region 状态报告更新方法

    /// <summary>
    /// 初始化状态频道（如果尚未初始化）
    /// </summary>
    private async Task InitializeStatusChannelIfNeeded()
    {
        if (_statusChannel == null)
        {
            _logger.Info("状态频道尚未初始化, 正在尝试获取频道对象 (ID: {ChannelId})...", _archiveConfig.Config.AuthConfig.StatusChannel);
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                var statusChannelObj = await _discordClient.GetChannelAsync(_archiveConfig.Config.AuthConfig.StatusChannel);
                _statusChannel = statusChannelObj as IMessageChannel;
            });
            if (_statusChannel != null)
            {
                // [新增日志]
                _logger.Info("状态频道初始化成功!");
            }
            else
            {
                // [新增日志]
                _logger.Error("状态频道初始化失败! 无法获取频道或频道类型不正确。后续将无法发送状态报告。");
            }
        }
    }

    /// <summary>
    /// 更新或发送归档状态报告
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="totalActiveThreads">总活跃线程数</param>
    /// <param name="activeThreadLimit">活跃线程限制</param>
    /// <param name="archiveResult">归档操作结果</param>
    /// <param name="taskStartTime">任务开始时间</param>
    private async Task UpdateArchiveStatusReport(
        ulong guildId,
        string guildName,
        int totalActiveThreads,
        int activeThreadLimit,
        ArchiveOperationResult archiveResult,
        DateTimeOffset taskStartTime
    )
    {
        if (_statusChannel == null)
        {
            _logger.Warning("[{guildName}({GuildId})] 步骤6/6: 由于状态频道未初始化, 跳过发送状态报告", guildName, guildId);
            return;
        }

        _logger.Info("[{guildName}({GuildId})] 步骤6/6: 正在生成并发送/更新 Discord 状态报告...", guildName, guildId);

        var taskEndTime = DateTimeOffset.UtcNow;
        var taskDuration = taskEndTime - taskStartTime;

        var pinnedCount = _pinnedThreadsCache.ContainsKey(guildId) ? _pinnedThreadsCache[guildId].Values.Count : 0;

        var statusEmbed = CreateStatusEmbed(
            guildId,
            guildName,
            totalActiveThreads,
            pinnedCount,
            activeThreadLimit,
            archiveResult,
            taskDuration,
            taskEndTime);

        // 记录状态频道（用于识别频道配置变更）
        _logMessageRegistry.RememberChannel(_statusChannel.Id);

        // 运行时句柄：本进程内已发送或已认领过的消息
        _guildStatusMessageCache.TryGetValue(guildId, out IUserMessage? statusMessage);

        // 句柄缺失（进程刚启动 / 上一轮认领失败）→ 按持久化账本认领既有报告，避免重复新建
        statusMessage ??= await _logMessageRegistry.TryClaimAsync(guildId, _statusChannel);

        // 认领不到 → 新建并记账
        if (statusMessage is null)
        {
            statusMessage = await _statusChannel.SendMessageAsync(embed: statusEmbed);
            _guildStatusMessageCache[guildId] = statusMessage;
            _logMessageRegistry.RememberMessage(guildId, statusMessage.Id);
            _logger.Info("[{guildName}({GuildId})] 步骤6/6: 状态报告发送成功", guildName, guildId);
            return;
        }

        _guildStatusMessageCache[guildId] = statusMessage;

        // 就地更新；消息已被删除（10008 Unknown Message）→ 清账重发，实现自愈
        try
        {
            await statusMessage.ModifyAsync(props => { props.Embed = statusEmbed; });
            _logger.Info("[{guildName}({GuildId})] 步骤6/6: 状态报告更新成功", guildName, guildId);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            _logger.Warning("[{guildName}({GuildId})] 状态报告消息已被删除，正在重新发送...", guildName, guildId);
            _guildStatusMessageCache.Remove(guildId);
            _logMessageRegistry.Forget(guildId);

            var resentMessage = await _statusChannel.SendMessageAsync(embed: statusEmbed);
            _guildStatusMessageCache[guildId] = resentMessage;
            _logMessageRegistry.RememberMessage(guildId, resentMessage.Id);
            _logger.Info("[{guildName}({GuildId})] 步骤6/6: 状态报告重新发送成功", guildName, guildId);
        }
        catch (Exception ex)
        {
            // 其它异常（限流 / 网络等）：保留账本与句柄，下一轮重试
            _logger.Error(ex, "[{guildName}({GuildId})] 步骤6/6: 状态报告更新失败，将于下一轮重试", guildName, guildId);
        }
    }

    /// <summary>
    /// 创建状态嵌入消息
    /// </summary>
    /// <param name="totalActiveThreads">总活跃线程数</param>
    /// <param name="pinnedCount">置顶线程数</param>
    /// <param name="activeThreadLimit">活跃线程限制</param>
    /// <param name="archiveResult">归档结果</param>
    /// <param name="taskDuration">任务执行时长</param>
    /// <param name="taskEndTime">任务结束时间</param>
    /// <returns>格式化的Discord嵌入消息</returns>
    private Discord.Embed CreateStatusEmbed(
        ulong guildId,
        string guildName,
        int totalActiveThreads,
        int pinnedCount,
        int activeThreadLimit,
        ArchiveOperationResult archiveResult,
        TimeSpan taskDuration,
        DateTimeOffset taskEndTime
    )
    {
        var statusSB = new StringBuilder();

        var title = "🗂️ 定时归档执行报告";
        var footer = $"⏱️ 耗时: {taskDuration.TotalSeconds:F2} 秒 | 间隔: {_archiveConfig.Config.IntervalMin} 分钟";

        var embedBuilder = new EmbedBuilder()
            .WithTitle(title)
            .WithFooter(footer)
            .WithTimestamp(DateTimeOffset.UtcNow);

        statusSB.Append('\n');
        statusSB.AppendLine(title);
        statusSB.AppendLine(footer);



        // 根据归档结果的不同情况创建不同的描述
        if (!archiveResult.HasExcessThreads)
        {
            // 情况1: 线程数量在限制内，无需归档
            var availableSlots = activeThreadLimit - totalActiveThreads;

            var description = $"""
                服务器: {guildName}({guildId})
                📊 **当前状态概览** (<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>)
                > 📈 当前活跃线程数: **{totalActiveThreads}**
                > 📌 置顶线程数量: **{pinnedCount}**
                > 🎯 活跃线程上限: **{activeThreadLimit}**
                > 💾 缓冲区大小: **{1000 - activeThreadLimit}**
                > ✅ 剩余可用额度: **{availableSlots}** 个，无需归档操作
                """;

            embedBuilder
                .WithColor(Color.Green)
                .WithDescription(description);

            statusSB.AppendLine(description);
        }
        else if (archiveResult.ThreadsToArchive == 0)
        {
            // 情况2: 正好达到限制，处于临界状态
            var description = $"""
                服务器: {guildName}({guildId})
                📊 **当前状态概览** (<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>)
                > 📈 当前活跃线程数: **{totalActiveThreads}**
                > 📌 置顶线程数量: **{pinnedCount}**
                > 🎯 活跃线程上限: **{activeThreadLimit}**
                > 💾 缓冲区大小: **{1000 - activeThreadLimit}**
                > ⚠️ 已达到活跃线程上限，下次检查时将根据情况进行归档
                """;

            embedBuilder
                .WithColor(Color.Orange)
                .WithDescription(description);

            statusSB.AppendLine(description);
        }
        else
        {
            // 情况3: 超出限制，执行了归档操作
            var successfulArchives = archiveResult.ArchiveResults.Count(r => r.Success);
            var failedArchives = archiveResult.ArchiveResults.Count(r => !r.Success);

            var description = $"""
                服务器: {guildName}({guildId})
                📊 **归档操作概览** (<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>)
                > 📈 当前活跃线程数: **{totalActiveThreads}**
                > 📌 置顶线程数量: **{pinnedCount}**
                > 🎯 活跃线程上限: **{activeThreadLimit}**
                > 💾 缓冲区大小: **{1000 - activeThreadLimit}**
                > 📦 需要归档的线程: **{archiveResult.ThreadsToArchive}** 个
                > ✅ 归档成功: **{successfulArchives}** 个
                > ❌ 归档失败: **{failedArchives}** 个
                """;

            embedBuilder
                .WithColor(failedArchives > 0 ? Color.Red : Color.Blue)
                .WithDescription(description);

            statusSB.AppendLine(description);

            _logger.Info(statusSB.ToString());

            // 添加具体的归档结果详情（最多显示25个）
            var displayResults = archiveResult.ArchiveResults.Take(25);
            int index = 0;

            foreach (var result in displayResults)
            {
                index++;
                var statusIcon = result.Success ? "✅" : "❌";
                var statusText = result.Success ? "归档成功" : "归档失败";
                var lastActiveTimestamp = result.LastActiveAt.ToUnixTimeSeconds();



                if (index == 25 && archiveResult.ArchiveResults.Length > 25)
                {
                    embedBuilder.AddField(
                        "📄 显示限制",
                        $"仅显示前25个结果，总共处理了 {archiveResult.ArchiveResults.Length} 个线程",
                        inline: false);
                }
                else
                {
                    if (result.LastActiveAt < DateTimeOffset.UnixEpoch)
                    {
                        embedBuilder.AddField(
                            $"{statusIcon} {statusText} [{index}] [优先归档] [权重: {(int)(DateTimeOffset.UnixEpoch - result.LastActiveAt).TotalSeconds}]",
                            $"""
                            > - 🔗 https://discord.com/channels/{result.Thread.GuildId}/{result.Thread.Id}
                            > - 🕐 最后活跃: <t:{lastActiveTimestamp}:R>
                            > - 🆔 ID: `{result.Thread.Id}`
                            """,
                            inline: false);
                    }
                    else
                    {
                        embedBuilder.AddField(
                            $"{statusIcon} {statusText} [{index}]",
                            $"""
                            > - 🔗 https://discord.com/channels/{result.Thread.GuildId}/{result.Thread.Id}
                            > - 🕐 最后活跃: <t:{lastActiveTimestamp}:R>
                            > - 🆔 ID: `{result.Thread.Id}`
                            """,
                            inline: false);
                    }

                }
            }
            
            index = 0;
            foreach (var result in archiveResult.ArchiveResults)
            {
                index++;
                var statusIcon = result.Success ? "✅" : "❌";
                var statusText = result.Success ? "归档成功" : "归档失败";
                var lastActiveTimestamp = result.LastActiveAt.ToUnixTimeSeconds();

                if (result.Success)
                {
                    if (result.LastActiveAt < DateTimeOffset.UnixEpoch)
                    {
                        _logger.Info($"""
                            
                            {statusIcon} {statusText} [{index}] [优先归档] [权重: {(int)(DateTimeOffset.UnixEpoch - result.LastActiveAt).TotalSeconds}]
                            > - 🔗 https://discord.com/channels/{result.Thread.GuildId}/{result.Thread.Id}
                            > - 🕐 最后活跃: <t:{lastActiveTimestamp}:R>
                            > - 🆔 ID: `{result.Thread.Id}`
                            """);
                    }
                    else
                    {
                        _logger.Info($"""
                            
                            {statusIcon} {statusText} [{index}]
                            > - 🔗 https://discord.com/channels/{result.Thread.GuildId}/{result.Thread.Id}
                            > - 🕐 最后活跃: <t:{lastActiveTimestamp}:R>
                            > - 🆔 ID: `{result.Thread.Id}`
                            """);
                    }
                }
                else
                {
                    if (result.LastActiveAt < DateTimeOffset.UnixEpoch)
                    {
                        _logger.Warning($"""
                            
                            {statusIcon} {statusText} [{index}] [优先归档] [权重: {(int)(DateTimeOffset.UnixEpoch - result.LastActiveAt).TotalSeconds}]
                            > - 🔗 https://discord.com/channels/{result.Thread.GuildId}/{result.Thread.Id}
                            > - 🕐 最后活跃: <t:{lastActiveTimestamp}:R>
                            > - 🆔 ID: `{result.Thread.Id}`
                            """);
                    }
                    else
                    {
                        _logger.Warning($"""
                            
                            {statusIcon} {statusText} [{index}]
                            > - 🔗 https://discord.com/channels/{result.Thread.GuildId}/{result.Thread.Id}
                            > - 🕐 最后活跃: <t:{lastActiveTimestamp}:R>
                            > - 🆔 ID: `{result.Thread.Id}`
                            """);
                    }
                }
            }
        }

        return embedBuilder.Build();
    }

    #endregion

    #region HTTP重试策略配置

    /// <summary>
    /// 配置HTTP请求的重试策略
    /// 处理网络异常和临时性的Discord API错误
    /// </summary>
    /// <returns>配置好的异步重试策略</returns>
    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        return Policy
            // 定义需要处理的异常:
            // - Discord.HttpException: 处理 5xx 服务器错误和 429 速率限制
            //   不重试: 404(资源不存在)、403(权限不足)、400(请求格式错误)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 && (int)ex.HttpCode != 403 && (int)ex.HttpCode != 400)
            // - HttpRequestException: 处理网络层级异常（SSL 握手失败、连接被重置等）
            .Or<HttpRequestException>()
            // - TaskCanceledException: 处理 HttpClient 超时
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)

            // 定义重试逻辑：指数退避
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2秒、4秒、8秒
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // 在日志中清晰显示触发重试的异常类型
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Warning("⚠️ HTTP 请求失败 ({ErrorType})，将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }

    #endregion

    #region 辅助类和资源释放

    /// <summary>
    /// 归档操作结果信息
    /// </summary>
    private class ArchiveOperationResult
    {
        /// <summary>总活跃线程数</summary>
        public int TotalActiveThreads { get; set; }

        /// <summary>需要归档的线程数</summary>
        public int ThreadsToArchive { get; set; }

        /// <summary>是否存在超出限制的线程</summary>
        public bool HasExcessThreads { get; set; }

        /// <summary>归档操作的具体结果</summary>
        public (bool Success, DateTimeOffset LastActiveAt, Discord.Rest.RestThreadChannel Thread)[] ArchiveResults { get; set; }
            = Array.Empty<(bool, DateTimeOffset, Discord.Rest.RestThreadChannel)>();
    }

    /// <summary>
    /// 释放资源。
    /// <para>
    /// 顺序：停定时器 → 通知关闭 → 等待在飞周期结束 → 释放其余资源。
    /// 等待是必要的：单轮归档实测约 27 秒，若不等它跑完，周期会在状态频道与消息账本释放后继续写入。
    /// </para>
    /// <para>
    /// 注意：必须通过容器的 <c>DisposeAsync</c> 释放（本类型只实现 <see cref="IAsyncDisposable"/>），
    /// 同步释放会抛 <see cref="InvalidOperationException"/>。
    /// </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 1) 停掉定时器：不再产生新的周期（已排队的那次会在 RunArchiveTaskAsync 中直接退出）
        _archiveTimer?.Dispose();
        _archiveTimer = null;

        // 2) 通知关闭，并等待在飞周期结束
        _shutdownCts.Cancel();
        try
        {
            // 能拿到锁 ⇒ 当前没有周期在跑（信号量已精确编码该状态）
            if (await _archiveTaskLock.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None))
            {
                _archiveTaskLock.Release();
            }
            else
            {
                _logger.Warning("等待在飞归档周期结束超时(60 秒)，继续释放资源");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "等待在飞归档周期时发生异常，继续释放资源");
        }

        // 3) 释放其余资源
        _apiRequestRateLimiter?.Dispose();
        _shutdownCts.Dispose();
    }

    #endregion
}