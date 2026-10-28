using System.Threading.RateLimiting;

using Discord;
using Discord.WebSocket;

using Microsoft.Extensions.DependencyInjection;

using Polly.Retry;

using Lunarium.Logging;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.OpenAPI;
using Lunarium.Scheduler.Interfaces;

using Lunarium.Bot.DataCenter.DataFetcher;

namespace Lunarium.Bot.DataCenter.Scheduler;

/// <summary>
/// 每 10 分钟拉取一次各部署服务器的活跃线程，把配置频道内的帖子 ID 累积进脏集合。
/// <para>
/// 作为 Listener 的兜底：网络抖动/掉线导致漏事件时，只要帖子仍在活跃列表里，
/// 下一次轮询就会把它补进集合，周更扫描不会漏掉它。
/// </para>
/// </summary>
public class ActiveThreadPollJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;
    private readonly ThreadActivityTracker _tracker;
    private readonly RateLimiter _apiRequestRateLimiter;
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    public ActiveThreadPollJob(
        ILogger logger,
        DiscordSocketClient client,
        ConfigLoader configLoader,
        ThreadActivityTracker tracker,
        RateLimiter rateLimiter,
        RequestOptions requestOptions,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy)
    {
        _logger = logger.ForContext("ActiveThreadPollJob");
        _client = client;
        _configLoader = configLoader;
        _tracker = tracker;
        _apiRequestRateLimiter = rateLimiter;
        _requestOptions = requestOptions;
        _httpRetryPolicy = httpRetryPolicy;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            var guildIds = _configLoader.Config.DeployConfig.ScanConfigs.Keys;
            var validChannels = _configLoader.ValidChannelSet;
            var trackedThisRound = 0;

            foreach (var guildId in guildIds)
            {
                var guild = _client.GetGuild(guildId);
                if (guild is null)
                {
                    _logger.Warning("⚠️ 活跃帖轮询跳过不存在的服务器: {GuildId}", guildId);
                    continue;
                }

                using var lease = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);

                var activeThreads = await _httpRetryPolicy.ExecuteAsync(
                    () => _client.GetAllActiveThreadsAsync(guild, _requestOptions));

                foreach (var thread in activeThreads)
                {
                    // 只关心配置中声明为有效扫描频道的帖子
                    if (!validChannels.Contains(thread.CategoryId ?? 0))
                    {
                        continue;
                    }

                    _tracker.Add(thread.Id);
                    trackedThisRound++;
                }
            }

            await _tracker.FlushAsync();
            _logger.Info("🔄 活跃帖轮询完成: 本轮 {RoundCount} 条, 集合总数 {TotalCount}",
                trackedThisRound, _tracker.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 活跃帖轮询失败");
        }
    }
}
