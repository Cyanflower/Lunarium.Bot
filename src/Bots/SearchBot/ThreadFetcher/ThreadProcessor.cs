// 引入必要的命名空间
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;

using Discord.Net;
using Discord;
using Polly;
using Polly.Retry;

using SearchBot.Model;

namespace SearchBot.ThreadFetcher;

public class ThreadProcessor
{
    private readonly ILogger<ThreadProcessor> _logger;
    private readonly AsyncRetryPolicy _apiRetryPolicy; // 定义Polly重试策略

    // RequestOptions: 维持 Discord.Net 的默认重试机制
    // Timeout设置一个比HttpClient超时更长的时间, 确保Polly有足够时间完成重试
    private readonly RequestOptions _requestOptions = new RequestOptions
    {
        Timeout = 20 * 60 * 1000, // 设置总超时为20分钟
        RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试
    };

    private ConcurrentDictionary<ulong, IForumChannel> forumCache = new();

    public ThreadProcessor(ILogger<ThreadProcessor> logger)
    {
        _logger = logger;

        // --- Polly 策略配置 ---
        // 配置一个重试策略, 专门用于处理 Discord.Net 默认不处理的特定异常
        _apiRetryPolicy = Policy
            // 1. 定义需要处理的异常
            //    - HttpClient 超时: 表现为 TaskCanceledException, 其内部异常为 TimeoutException
            .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            //    - 服务器不可用: 表现为 HttpException, 状态码为 503
            .Or<HttpException>(ex => ex.HttpCode == HttpStatusCode.ServiceUnavailable)

            // 2. 定义重试逻辑: 指数退避策略
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试3次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 等待时长: 2s, 4s, 8s
                onRetry: (exception, timespan, attempt, context) => // 每次重试时执行的回调
                {
                    // 从上下文中获取线程信息用于日志记录
                    var threadId = context.ContainsKey("threadId") ? context["threadId"] : "N/A";
                    var threadName = context.ContainsKey("threadName") ? context["threadName"] : "N/A";

                    _logger.LogWarning(exception,
                        "帖子 {ThreadName}({ThreadId}) API请求失败，在 {TimeSpan} 后进行第 {Attempt}/3 次重试",
                        threadName, threadId, timespan, attempt);
                }
            );
    }

    public async Task<List<IThreadChannel>> GetAllThreads(IForumChannel channel)
    {
        List<IThreadChannel> threads = new List<IThreadChannel>();
        // 1. 获取活跃的帖子 (Active Threads)
        // 这些是当前未归档的帖子, 用户可以直接在频道内看到。
        {
            _logger.LogInformation("正在获取活跃的帖子...");
            var activeThreads = await channel.GetActiveThreadsAsync();
            threads.AddRange(activeThreads);
            _logger.LogInformation("获取了 {Count} 个活跃的帖子.", activeThreads.Count);
        }
        // 2. 获取公开归档的帖子 (Public Archived Threads)
        // 这些是已归档但对所有用户可见的帖子。
        // Discord API 对此有分页限制 (通常每页100个), 因此需要循环获取直到获取完毕。
        {
            _logger.LogInformation("[{ChannelName}({ChannelId})] 正在获取公开归档的帖子 (分页获取)...", channel.Name, channel.Id);
            var publicArchivedThreads = new List<IThreadChannel>();
            DateTimeOffset? beforeTimestamp = null; // 用作分页的锚点, 获取此时间戳之前的帖子
            int page = 1;

            while (true)
            {
                _logger.LogDebug("[{ChannelName}({ChannelId})] 正在获取第 {Page} 批公开归档帖子...", channel.Name, channel.Id, page);
                // GetPublicArchivedThreadsAsync 支持 before 参数来进行分页
                var retryCount = 0;
                IReadOnlyCollection<IThreadChannel>? fetchedThreads = null;
                while (retryCount < 3)
                {
                    try
                    {
                        fetchedThreads = await channel.GetPublicArchivedThreadsAsync(limit: 100, before: beforeTimestamp);
                        break;
                    }
                    catch (HttpException httpEx) when (httpEx.HttpCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        _logger.LogError(httpEx, "[{ChannelName}({ChannelId})] 获取公开归档的帖子时发生503错误，重试中...{RetryCount}/{MaxRetries}", channel.Name, channel.Id, retryCount, 3);
                        retryCount++;
                    }
                }

                if (fetchedThreads?.Count > 0)
                {
                    publicArchivedThreads.AddRange(fetchedThreads);
                    // 更新下一次请求的 'before' 时间戳为当前批次中最旧的帖子的归档时间
                    beforeTimestamp = fetchedThreads.Min(t => t.ArchiveTimestamp);
                    page++;
                }
                else
                {
                    // 如果返回的集合为空, 说明已经没有更多归档帖子了
                    _logger.LogDebug("所有公开归档帖子已获取完毕.");
                    break;
                }
            }
            threads.AddRange(publicArchivedThreads);
            _logger.LogInformation("获取了 {Count} 个公开归档的帖子.", publicArchivedThreads.Count);
        }
        var uniqueThreads = threads.DistinctBy(t => t.Id).ToList();

        return uniqueThreads;
    }

    public async Task<(List<ThreadDataModel>, ThreadProcessResult)> ProcessToThreadDataModelAsync(List<IThreadChannel> threads)
    {
        List<ThreadDataModel> threadDataModels = new List<ThreadDataModel>();
        _logger.LogInformation("开始并发处理 {Count} 个帖子", threads.Count);
        var semaphore = new SemaphoreSlim(26, 26);
        var results = new ConcurrentBag<ThreadDataModel>();
        var skippedCount = 0;
        var failedCount = 0;
        var num = 0;

        // 添加时间戳和性能统计变量
        var startTime = DateTime.Now;
        var processingTimes = new ConcurrentQueue<double>(); // 存储处理时间（秒）
        var lockObj = new object();

        // 创建处理任务
        var tasks = threads.Select(async thread =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (status, threadData) = await ToThreadDataModelAsync(thread, _requestOptions);
                if (threadData != null)
                {
                    results.Add(threadData);
                    var currentNum = Interlocked.Increment(ref num);

                    // 计算性能统计
                    var currentTime = DateTime.Now;
                    var elapsedSeconds = (currentTime - startTime).TotalSeconds;

                    lock (lockObj)
                    {
                        // 维护最近处理时间的队列
                        processingTimes.Enqueue(elapsedSeconds);
                        while (processingTimes.Count > 200)
                        {
                            processingTimes.TryDequeue(out _);
                        }

                        // 计算平均处理速度
                        var timesArray = processingTimes.ToArray();
                        if (timesArray.Length >= 2)
                        {
                            var timeSpan = timesArray[^1] - timesArray[0]; // 最新时间 - 最早时间
                            var avgSpeed = timeSpan > 0 ? (timesArray.Length - 1) / timeSpan : 0;

                            // 预估完成时间
                            var remainingTasks = threads.Count - currentNum;
                            var estimatedSecondsRemaining = avgSpeed > 0 ? remainingTasks / avgSpeed : 0;
                            var estimatedCompletionTime = currentTime.AddSeconds(estimatedSecondsRemaining);

                            _logger.LogDebug("第 {num}/{total} 个帖子处理完成 - 平均速度: {avgSpeed:F2} 任务/秒，预计完成时间: {estimatedTime:HH:mm:ss}",
                                currentNum, threads.Count, avgSpeed, estimatedCompletionTime);
                        }
                        else
                        {
                            _logger.LogDebug("第 {num}/{total} 个帖子处理完成 - 收集性能数据中...", currentNum, threads.Count);
                        }
                    }
                }
                else if (status == ProcessingStatus.Skipped)
                {
                    var currentNum = Interlocked.Increment(ref num);
                    _logger.LogWarning("第 {num}/{total} 个帖子: {ThreadId} 被跳过", currentNum, threads.Count, thread.Id);

                    Interlocked.Increment(ref skippedCount);
                }
                else if (status == ProcessingStatus.Failed)
                {
                    var currentNum = Interlocked.Increment(ref num);
                    _logger.LogError("第 {num}/{total} 个帖子: {ThreadId} 处理失败", currentNum, threads.Count, thread.Id);
                    Interlocked.Increment(ref failedCount);
                }
            }
            catch (TimeoutException ex)
            {
                var currentNum = Interlocked.Increment(ref num);
                _logger.LogWarning("第 {num}/{total} 个帖子: {ThreadId} 处理超时: {Message}", currentNum, threads.Count, thread.Id, ex.Message);
                Interlocked.Increment(ref failedCount);
            }
            catch (Exception ex)
            {
                var currentNum = Interlocked.Increment(ref num);
                _logger.LogError(ex, "第 {num}/{total} 个帖子: {ThreadId} 处理时发生错误", currentNum, threads.Count, thread.Id);
                Interlocked.Increment(ref failedCount);
            }
            finally
            {
                semaphore.Release();
            }
        });

        // 等待所有任务完成
        await Task.WhenAll(tasks);

        var successCount = results.Count;
        var totalElapsed = (DateTime.Now - startTime).TotalSeconds;
        var overallAvgSpeed = totalElapsed > 0 ? successCount / totalElapsed : 0;

        _logger.LogInformation("并发处理完成。成功: {SuccessCount}，失败: {FailedCount}，跳过: {SkippedCount}，总计: {Total}，总耗时: {TotalSeconds:F2}秒，平均速度: {OverallSpeed:F2} 任务/秒",
            successCount, failedCount, skippedCount, threads.Count, totalElapsed, overallAvgSpeed);

        // 构建结果数据
        var threadProcessResult = new ThreadProcessResult
        {
            SuccessCount = successCount,
            FailedCount = failedCount,
            SkippedCount = skippedCount,
            ThreadsCount = threads.Count,
            TotalElapsed = totalElapsed,
            OverallAvgSpeed = overallAvgSpeed
        };

        // 清空缓存
        forumCache.Clear();

        return (results.ToList(), threadProcessResult);
    }


    /// <summary>
    /// 将 IThreadChannel 对象转换为用于存储的 ThreadDataModel, 内置 Polly 重试策略。
    /// </summary>
    public async Task<(ProcessingStatus Status, ThreadDataModel? Data)> ToThreadDataModelAsync(IThreadChannel thread, RequestOptions requestOptions)
    {
        // 规则检查: 跳过锁定或置顶的帖子
        if (thread.IsLocked)
        {
            _logger.LogWarning("帖子 {ThreadName}({ThreadId}) 被锁定，已跳过", thread.Name, thread.Id);
            return (ProcessingStatus.Skipped, null);
        }
        if (thread.Flags.HasFlag(ChannelFlags.Pinned))
        {
            _logger.LogDebug("thread.Flags {thread.Flags}", thread.Flags);
            _logger.LogWarning("帖子 {ThreadName}({ThreadId}) 被置顶，已跳过", thread.Name, thread.Id);
            return (ProcessingStatus.Skipped, null);
        }

        IMessage? firstMessage = null;
        IEnumerable<IMessage>? recentMessages = null;

        try
        {
            // 创建一个 Polly 上下文, 用于在重试日志中传递额外信息
            var pollyContext = new Context($"ThreadProcessing-{thread.Id}", new Dictionary<string, object>
            {
                { "threadId", thread.Id },
                { "threadName", thread.Name }
            });

            // 使用 Polly 策略包装 API 调用代码块
            await _apiRetryPolicy.ExecuteAsync(async (ctx) =>
            {
                // 获取首楼消息
                firstMessage = await thread.GetMessageAsync(thread.Id, CacheMode.AllowDownload, requestOptions);
                // 获取最新消息
                recentMessages = await thread.GetMessagesAsync(1, CacheMode.AllowDownload, requestOptions).FlattenAsync();
            }, pollyContext); // 传递上下文
        }
        // 当 Polly 重试耗尽后仍然失败, 捕获最终的异常
        catch (Exception ex) when (ex is TaskCanceledException || ex is HttpException)
        {
            _logger.LogError(ex, "帖子 {ThreadName}({ThreadId}) 在多次重试后处理失败，已跳过", thread.Name, thread.Id);
            return (ProcessingStatus.Failed, null);
        }
        // 捕获其他非 Polly 处理的未知异常
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理帖子 {ThreadName}({ThreadId}) 时发生未知错误", thread.Name, thread.Id);
            return (ProcessingStatus.Failed, null);
        }

        // 验证消息有效性
        if (firstMessage == null || string.IsNullOrWhiteSpace(firstMessage.Content))
        {
            _logger.LogWarning("帖子 {ThreadName}({ThreadId}) 没有有效首楼消息，已跳过", thread.Name, thread.Id);
            return (ProcessingStatus.Skipped, null);
        }

        // 计算最后活跃时间
        long lastActiveAt;
        if (recentMessages?.Any() != true)
        {
            _logger.LogWarning("帖子 {ThreadName}({ThreadId}) 没有有效最新消息，使用首楼时间作为活跃时间", thread.Name, thread.Id);
            lastActiveAt = firstMessage.CreatedAt.ToUnixTimeSeconds();
        }
        else
        {
            lastActiveAt = recentMessages.Max(m => m.CreatedAt.ToUnixTimeSeconds());
        }

        // 提取数据
        var imageAttachment = firstMessage.Attachments.FirstOrDefault(a => IsImageAttachment(a));
        var highestReactionCount = firstMessage.Reactions.Values
            .Select(r => r.ReactionCount)
            .DefaultIfEmpty(0)
            .Max();

        string tags = "";
        IForumChannel? forumChannel;

        if (thread.CategoryId.HasValue && forumCache.TryGetValue(thread.CategoryId.Value, out forumChannel))
        {
            // 如果能进入这个 if 代码块，说明：
            // 1. thread.CategoryId 有值。
            // 2. 缓存中成功找到了这个ID。
            // 处理标签
            // 只获取该帖子实际应用的标签
            var appliedTagNames = forumChannel.Tags
                .Where(tag => thread.AppliedTags.Contains(tag.Id)) 
                .Select(tag => tag.Name)
                .ToList();
            tags = string.Join(", ", appliedTagNames);
        }
        else
        {
            // 如果缓存里没有，或者 CategoryId 本身就是 null，
            // 在这里执行获取频道、并存入缓存的逻辑。
            if (thread.CategoryId.HasValue)
            {
                forumChannel = await thread.Guild.GetChannelAsync(thread.CategoryId.Value, CacheMode.AllowDownload) as IForumChannel;
                if (forumChannel != null)
                {
                    // 将新获取到的频道信息存入缓存，以便后续使用
                    forumCache.TryAdd(thread.CategoryId.Value, forumChannel);
                    // 只获取该帖子实际应用的标签
                    var appliedTagNames = forumChannel.Tags
                        .Where(tag => thread.AppliedTags.Contains(tag.Id)) 
                        .Select(tag => tag.Name)
                        .ToList();
                    tags = string.Join(", ", appliedTagNames);
                }
                else
                {
                    _logger.LogWarning("帖子 {ThreadName}({ThreadId}) 没有有效频道ID (forumChannel is null)， thread.CategoryId: {CategoryId}，已跳过", thread.Name, thread.Id, thread.CategoryId);
                    return (ProcessingStatus.Failed, null);
                }
            }
            else
            {
                _logger.LogWarning("帖子 {ThreadName}({ThreadId}) 没有有效频道ID (No Has Value)，已跳过", thread.Name, thread.Id);
                return (ProcessingStatus.Failed, null);
            }
        }

        // 构建并返回数据模型
        return (ProcessingStatus.Success, new ThreadDataModel
        {
            ThreadId = thread.Id,
            FirstMessageId = firstMessage.Id,
            AuthorId = thread.OwnerId,
            ChannelId = (ulong)thread.CategoryId!,
            ChannelName = forumChannel.Name,
            GuildId = thread.GuildId,
            GuildName = thread.Guild.Name,
            CreatedAt = thread.CreatedAt.ToUnixTimeSeconds(),
            MemberCount = thread.MemberCount,
            ReactionCount = highestReactionCount,
            ReplyCount = thread.MessageCount,
            LastActiveAt = lastActiveAt,
            JumpUrl = $"https://discord.com/channels/{thread.GuildId}/{thread.Id}/0",
            AttachmentUrl = imageAttachment?.Url,
            Tags = tags,
            TagsId = thread.AppliedTags.ToList(),
            Title = thread.Name,
            Content = firstMessage.Content,
        });
    }

    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
}
