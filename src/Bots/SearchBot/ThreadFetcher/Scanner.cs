using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Discord.Net;

using SearchBot.Model;
using SearchBot.Database;

namespace SearchBot.ThreadFetcher;

/// <summary>
/// 负责扫描指定服务器和频道中的帖子(Thread)。
/// </summary>
public class Scanner
{
    private readonly ILogger<Scanner> _logger;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _databaseService;

    private readonly ThreadProcessor _threadProcessor;

    public bool FullSyncLock { get; private set; } = false;

    public Scanner(ILogger<Scanner> logger, ConfigLoader configLoader, DiscordSocketClient client, DatabaseService databaseService, ThreadProcessor threadProcessor)
    {
        _logger = logger;
        _configLoader = configLoader;
        _client = client;
        _databaseService = databaseService;
        _threadProcessor = threadProcessor;
    }

    public ScanResult FullSync()
    {
        try
        {
            // 检查锁
            if (FullSyncLock)
            {
                var errorMessage = "❌ 另一次全量同步正在执行中，跳过。";
                _logger.LogInformation(errorMessage);
                throw new InvalidOperationException(errorMessage); ;
            }
            // 设置锁
            FullSyncLock = true;
            var scanResult = new ScanResult();
            // 扫描全部帖子
            List<IThreadChannel> threads = Scan().GetAwaiter().GetResult();
            scanResult.ScanAllResult = threads.Count;
            // 处理全部帖子数据
            (List<ThreadDataModel> threadDataModels, ThreadProcessResult threadProcessResult) = _threadProcessor.ProcessToThreadDataModelAsync(threads).GetAwaiter().GetResult();
            scanResult.ThreadProcessResult = threadProcessResult;
            // 批量插入数据库
            (List<ThreadDataModel>? result, InsertPostResult? insertPostResult) = _databaseService.InsertPostAsync(threadDataModels).GetAwaiter().GetResult();
            if (result != null)
            {
                scanResult.InsertPostResult = insertPostResult!;
                // 查询数据库内过时失效帖子
                var obsoletePosts = _databaseService.QueryObsoletePostsAsync(result).GetAwaiter().GetResult();
                if (obsoletePosts != null && obsoletePosts.Count != 0)
                {
                    // 批量移除
                    _logger.LogInformation("数据库有 {0} 个过时失效帖子, 正在执行移除", obsoletePosts.Count);
                    var removeResult = _databaseService.RemovePostAsync(obsoletePosts).GetAwaiter().GetResult();
                    scanResult.ScanObsoleteResult = obsoletePosts.Count;
                    scanResult.ScanRemoveResult = removeResult;
                    _logger.LogInformation("成功从数据库移除 {0} 个过时失效帖子", obsoletePosts.Count);
                }
            }
            // 执行一次 内存数据库 -> 文件 的写入
            _databaseService.SaveDB();
            // 关闭锁
            FullSyncLock = false;
            return scanResult;
        }
        catch (Exception ex)
        {
            _logger.LogError("FullSync {ex}", ex);
            // TODO 发送提示消息
            return new ScanResult();
        }
    }

