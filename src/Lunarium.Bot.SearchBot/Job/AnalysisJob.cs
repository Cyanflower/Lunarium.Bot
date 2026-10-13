using System.Collections.Concurrent;
using System.Text;
using Discord;
using Discord.WebSocket;
using Lunarium.Bot.Logger;
using Lunarium.Bot.SearchBot.Utils;
using Lunarium.Bot.Scheduler.Interfaces;

namespace Lunarium.Bot.SearchBot.Scheduler;

/// <summary>
/// 一个定时任务，负责执行数据分析、生成报告并通过 Embed 消息推送到各服务器的 Dashboard 频道。
/// 支持多服务器独立推送，每个服务器接收各自的数据分析报告。
/// </summary>
public class AnalysisJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly DataAnalysis _dataAnalysis;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;

    // 线程安全的频道缓存字典，避免重复调用 API 获取频道信息
    private readonly ConcurrentDictionary<ulong, IMessageChannel> _dashboardChannelCache = new();

    // 为 Discord API 请求设置统一的选项，包括较长的超时时间和自动重试策略
    private readonly RequestOptions _requestOptions = new RequestOptions
    {
        Timeout = 5 * 60 * 1000, // 设置总超时为 5 分钟
        RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试机制
    };

    public AnalysisJob(ILogger logger, ConfigLoader configLoader, DataAnalysis dataAnalysis, DiscordSocketClient client)
    {
        _logger = logger.ForContext("AnalysisJob");
        _configLoader = configLoader;
        _dataAnalysis = dataAnalysis;
        _client = client;
    }

    /// <summary>
    /// Coravel 调度器调用的主入口方法
    /// </summary>
    public async Task ExecuteAsync()
    {
        _logger.Info("🚀 分析与报告推送任务开始执行...");

        // 用于跟踪成功处理的频道，以便后续清理失效缓存
        var successfullyProcessedChannels = new ConcurrentBag<ulong>();

        try
        {
            // === 1. 数据准备阶段 ===
            _logger.Info("📊 正在生成所有服务器的数据分析报告...");
            var allReports = await _dataAnalysis.GenerateAllGuildReportsAsync();

            if (!allReports.Any())
            {
                _logger.Warning("⚠️ 数据分析未生成任何报告，本次任务提前结束。");
                return;
            }

            _logger.Info("✅ 数据分析完成，共为 {Count} 个服务器生成报告", allReports.Count);

            // === 2. 并发推送阶段 ===
            _logger.Info("📤 开始并发推送报告到各服务器的 Dashboard 频道...");

            var pushTasks = allReports.Select(async kvp =>
            {
                var guildId = kvp.Key;
                var reports = kvp.Value;
                var taskLogger = _logger.ForContext($"Task: Guild-{guildId}");

                try
                {
                    // 获取该服务器的配置
                    if (!_configLoader.Config.DeployConfig.GuildAnalysisConfigs.TryGetValue(guildId, out var guildConfig))
                    {
                        taskLogger.Warning("⚠️ 服务器 {GuildId} 缺少 GuildAnalysisConfig 配置，跳过推送", guildId);
                        return;
                    }

                    var dashboardChannelId = guildConfig.DashboardChannelId;
                    taskLogger.Info("🎯 目标 Dashboard 频道: {ChannelId}", dashboardChannelId);

                    // 获取 Dashboard 频道（优先从缓存）
                    var dashboardChannel = await GetOrCacheDashboardChannelAsync(dashboardChannelId, taskLogger);
                    if (dashboardChannel is null)
                    {
                        taskLogger.Warning("⚠️ 无法获取 Dashboard 频道 {ChannelId}，跳过推送", dashboardChannelId);
                        return;
                    }

                    // 转换为 Discord Embed
                    taskLogger.Info("🔄 正在将报告转换为 Discord Embed 格式...");
                    var embedDict = DisplayEmbedBuilder.ForumInfoToEmbed(reports);

                    // 按配置的 ChannelOrder 排序
                    var sortedEmbeds = SortEmbedsByOrder(embedDict, guildId);

                    if (!sortedEmbeds.Any())
                    {
                        taskLogger.Warning("⚠️ 未生成任何 Embed，跳过推送");
                        return;
                    }

                    taskLogger.Info("📝 已生成 {Count} 个 Embed，开始发送...", sortedEmbeds.Count);

                    // 串行发送所有 Embed（保证消息顺序）
                    var sentMessages = new List<(string category, IUserMessage message)>();
                    foreach (var embed in sortedEmbeds)
                    {
                        var categoryName = embed.Title ?? "未知分类";
                        var sentMessage = await dashboardChannel.SendMessageAsync(embed: embed, options: _requestOptions);
                        sentMessages.Add((categoryName, sentMessage));
                    }

                    taskLogger.Info("✅ 成功发送 {Count} 条 Embed 报告", sentMessages.Count);

                    // 构建并发送摘要/索引消息
                    await SendSummaryMessageAsync(dashboardChannel, sentMessages, taskLogger);

                    // 标记该频道为成功处理
                    successfullyProcessedChannels.Add(dashboardChannelId);
                    taskLogger.Info("🎉 服务器 {GuildId} 的报告推送完成！", guildId);
                }
                catch (Exception ex)
                {
                    taskLogger.Error(ex, "❌ 向服务器 {GuildId} 推送报告时发生严重错误", guildId);
                }
            });

            // 等待所有推送任务完成
            await Task.WhenAll(pushTasks);
            _logger.Info("✅ 所有服务器的推送任务均已执行完毕");
        }
        catch (Exception ex)
        {
            _logger.Critical(ex, "💥 AnalysisJob 在主执行流程中发生严重异常，任务可能未完成");
        }
        finally
        {
            // === 3. 缓存清理阶段 ===
            CleanupFailedChannelsFromCache(successfullyProcessedChannels);
            _logger.Info("🏁 分析与报告推送任务执行结束");
        }
    }

    /// <summary>
    /// 获取或缓存 Dashboard 频道
    /// </summary>
    private async Task<IMessageChannel?> GetOrCacheDashboardChannelAsync(ulong channelId, ILogger logger)
    {
        // 尝试从缓存获取
        if (_dashboardChannelCache.TryGetValue(channelId, out var cachedChannel))
        {
            logger.Debug("✅ 从缓存中获取频道 {ChannelId}", channelId);
            return cachedChannel;
        }

        // 缓存未命中，通过 API 获取
        logger.Debug("🔍 频道 {ChannelId} 不在缓存中，尝试通过 API 获取...", channelId);
        var channel = await _client.GetChannelAsync(channelId);

        if (channel is IMessageChannel messageChannel)
        {
            // 添加到缓存
            if (_dashboardChannelCache.TryAdd(channelId, messageChannel))
            {
                logger.Info("✅ 成功获取频道 {ChannelId} 并已添加到缓存", channelId);
            }
            return messageChannel;
        }

        logger.Warning("⚠️ 频道 {ChannelId} 不是有效的 IMessageChannel", channelId);
        return null;
    }

    /// <summary>
    /// 按配置的 ChannelOrder 对 Embed 排序
    /// </summary>
    private List<Embed> SortEmbedsByOrder(Dictionary<string, Embed> embedDict, ulong guildId)
    {
        var sortedKeys = embedDict.Keys
            .OrderBy(key => key == "总概览" ? -1 : _configLoader.Config.ChannelOrder.GetValueOrDefault(key, int.MaxValue))
            .ToList();

        return sortedKeys.Select(key => embedDict[key]).ToList();
    }

    /// <summary>
    /// 发送摘要/索引消息
    /// </summary>
    private async Task SendSummaryMessageAsync(
        IMessageChannel channel,
        List<(string category, IUserMessage message)> sentMessages,
        ILogger logger)
    {
        logger.Info("📋 正在构建并发送摘要信息...");

        var summaryBuilder = new StringBuilder();
        var currentTime = DateTimeOffset.UtcNow.AddHours(8); // UTC+8 时间
        summaryBuilder.AppendLine($"## 📊 [{currentTime:yyyy-MM-dd}] 信息索引");

        foreach (var (category, message) in sentMessages)
        {
            summaryBuilder.AppendLine($"> - **{category}**: [点击跳转]({message.GetJumpUrl()})");
        }

        summaryBuilder.AppendLine("-# 每天定时更新，报告记录保存在本频道内，可通过搜索 [时间] 进行查找。");

        await channel.SendMessageAsync(summaryBuilder.ToString());
        logger.Info("✅ 摘要信息发送成功！");
    }

    /// <summary>
    /// 清理失效的频道缓存
    /// </summary>
    private void CleanupFailedChannelsFromCache(ConcurrentBag<ulong> successfulChannels)
    {
        _logger.Info("🧹 开始清理无效的 Dashboard 频道缓存...");

        var successSet = new HashSet<ulong>(successfulChannels);
        int removedCount = 0;

        foreach (var cachedChannelId in _dashboardChannelCache.Keys)
        {
            if (!successSet.Contains(cachedChannelId))
            {
                if (_dashboardChannelCache.TryRemove(cachedChannelId, out _))
                {
                    _logger.Info("🗑️ 已从缓存中移除无效或处理失败的频道: {ChannelId}", cachedChannelId);
                    removedCount++;
                }
            }
        }

        _logger.Info("✅ 缓存清理完成，共移除 {Count} 个频道", removedCount);
    }
}
