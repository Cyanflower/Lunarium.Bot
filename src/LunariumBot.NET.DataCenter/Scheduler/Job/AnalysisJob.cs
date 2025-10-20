using System.Collections.Concurrent;
using System.Text;
using Coravel.Invocable;
using Discord;
using Discord.WebSocket;
using LunariumBot.Net.Logger;
using LunariumBot.Net.DataCenter.Utils;

namespace LunariumBot.Net.DataCenter.Scheduler;

/// <summary>
/// 一个定时任务，负责执行数据分析、生成报告并通过Embed消息推送到指定的Dashboard频道。
/// 这个任务被设计为可并发处理多个频道，并包含缓存和错误处理机制。
/// </summary>
public class AnalysisJob : IInvocable
{
    // 日志记录器实例
    private readonly ILogger _logger;
    // 数据分析服务
    private readonly DataAnalysis _dataAnalysis;
    // Discord客户端实例
    private readonly DiscordSocketClient _client;
    // 配置加载器
    private readonly ConfigLoader _configLoader;
    
    // 使用信号量来限制并发执行的任务数量，防止对Discord API造成过大压力。
    // 初始化为20，意味着最多同时处理20个频道的推送任务。
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(20, 20);
    
    // 线程安全的字典，用于缓存IMessageChannel对象，避免重复调用API获取频道信息，提高效率。
    private readonly ConcurrentDictionary<ulong, IMessageChannel> _dashboardChannelCache = new();
    
    // 为Discord API请求设置统一的选项，包括较长的超时时间和自动重试策略。
    private readonly RequestOptions _requestOptions = new RequestOptions
    {
        Timeout = 5 * 60 * 1000, // 设置总超时为 5 分钟
        RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试机制
    };

    public AnalysisJob(ILogger logger, ConfigLoader configLoader, DataAnalysis dataAnalysis, DiscordSocketClient client)
    {
        // 使用 ForContext 为这个任务创建一个专用的日志记录器，所有日志都会自动带上 "AnalysisJob" 上下文。
        _logger = logger.ForContext("AnalysisJob");
        _configLoader = configLoader;
        _dataAnalysis = dataAnalysis;
        _client = client;
    }

