using System.Threading.RateLimiting;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using System.Net.Sockets;
using Polly;
using Polly.Retry;
using Discord;
using Discord.Net;
using Discord.WebSocket;

using LunariumBot.Net.Logger;
using LunariumBot.Net.Scheduler;
using LunariumBot.Net.Scheduler.Interfaces;

using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.UrlSignedInfoCache;
using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.DataCenter.Models;
using Discord.Rest;

namespace LunariumBot.Net.DataCenter.Scheduler;
public class UrlSignedInfoCacheCheckAsyncJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly UrlSignedInfoCacheService _urlSignedInfoCacheService;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _client;
    private IChannel? _cacheLogChannel;
    private readonly RateLimiter _apiRequestRateLimiter;
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private ConcurrentDictionary<ulong, IMessageChannel> _channelCache = new();

    public UrlSignedInfoCacheCheckAsyncJob(
        ILogger logger, ConfigLoader configLoder,
        DatabaseService databaseService,
        UrlSignedInfoCacheService urlSignedInfoCacheService,
        DiscordSocketClient client,
        RateLimiter rateLimiter,
        RequestOptions requestOptions,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy,
        [FromKeyedServices("DbPolicy")] AsyncRetryPolicy dbRetryPolicy
    )
    {
        _logger = logger.ForContext("UrlSignedInfoCacheCheckJob");
        _configLoader = configLoder;
        _databaseService = databaseService;
        _urlSignedInfoCacheService = urlSignedInfoCacheService;
        _client = client;
        _apiRequestRateLimiter = rateLimiter;
        _requestOptions = requestOptions;
        _httpRetryPolicy = httpRetryPolicy;
        _dbRetryPolicy = dbRetryPolicy;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            _logger.Info("定时检查URL签名过期开始");
            // 执行同步扫描
            var checkResult = _urlSignedInfoCacheService.HourlyCheck();

            var embed = EmbedUtils.SuccessEmbed(_urlSignedInfoCacheService.GetCacheStatistics(), "定时同步缓存更新成功");

            List<ulong> needGetChannel = new();

            foreach (var info in checkResult)
            {
                if (!_channelCache.ContainsKey(info.ChannelId)) needGetChannel.Add(info.ChannelId);
            }

            var getChannelTask = needGetChannel.Select(id => Task.Run(async () =>
            {
                try
                {
                    using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
                    if (!apiRateLimitToken.IsAcquired) 
                    {
                        _logger.Error("_apiRequestRateLimiter 队列已满");
                        // 只记录日志
                        // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
                    }
                    var channel = await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        return await _client.GetChannelAsync(id, options: _requestOptions);
                    });
                    if (channel is IMessageChannel messageChannel) _channelCache.TryAdd(channel.Id, messageChannel);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取 Discord 频道失败 (ChannelId: {ChannelId})，将跳过该频道的缓存检查。", id);
                }
            }));

            await Task.WhenAll(getChannelTask);

            ConcurrentBag<(UrlSignedInfo OldItem, UrlSignedInfo? NewItem)> getResult = new();
            ConcurrentBag<(ulong threadId, string? urls)> toDbData = new();

            var getResultTask = checkResult.Select(info => Task.Run(async () =>
            {
                if (_channelCache.TryGetValue(info.ChannelId, out var channel))
                {
                    try
                    {
                        using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
                        if (!apiRateLimitToken.IsAcquired) 
                        {
                            _logger.Error("_apiRequestRateLimiter 队列已满");
                            // 只记录日志
                            // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
                        }
                        var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            return await channel.GetMessageAsync(info.MessageId, options: _requestOptions);
                        });

                        if (message is null) 
                        {
                            getResult.Add((info, null));
                            toDbData.Add((info.ChannelId, null));
                            return;
                        }

                        List<string> imageAttachment = new();

                        if (message is SocketMessage socketMessage)
                        {
                            imageAttachment = socketMessage.Attachments
                                .Where(a => IsImageAttachment(a))
                                .Select(a => a.Url)
                                .ToList();
                        }
                        if (message is RestMessage restMessage)
                        {
                            imageAttachment = restMessage.Attachments
                                .Where(a => IsImageAttachment(a))
                                .Select(a => a.Url)
                                .ToList();
                        }

                        if (imageAttachment.Count > 0 && !string.IsNullOrWhiteSpace(imageAttachment[0]))
                        {
                            // 提取首楼消息中的图片附件
                            var newInfo = new UrlSignedInfo(imageAttachment[0], channel.Id, message.Id);
                            getResult.Add((info, newInfo));
                            toDbData.Add((info.ChannelId, JsonSerializer.Serialize(imageAttachment, JsonSerializerConfig.Options)));
                        }
                        else
                        {
                            getResult.Add((info, null));
                            toDbData.Add((info.ChannelId, null));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex);
                        getResult.Add((info, null));
                        toDbData.Add((info.ChannelId, null));
                        _logger.Error(ex, "❌ 处理频道消息时发生异常，无法提取图片链接 (ChannelId: {ChannelId}, MessageId: {MessageId})", info.ChannelId, info.MessageId);
                    }
                }
                else
                {
                    getResult.Add((info, null));
                    toDbData.Add((info.ChannelId, null));
                }
            }));

            await Task.WhenAll(getResultTask);

            try
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await _dbRetryPolicy.ExecuteAsync(async () =>
                {
                    await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();
                    try
                    {
                        await _databaseService.UpdateUrlInfoAsync(transaction, toDbData.ToList());
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });

                _urlSignedInfoCacheService.UpdateCache(getResult);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 批量更新 URL 签名信息到数据库时发生错误，事务已回滚。涉及数据量: {Count}", toDbData.Count);
            }

            _logger.Info("定时检查URL签名过期完成");

            // 判断是否有缓存或者已有缓存是否和配置相同(检查配置更新)
            if (_cacheLogChannel is not IMessageChannel || _cacheLogChannel.Id != _configLoader.Config.AuthConfig.LogChannel)
            {
                _cacheLogChannel = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogChannel);
            }

            // 转换为通用可发消息频道
            if (_cacheLogChannel is IMessageChannel logChannel)
            {
                await logChannel.SendMessageAsync(embed: embed);
            }
            else
            {
                _logger.Error("配置的日志频道 (ID: {_configLoader.Config.AuthConfig.LogChannel}) 不是一个有效的文本或线程频道，无法发送日志！", _configLoader.Config.AuthConfig.LogChannel);
            }

        }
        catch (Exception ex)
        {
            _logger.Error("UrlSignedInfoCacheCheckJob 执行时发生未处理的异常:", ex);
        }
    }

    /// <summary>
    /// 判断附件是否为图片类型
    /// </summary>
    /// <param name="attachment">Discord 消息附件</param>
    /// <returns>如果是图片类型返回 true, 否则返回 false</returns>
    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }

    /// <summary>
    /// 配置 HTTP 请求重试策略（基于 Polly）。
    /// 该策略专门处理 Discord API 和网络相关的临时性错误。
    /// 使用指数退避策略：2秒、4秒、8秒。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
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

    /// <summary>
    /// 配置数据库操作重试策略（基于 Polly）。
    /// 该策略专门处理 PostgreSQL 数据库的临时性错误。
    /// 使用指数退避策略加随机抖动：2秒、4秒、8秒 + 0-100ms 随机延迟。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    private AsyncRetryPolicy DbRetryPolicy()
    {
        return Policy
            // 定义需要处理的异常:
            // - NpgsqlException: 处理 Npgsql 的暂时性故障（如连接池耗尽）
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            // - TimeoutException: 处理连接或查询超时
            .Or<TimeoutException>()
            // - SocketException: 处理底层网络连接问题
            .Or<SocketException>()

            // 使用指数退避策略 + 随机抖动
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                onRetry: (exception, timespan, attempt, context) =>
                {
                    _logger.Warning("⚠️ 数据库操作失败，将在 {Seconds:F2} 秒后进行第 {Attempt}/3 次重试",
                        timespan.TotalSeconds, attempt);
                }
            );
    }
}