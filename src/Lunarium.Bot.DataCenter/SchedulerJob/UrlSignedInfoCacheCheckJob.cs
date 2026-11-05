using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using System.Net.Sockets;
using Polly;
using Polly.Retry;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Scheduler.Interfaces;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.RateLimiting;

using Lunarium.Bot.DataCenter.Database;
using Lunarium.Bot.DataCenter.UrlSignedInfoCache;
using Lunarium.Bot.DataCenter.Utils;
using Lunarium.Bot.DataCenter.Models;

namespace Lunarium.Bot.DataCenter.Scheduler;
public class UrlSignedInfoCacheCheckAsyncJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly UrlSignedInfoCacheService _urlSignedInfoCacheService;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _client;
    // private IChannel? _cacheLogChannel;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly ConcurrentDictionary<ulong, IMessageChannel> _channelCache = new();

    public UrlSignedInfoCacheCheckAsyncJob(
        ILogger logger, ConfigLoader configLoder,
        DatabaseService databaseService,
        UrlSignedInfoCacheService urlSignedInfoCacheService,
        DiscordSocketClient client,
        RateLimitedExecutor rateLimitedExecutor,
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
        _rateLimitedExecutor = rateLimitedExecutor;
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
            UrlCheckResult checkResult = _urlSignedInfoCacheService.HourlyCheck();

            if (checkResult.UrlSigneds.Count == 0)
            {
                _logger.Info(
                    "本轮没有待处理的 URL（桶 [{Buckets}]），跳过",
                    string.Join(",", checkResult.BucketIndices));
                return;
            }

            await CheckChannelCache(checkResult);

            ConcurrentBag<(ulong threadId, Jsonb<List<string>> urls)> toDbData = new();
            ConcurrentBag<UrlSignedInfo> toRequeue = new();

            var getResultTask = checkResult.UrlSigneds.Select(info => Task.Run(async () =>
            {
                if (_channelCache.TryGetValue(info.ChannelId, out var channel))
                {
                    try
                    {
                        var message = await _httpRetryPolicy.ExecuteAsync(
                            token => _rateLimitedExecutor.WriteAndWaitAsync(
                                async _ => await channel.GetMessageAsync(info.MessageId, options: _requestOptions),
                                token),
                            CancellationToken.None);

                        if (message is null)
                        {
                            toDbData.Add((info.ChannelId, new Jsonb<List<string>>(null)));
                            return;
                        }

                        List<string> imageAttachment = new();

                        if (message is SocketMessage socketMessage)
                        {
                            imageAttachment = socketMessage.Attachments
                                .Where(a => IsImageAttachment(a))
                                .Select(a => a.Url)
                                .ToList();
                            // 外链图片以embed形式存在（括号不能省：Gifv 可能没有 Thumbnail）
                            imageAttachment.AddRange(socketMessage.Embeds
                                .Where(embed => embed.Thumbnail is not null && (embed.Type == EmbedType.Image || embed.Type == EmbedType.Gifv))
                                .Select(embed => embed.Thumbnail!.Value.Url)
                                .ToList()
                            );
                        }
                        if (message is RestMessage restMessage)
                        {
                            imageAttachment = restMessage.Attachments
                                .Where(a => IsImageAttachment(a))
                                .Select(a => a.Url)
                                .ToList();
                            // 外链图片以embed形式存在（括号不能省：Gifv 可能没有 Thumbnail）
                            imageAttachment.AddRange(restMessage.Embeds
                                .Where(embed => embed.Thumbnail is not null && (embed.Type == EmbedType.Image || embed.Type == EmbedType.Gifv))
                                .Select(embed => embed.Thumbnail!.Value.Url)
                                .ToList()
                            );
                        }

                        if (imageAttachment.Count > 0 && !string.IsNullOrWhiteSpace(imageAttachment[0]))
                        {
                            // 无论如何都回库；是否继续维护由下一次 DailySync 的 TryToDomain 判断
                            toDbData.Add((info.ChannelId, new Jsonb<List<string>>(imageAttachment)));
                        }
                        else
                        {
                            toDbData.Add((info.ChannelId, new Jsonb<List<string>>(null)));
                        }
                    }
                    catch (Exception ex)
                    {
                        // 临时失败：不回写 null（否则下次 GetAllUrlInfoAsync 会把这条永久过滤掉），
                        // 塞进下一个桶，下一小时重试。
                        toRequeue.Add(info);
                        _logger.Error(ex, "❌ 处理频道消息时发生异常，本条已排入下一个桶重试 (ChannelId: {ChannelId}, MessageId: {MessageId})", info.ChannelId, info.MessageId);
                    }
                }
                else
                {
                    // 频道没取到属于临时失败：保留旧行，塞进下一个桶重试
                    toRequeue.Add(info);
                }
            }));

            await Task.WhenAll(getResultTask);

            // 本轮失败（临时异常/频道取不到）的条目回插到下一个桶，下一小时重试
            _urlSignedInfoCacheService.RequeueToNextBucket(toRequeue);

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
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 批量更新 URL 签名信息到数据库时发生错误，事务已回滚。涉及数据量: {Count}", toDbData.Count);
            }

            _logger.Info("定时检查URL签名过期完成");

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UrlSignedInfoCacheCheckJob 执行时发生未处理的异常:");
        }
    }

    private async Task CheckChannelCache(UrlCheckResult urlCheckResult)
    {
        try
        {
            // 使用 Distinct() 防止同一个 Channel 被重复请求多次
            var distinct = urlCheckResult.UrlSigneds
                .Select(x => x.ChannelId)
                .Distinct()
                .Where(id => !_channelCache.ContainsKey(id))
                .ToList();

            List<ulong> needGetChannelIds = new();

            foreach (var id in distinct)
            {
                if (!_channelCache.ContainsKey(id)) 
                    needGetChannelIds.Add(id);
            }
    
            var getChannelTask = needGetChannelIds.Select(async (id) =>
            {
                try
                {
                    var channel = await _httpRetryPolicy.ExecuteAsync(
                        token => _rateLimitedExecutor.WriteAndWaitAsync(
                            async _ => await _client.GetChannelAsync(id, options: _requestOptions),
                            token),
                        CancellationToken.None);
                    if (channel is IMessageChannel messageChannel) _channelCache.TryAdd(channel.Id, messageChannel);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 获取 Discord 频道失败 (ChannelId: {ChannelId})，将跳过该频道的缓存检查。", id);
                }
            });
    
            await Task.WhenAll(getChannelTask);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "检查频道缓存失败");
        }
    }

    /// <summary>
    /// 判断附件是否为图片类型
    /// </summary>
    /// <param name="attachment">Discord 消息附件</param>
    /// <returns>如果是图片类型返回 true, 否则返回 false</returns>
    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".gifv", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
}