    /// <summary>
    /// Coravel调度器调用的主入口方法。
    /// </summary>
    public async Task Invoke()
    {
        _logger.Info("🚀 分析与报告推送任务开始执行...");
        
        // 创建一个线程安全的集合，用于在并发环境中安全地收集本次成功处理的频道ID。
        var successfullyProcessedChannels = new ConcurrentBag<ulong>();

        try
        {
            // --- 1. 数据准备阶段 ---
            _logger.Info("正在执行数据分析...");
            var dataList = await _dataAnalysis.StartAnalysis();

            _logger.Info("数据分析完成，正在生成Embed报告...");
            var dataDict = DisplayEmbedBuilder.ForumInfoToEmbed(dataList);

            _logger.Info("根据配置文件中的ChannelOrder对报告进行排序...");
            var sortedKeys = dataDict.Keys
                .OrderBy(key => key == "总概览" ? -1 : _configLoader.Config.ChannelOrder.GetValueOrDefault(key, int.MaxValue))
                .ToList();
            
            var embedList = sortedKeys.Select(key => dataDict[key]).ToArray();

            if (!embedList.Any())
            {
                _logger.Warning("数据分析未生成任何Embed报告，本次任务提前结束。");
                return;
            }

            _logger.Info("已成功生成并排序了 {embedList.Length} 个分析报告 Embed，准备推送到所有 Dashboard 频道...", embedList.Length);

            // --- 2. 并发推送阶段 ---
            // 使用 LINQ 的 Select 创建一个异步任务列表，每个任务负责处理一个Dashboard频道。
            var tasks = _configLoader.Config.DeployConfig.DashboardChannels.Select(async channelId =>
            {
                // 在进入任务处理前，等待信号量。如果并发数已满，这里会异步等待。
                await _semaphore.WaitAsync();
                var taskLogger = _logger.ForContext($"Channel-{channelId}"); // 为每个并发任务创建独立的日志上下文

                try
                {
                    // 尝试从缓存中获取频道对象，如果失败则通过API获取并存入缓存。
                    if (!_dashboardChannelCache.TryGetValue(channelId, out var dashboardChannel))
                    {
                        taskLogger.Debug("频道不在缓存中，尝试通过API获取...");
                        var channel = await _client.GetChannelAsync(channelId);
                        if (channel is IMessageChannel messageChannel)
                        {
                            dashboardChannel = messageChannel;
                            // 使用 TryAdd 保证线程安全地添加新条目到缓存
                            if (_dashboardChannelCache.TryAdd(channelId, dashboardChannel))
                            {
                                taskLogger.Info("成功获取频道并已添加到缓存。");
                            }
                        }
                    }

                    if (dashboardChannel != null)
                    {
                        taskLogger.Info("开始向该频道发送报告...");
                        var messages = new List<(string category, IUserMessage message)>();
                        
                        // 串行发送所有Embed，以保证在一个频道内的消息顺序是固定的。
                        foreach (var embed in embedList)
                        {
                            var categoryName = embed.Title.Replace("📄 分析项: ", "");
                            var sentMessage = await dashboardChannel.SendMessageAsync(embed: embed, options: _requestOptions);
                            messages.Add((categoryName, sentMessage));
                        }
                        taskLogger.Debug($"已发送 {messages.Count} 条Embed报告。");

                        // 构建并发送摘要/索引消息。
                        taskLogger.Info("正在构建并发送摘要信息...");
                        var updateInfo = new StringBuilder();
                        var currentTime = DateTimeOffset.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
                        updateInfo.AppendLine($"## 📊 [{currentTime}] 信息索引");
                        
                        foreach (var (category, message) in messages)
                        {
                            // 格式化每一条报告的跳转链接
                            updateInfo.AppendLine($"> - **{category}**: [点击跳转]({message.GetJumpUrl()})");
                        }
                        
                        updateInfo.AppendLine("-# 每天凌晨 4:30 (UTC+8) 更新，报告记录保存在本频道内，可通过搜索[时间]进行查找。");

                        await dashboardChannel.SendMessageAsync(updateInfo.ToString());
                        taskLogger.Info("摘要信息发送成功！");
                        
                        // 标记这个频道为成功处理，将其ID添加到成功列表中。
                        successfullyProcessedChannels.Add(channelId);
                    }
                    else
                    {
                         // 修复：在这里使用 taskLogger 记录带上下文的日志
                         taskLogger.Warning("无法获取该频道或该频道不是有效的IMessageChannel，已跳过处理。");
                    }
                }
                catch (Exception ex)
                {
                    // 修复：在这里使用 taskLogger 记录带上下文的错误，并传递异常对象
                    taskLogger.Error("向此频道推送分析报告时发生严重错误。", ex);
                }
                finally
                {
                    // 无论成功与否，任务结束时都必须释放信号量，以便其他等待的任务可以继续执行。
                    _semaphore.Release();
                }
            });

            // 异步等待所有创建的推送任务完成。
            await Task.WhenAll(tasks);
            _logger.Info("所有频道的推送任务均已执行完毕。");
        }
        catch (Exception ex)
        {
            // 捕获任务执行过程中的顶层异常，例如数据分析失败等。
            _logger.Critical("AnalysisJob 在主执行流程中发生严重异常，任务可能未完成。", ex);
        }
        finally
        {
            // --- 3. 缓存清理阶段 ---
            _logger.Info("开始清理无效的 Dashboard 频道缓存...");

            // 将 ConcurrentBag 转换为 HashSet 以提高查找效率 (O(1))
            var successSet = new HashSet<ulong>(successfullyProcessedChannels);

            // 遍历当前缓存的所有键
            foreach (var cachedChannelId in _dashboardChannelCache.Keys)
            {
                // 如果缓存中的ID不在本次成功处理的列表里，说明该频道可能已失效或本次处理失败，应从缓存移除。
                if (!successSet.Contains(cachedChannelId))
                {
                    // 使用线程安全的 TryRemove
                    if (_dashboardChannelCache.TryRemove(cachedChannelId, out _))
                    {
                        _logger.Info("已从缓存中移除无效或处理失败的频道: {cachedChannelId}", cachedChannelId);
                    }
                }
            }
            _logger.Info("缓存清理完成。任务执行结束。🏁");
        }
    }
}