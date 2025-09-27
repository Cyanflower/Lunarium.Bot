// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Discord;
using Discord.Net;

using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.DataProcess;

public partial class DataProcessService
{

    // 频道缓存
    private ConcurrentDictionary<ulong, IForumChannel> forumCache = new();

    public async Task<List<IThreadChannel>> GetAllThreads(IForumChannel channel)
    {
        var threads = new List<IThreadChannel>();

        try
        {
            // 1. 获取活跃的帖子
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                _logger.Info("[{channel.Name}({channel.Id})] 正在获取活跃的帖子...",
                    channel.Name, channel.Id);
                var activeThreads = await channel.GetActiveThreadsAsync();
                _logger.Info("[{channel.Name}({channel.Id})] 成功获取了 {activeThreads.Count} 个活跃的帖子.",
                    channel.Name, channel.Id, activeThreads.Count);
                threads.AddRange(activeThreads);
            });

            // 2. 获取公开归档的帖子
            _logger.Info("[{channel.Name}({channel.Id})] 正在获取公开归档的帖子...", channel.Name, channel.Id);
            var publicArchivedThreads = new List<IThreadChannel>();
            DateTimeOffset? beforeTimestamp = null;
            int page = 1;

            while (true)
            {
                IReadOnlyCollection<IThreadChannel>? fetchedThreads = null;
                
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    _logger.Debug("[{channel.Name}({channel.Id})] 正在获取第 {page} 批公开归档帖子...",
                        channel.Name, channel.Id, page);
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
                    _logger.Info("[{channel.Name}({channel.Id})] 所有公开归档帖子已获取完毕.", channel.Name, channel.Id);
                    break;
                }
            }
            threads.AddRange(publicArchivedThreads);
            _logger.Info("[{channel.Name}({channel.Id})] 总共获取了 {publicArchivedThreads.Count} 个公开归档的帖子.", channel.Name, channel.Id, publicArchivedThreads.Count);

            var uniqueThreads = threads.DistinctBy(t => t.Id).ToList();
            _logger.Info("[{channel.Name}({channel.Id})] 总计获取到 {uniqueThreads.Count} 个去重帖子。", channel.Name, channel.Id, uniqueThreads.Count);

            return uniqueThreads;
        }
        catch (Exception ex)
        {
            // 当 Polly 重试用尽后，这里会捕获到最终的异常
            _logger.Error(ex, "[{channel.Name}({channel.Id})] 获取全部帖子过程中发生最终失败", channel.Name, channel.Id);
            throw;
        }
    }

    public async Task<(List<ThreadData>, ThreadProcessResult)> ProcessToThreadDataAsync(List<IThreadChannel> threads)
    {
        List<ThreadData> ThreadDatas = new List<ThreadData>();
        _logger.Info("开始并发处理 {threads.Count} 个帖子", threads.Count);
        var semaphore = new SemaphoreSlim(26, 26);
        var results = new ConcurrentBag<ThreadData>();
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
                var (status, threadData) = await ToThreadDataAsync(thread, _requestOptions);
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

                            _logger.Debug("第 {currentNum}/{threads.Count} 个帖子处理完成 - 平均速度: {avgSpeed:F2} 任务/秒，预计完成时间: {estimatedCompletionTime:HH:mm:ss}",
                            currentNum, threads.Count, avgSpeed, estimatedCompletionTime);
                        }
                        else
                        {
                            _logger.Debug("第 {currentNum}/{threads.Count} 个帖子处理完成 - 收集性能数据中...", currentNum, threads.Count);
                        }
                    }
                }
                else if (status == ProcessingStatus.Skipped)
                {
                    var currentNum = Interlocked.Increment(ref num);
                    _logger.Warning("第 {currentNum}/{threads.Count} 个帖子: {thread.Id} 被跳过", currentNum, threads.Count, thread.Id);

                    Interlocked.Increment(ref skippedCount);
                }
                else if (status == ProcessingStatus.Failed)
                {
                    var currentNum = Interlocked.Increment(ref num);
                    _logger.Warning("第 {currentNum}/{threads.Count} 个帖子: {thread.Id} 处理失败", currentNum, threads.Count, thread.Id);
                    Interlocked.Increment(ref failedCount);
                }
            }
            catch (TimeoutException ex)
            {
                var currentNum = Interlocked.Increment(ref num);
                _logger.Warning("第 {currentNum}/{threads.Count} 个帖子: {thread.Id} 处理超时: {ex.Message}", currentNum, threads.Count, thread.Id, ex.Message);
                Interlocked.Increment(ref failedCount);
            }
            catch (Exception ex)
            {
                var currentNum = Interlocked.Increment(ref num);
                _logger.Error(ex, "第 {currentNum}/{threads.Count} 个帖子: {thread.Id} 处理时发生错误", currentNum, threads.Count, thread.Id);
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

        _logger.Info("并发处理完成。成功: {successCount}，失败: {failedCount}，跳过: {skippedCount}，总计: {threads.Count}，总耗时: {totalElapsed:F2}秒，平均速度: {overallAvgSpeed:F2} 任务/秒",
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
    /// 将 IThreadChannel 对象转换为用于存储的 ThreadData, 内置 Polly 重试策略。
    /// </summary>
    public async Task<(ProcessingStatus Status, ThreadData? Data)> ToThreadDataAsync(IThreadChannel thread, RequestOptions requestOptions)
    {
        try
        {
            // 规则检查: 跳过锁定或置顶的帖子
            if (thread.IsLocked)
            {
                _logger.Info("帖子 {thread.Name}({thread.Id}) 被锁定，已跳过", thread.Name, thread.Id);
                return (ProcessingStatus.Skipped, null);
            }
            // _logger.Debug("thread.Flags {thread.Flags}", thread.Flags);
            if (thread.Flags.HasFlag(ChannelFlags.Pinned))
            {
                _logger.Info("帖子 {thread.Name}({thread.Id}) 被置顶，已跳过", thread.Name, thread.Id);
                return (ProcessingStatus.Skipped, null);
            }

            IMessage? firstMessage = null;
            IEnumerable<IMessage>? recentMessages = null;

            // 使用 Polly 策略包装 API 调用代码块
            await _apiRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息
                firstMessage = await thread.GetMessageAsync(thread.Id, CacheMode.AllowDownload, requestOptions);
                // 获取最新消息
                recentMessages = await thread.GetMessagesAsync(1, CacheMode.AllowDownload, requestOptions).FlattenAsync();
            });


            // 验证消息有效性
            if (firstMessage == null || string.IsNullOrWhiteSpace(firstMessage.Content))
            {
                _logger.Info("帖子 {thread.Name}({thread.Id}) 没有有效首楼消息，已跳过", thread.Name, thread.Id);
                return (ProcessingStatus.Skipped, null);
            }

            // 计算最后活跃时间
            long lastActiveAt;
            if (recentMessages?.Any() != true)
            {
                _logger.Info("帖子 {thread.Name}({thread.Id}) 没有有效最新消息，使用首楼时间作为活跃时间", thread.Name, thread.Id);
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
            IForumChannel? forumChannel = null;

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
                    
                    await _apiRetryPolicy.ExecuteAsync(async () =>
                    {
                        forumChannel = await thread.Guild.GetChannelAsync(thread.CategoryId.Value, CacheMode.AllowDownload) as IForumChannel;
                    });
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
                            _logger.Warning("帖子 {thread.Name}({thread.Id}) 没有有效频道ID (forumChannel is null)， thread.CategoryId: {CategoryId}，已跳过",
                            thread.Name, thread.Id, thread.CategoryId);
                            return (ProcessingStatus.Failed, null);
                        }
                }
                else
                {
                    _logger.Warning("帖子 {thread.Name}({thread.Id}) 没有有效频道ID (No Has Value)，已跳过", thread.Name, thread.Id);
                    return (ProcessingStatus.Failed, null);
                }
            }
            // 构建并返回数据模型
            return (ProcessingStatus.Success, new ThreadData
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
        // 当 Polly 重试耗尽后仍然失败, 捕获最终的异常
        catch (Exception ex) when (ex is TaskCanceledException || ex is HttpException)
        {
            _logger.Warning("帖子 {thread.Name}({thread.Id}) 在多次重试后处理失败，已跳过");
            return (ProcessingStatus.Failed, null);
        }
        // 捕获其他非 Polly 处理的未知异常
        catch (Exception ex)
        {
            _logger.Error(ex, "处理帖子 {thread.Name}({thread.Id}) 时发生未知错误", thread.Name, thread.Id);
            return (ProcessingStatus.Failed, null);
        }
    }

}