    /// <summary>
    /// 开始扫描配置文件中指定的所有服务器和论坛频道, 获取其中的所有帖子。
    /// </summary>
    /// <returns>一个包含所有扫描到的帖子的列表。</returns>
    public async Task<List<IThreadChannel>> Scan()
    {
        _logger.LogInformation("扫描任务开始.");

        /// ===========================
        /// 预处理并校准频道/标签部分
        /// ===========================
        
        _logger.LogInformation("开始预处理频道和标签数据...");
        
        // 步骤1: 收集配置中的所有频道ID
        var configChannelIds = _configLoader.GuildsConfig.ScanChannelIDS.Values
            .SelectMany(channelList => channelList)
            .Distinct()
            .ToList();
        
        _logger.LogInformation("从配置文件中发现 {Count} 个频道需要处理", configChannelIds.Count);

        // 步骤2: 获取有效的论坛频道对象
        var validForumChannels = new List<IForumChannel>();
        var tagDict = new Dictionary<IForumChannel, List<IForumTag>>();

        foreach (var channelId in configChannelIds)
        {
            try
            {
                var channel = await _client.GetChannelAsync(channelId);
                
                if (channel == null)
                {
                    _logger.LogWarning("频道 {ChannelId} 不存在或无法访问，已跳过", channelId);
                    continue;
                }

                if (channel is IForumChannel forumChannel)
                {
                    validForumChannels.Add(forumChannel);
                    
                    // 收集该频道的所有标签，并添加到字典中
                    if (forumChannel.Tags?.Any() == true)
                    {
                        tagDict[forumChannel] = forumChannel.Tags.Cast<IForumTag>().ToList();
                        _logger.LogDebug("频道 {ChannelName}({ChannelId}) 包含 {TagCount} 个标签", 
                            forumChannel.Name, forumChannel.Id, forumChannel.Tags.Count);
                    }
                    else
                    {
                        tagDict[forumChannel] = new List<IForumTag>();
                    }
                }
                else
                {
                    _logger.LogWarning("频道 {ChannelId} 不是论坛频道，已跳过", channelId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取频道 {ChannelId} 时发生错误，已跳过", channelId);
            }
        }

        var totalTagCount = tagDict.Values.Sum(tags => tags.Count);
        _logger.LogInformation("成功获取 {ChannelCount} 个有效论坛频道，共 {TagCount} 个标签", 
            validForumChannels.Count, totalTagCount);

        // 步骤3: 校准并插入频道数据
        if (validForumChannels.Any())
        {
            _logger.LogInformation("开始插入/更新频道数据...");
            // 这里会使用UPSERT，所以重复调用是安全的
            await _databaseService.InsertChannelsAsync(validForumChannels);
        }

        // 步骤4: 校准并插入标签数据  
        if (tagDict.Any())
        {
            _logger.LogInformation("开始插入/更新标签数据...");
            await _databaseService.InsertTagsAsync(tagDict);
        }

        // 步骤5: 数据校准 - 清理不再使用的频道和标签 (TODO)
        _logger.LogInformation("开始校准数据库, 清理过时数据...");
        // 从字典中收集所有有效的标签
        var allTagsFromDict = tagDict.Values.SelectMany(tags => tags).ToList();
        // 调用DatabaseService中的清理方法
        await _databaseService.CleanupObsoleteDataAsync(validForumChannels, allTagsFromDict);

        _logger.LogInformation("预处理完成！频道和标签数据已准备就绪。");

        /// ===========================
        /// 正式扫描阶段
        /// ===========================

        // 创建一个列表来存放所有获取到的帖子
        var allThreads = new ConcurrentBag<IThreadChannel>();
        // 创建一个列表来存放所有任务
        var allTasks = new List<Task>();

        // 迭代处理配置文件中指定的每一个服务器
        foreach (KeyValuePair<string, List<ulong>> scanChannelKV in _configLoader.GuildsConfig.ScanChannelIDS)
        {
            _logger.LogInformation("开始处理分类: {TypeName}", scanChannelKV.Key);
            
            // 迭代处理该服务器下配置的每一个需要扫描的频道ID
            foreach (var channelId in scanChannelKV.Value)
            {
                var task = Task.Run(async () =>
                {
                    // 获取论坛频道对象
                    var channel = await _client.GetChannelAsync(channelId);
                    
                    if (channel == null)
                    {
                        _logger.LogWarning("无法找到论坛频道 {ChannelId}, 已跳过.", channelId);
                    }

                    if (channel is IForumChannel forumChannel)
                    {
                        IGuild guild = forumChannel.Guild;
                        _logger.LogInformation("开始扫描论坛频道: [{ChannelName}({ChannelId}).{GuildName}] 分类: [{TypeName}]", channel.Name, channel.Id, guild.Name, scanChannelKV.Key);
                        
                        var threadsInCurrentChannel = await _threadProcessor.GetAllThreads(forumChannel);

                        // 将当前频道扫描到的所有帖子添加到总列表中
                        foreach (var thread in threadsInCurrentChannel)
                        {
                            allThreads.Add(thread);
                        }
                        _logger.LogInformation("频道 [{ChannelName}({ChannelId}).{GuildName}] 分类: [{TypeName}] 扫描完成, 本次共找到 {Count} 个帖子.", channel.Name, channel.Id, guild.Name, scanChannelKV.Key, threadsInCurrentChannel.Count);
                    }
                    else
                    {
                        _logger.LogWarning("频道 {ChannelId} 不是论坛频道, 已跳过.", channelId);
                    }
                });
                allTasks.Add(task);
            }
        }

        await Task.WhenAll(allTasks);

        _logger.LogInformation("扫描任务完成. 总共从 {GuildCount} 个服务器获取了 {ThreadCount} 个帖子.", _configLoader.GuildsConfig.GuildIds.Count, allThreads.Count);

        return allThreads.ToList();
    }
}

