// .NET 核心库
using System.Collections.Concurrent;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Rest;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.DataFetcher;

/// <summary>
/// 负责扫描指定服务器和频道中的帖子(Thread)。
/// </summary>
public class Scanner
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly SemaphoreSlim _semaphore;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly RequestOptions _requestOptions;
    private Dictionary<ulong, SocketForumChannel> _socketForumChannels = new();
    private HashSet<ulong> ScanChannelIDs => _configLoader.Config.GuildsConfig.ScanChannelIDs.Values.SelectMany(list => list).ToHashSet();
    private HashSet<ulong> DeployGuildIds => _configLoader.Config.GuildsConfig.DeployGuildIds.ToHashSet();
    public Scanner(ILogger logger, ConfigLoader configLoader, DiscordSocketClient discordSocketClient, SemaphoreSlim semaphore)
    {
        _logger = logger.ForContext("Scanner");
        _configLoader = configLoader;
        _discordSocketClient = discordSocketClient;
        _semaphore = semaphore;
        _httpRetryPolicy = HttpRetryPolicyConfig();
        _requestOptions = RequestOptionsConfig();
    }

    public async Task SyncScan()
    {
        _logger.Info("扫描任务开始.");
        var allForumChannels = GetAllForumChannel();
        var allRestThreads = await GetAllRestThreads(allForumChannels);
    }

    private Dictionary<SocketGuild, List<SocketForumChannel>> GetAllForumChannel()
    {
        try
        {
            Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels = new();
            _socketForumChannels = new();
            foreach (var guildId in DeployGuildIds)
            {
                List<SocketForumChannel> socketForumChannels = new();
                var guild = _discordSocketClient.GetGuild(guildId);
                if (guild != null)
                {
                    foreach (var channel in guild.Channels)
                    {
                        if (channel is SocketForumChannel forumChannel && channel.GetType() == typeof(SocketForumChannel) && ScanChannelIDs.Contains(channel.Id))
                        {
                            socketForumChannels.Add(forumChannel);
                            _socketForumChannels[forumChannel.Id] = forumChannel;
                        }
                    }
                    allForumChannels[guild] = socketForumChannels;
                }
            }
            return allForumChannels;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetAllForumChannel");
            return new();
        }
    }

    private async Task<Dictionary<SocketGuild, List<RestThreadChannel>>> GetAllRestThreads(Dictionary<SocketGuild, List<SocketForumChannel>> allForumChannels)
    {
        try
        {
            Dictionary<SocketGuild, List<RestThreadChannel>> allRestThreads = new();
            foreach (var (guild, forumChannels) in allForumChannels)
            {
                List<RestThreadChannel> currentGuildThreads = new();
                List<Task<List<RestThreadChannel>>> tasks = new();
                foreach (var channel in forumChannels)
                {
                    Task<List<RestThreadChannel>> task = Task.Run(async () =>
                    {
                        try
                        {
                            await _semaphore.WaitAsync();
                            List<RestThreadChannel> restThreads = new();
                            // 1. 获取活跃的帖子
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                _logger.Info("[{GuildName}.{channelName}({channelId})] 正在获取活跃的帖子...", guild.Name, channel.Name, channel.Id);

                                var activeThreads = await channel.GetActiveThreadsAsync();

                                _logger.Info("[{GuildName}.{channelName}({channelId})] 成功获取了 {activeThreadsCount} 个活跃的帖子.", guild.Name, channel.Name, channel.Id, activeThreads.Count);

                                restThreads.AddRange(activeThreads);
                            });

                            // 2. 获取公开归档的帖子
                            _logger.Info("[{GuildName}.{channelName}({channelId})] 正在获取公开归档的帖子...", guild.Name, channel.Name, channel.Id);

                            var publicArchivedThreads = new List<RestThreadChannel>();
                            DateTimeOffset? beforeTimestamp = null;
                            int page = 1;

                            while (true)
                            {
                                IReadOnlyCollection<RestThreadChannel>? fetchedThreads = null;

                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    _logger.Debug("[{GuildName}.{channelName}({channelId})] 正在获取第 {page} 批公开归档帖子...", guild.Name, channel.Name, channel.Id, page);
                                    fetchedThreads = await channel.GetPublicArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                                });

                                if (fetchedThreads?.Count > 0)
                                {
                                    publicArchivedThreads.AddRange(fetchedThreads);
                                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                                    page++;
                                }
                                else
                                {
                                    _logger.Info("[{GuildName}.{channelName}({channelId})] 所有公开归档帖子已获取完毕.", guild.Name, channel.Name, channel.Id);
                                    break;
                                }
                            }
                            restThreads.AddRange(publicArchivedThreads);
                            _logger.Info("[{GuildName}.{channelName}({channelId})] 总共获取了 {publicArchivedThreadsCount} 个公开归档的帖子.", guild.Name, channel.Name, channel.Id, publicArchivedThreads.Count);

                            var uniqueRestThreads = restThreads.DistinctBy(t => t.Id).ToList();
                            _logger.Info("[{GuildName}.{channelName}({channelId})] 总计获取到 {uniqueThreadsCount} 个去重帖子。", guild.Name, channel.Name, channel.Id, uniqueRestThreads.Count);

                            return uniqueRestThreads;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex);
                            return new();
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    });
                    tasks.Add(task);
                }
                currentGuildThreads = (await Task.WhenAll(tasks)).SelectMany(thread => thread).ToList();
                allRestThreads[guild] = currentGuildThreads;
            }
            return allRestThreads;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetAllSocketThreads");
            return new();
        }
    }

    private async Task GetAllThreadsData(Dictionary<SocketGuild, List<RestThreadChannel>> allRestThreads)
    {
        try
        {

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetAllRestThreads");
        }
    }

    private async Task<(ProcessingStatus Status, ThreadData Data)> GetThreadData(RestThreadChannel restThreadChannel)
    {
        try
        {
            var isValid = true;
            ProcessingStatus status = ProcessingStatus.Success;
            // 规则检查: 跳过锁定或置顶的帖子
            if (restThreadChannel.IsLocked)
            {
                _logger.Info("帖子 {threadName}({threadId}) 被锁定，已跳过", restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                status = ProcessingStatus.Skipped;
            }
            // _logger.Debug("thread.Flags {thread.Flags}", thread.Flags);
            if (restThreadChannel.Flags.HasFlag(ChannelFlags.Pinned))
            {
                _logger.Info("帖子 {threadName}({threadId}) 被置顶，已跳过", restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                status = ProcessingStatus.Skipped;
            }

            RestMessage? firstMessage = null;
            RestMessage? lastMessage = null;
            Attachment? imageAttachment = null;
            int highestReactionCount = 0;

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息
                firstMessage = await restThreadChannel.GetMessageAsync(restThreadChannel.Id, _requestOptions);
                // 获取最新消息
                // SelectManyAsync 会把 IAsyncEnumerable<批次> 转换成 IAsyncEnumerable<单个消息>
                lastMessage = await restThreadChannel.GetMessagesAsync(1, _requestOptions)
                                           .SelectMany(batch => batch.ToAsyncEnumerable()) // 压平操作
                                           .FirstOrDefaultAsync(); // 获取第一个元素
            });

            if (firstMessage == null || string.IsNullOrWhiteSpace(firstMessage.Content))
            {
                _logger.Info("帖子 {threadName}({threadId}) 没有有效首楼消息", restThreadChannel.Name, restThreadChannel.Id);
                isValid = false;
                status = ProcessingStatus.Skipped;
            }
            else
            {
                imageAttachment = firstMessage.Attachments.FirstOrDefault(a => IsImageAttachment(a));
                highestReactionCount = firstMessage.Reactions.Values
                    .Select(r => r.ReactionCount)
                    .DefaultIfEmpty(0)
                    .Max();
            }

            if (!_socketForumChannels.TryGetValue(restThreadChannel.ParentChannelId, out var parentChannel))
            {
                // 如果因为某些极其罕见的原因找不到父频道
                _logger.Error("数据不一致错误！无法为帖子 {ThreadName}({ThreadId}) 找到其父频道缓存，ID: {ParentChannelId}",
                            restThreadChannel.Name, restThreadChannel.Id, restThreadChannel.ParentChannelId);
                isValid = false;
                status = ProcessingStatus.Failed;
            }
            else
            {
                // 获得帖子标签
                var appliedTagNames = parentChannel.Tags
                    .Where(tag => restThreadChannel.AppliedTags.Contains(tag.Id))
                    .Select(tag => tag.Name)
                    .ToList();
                var tags = string.Join(", ", appliedTagNames);
            }

            var threadData = new ThreadData()
            {
                ThreadId = restThreadChannel.Id,
                FirstMessageContent = firstMessage?.Content,
                AuthorId = restThreadChannel.OwnerId,
                ChannelId = restThreadChannel.CategoryId ?? 0,
                ChannelName = parentChannel != null ? parentChannel.Name : $"UnknownChannel:{restThreadChannel.CategoryId ?? 0}",
                GuildId = restThreadChannel.GuildId,
                GuildName = parentChannel != null ? parentChannel.Guild.Name : $"UnknownGuild:{restThreadChannel.GuildId}",
                CreatedAt = restThreadChannel.CreatedAt,
                
            };
            
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }

    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        // --- Polly 策略配置 ---
        // 配置一个重试策略, 专门用于处理Http的网络重试逻辑
        return Policy
            // 定义需要处理的异常:
            // - 处理任意类型的 Discord.HttpException (例如 500, 502, 503, 429 等, 如果是404(不存在), 403(权限不足), 400(请求格式错误)则不重试)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 || (int)ex.HttpCode != 403 || (int)ex.HttpCode != 400)
            // - 处理网络层级的异常，比如 SSL 握手失败、连接被重置等
            .Or<HttpRequestException>()
            // - 对 HttpClient 超时的处理
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)

            // 定义重试逻辑 (这部分保持不变)
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试3次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 等待时长: 2s, 4s, 8s
                onRetry: (exception, timespan, attempt, context) => // 每次重试时执行的回调
                {
                    // 在日志中可以更清晰地看到是哪种异常触发了重试
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Error(exception, "HTTP请求失败 ({errorType})，在 {timespan.TotalSeconds}秒 后进行第 {attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }

    public static RequestOptions RequestOptionsConfig()
    {
        // RequestOptions: 维持 Discord.Net 的默认重试机制
        // Timeout设置一个比HttpClient超时更长的时间, 确保Polly有足够时间完成重试
        return new RequestOptions
        {
            Timeout = 20 * 60 * 1000, // 设置总超时为20分钟
            RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试
        };
    }
}