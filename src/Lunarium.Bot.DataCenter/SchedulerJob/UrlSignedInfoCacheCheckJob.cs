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
using Discord.Rest;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Scheduler.Interfaces;
using Lunarium.Bot.Framework;

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

            List<ulong> needGetChannel = new();

            foreach (var info in checkResult)
            {
                if (!_channelCache.ContainsKey(info.ChannelId)) needGetChannel.Add(info.ChannelId);
            }

            var getChannelTask = needGetChannel.Select(id => Task.Run(async () =>
            {
                try
                {
                    using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);

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
            ConcurrentBag<(ulong threadId, Jsonb<List<string>> urls)> toDbData = new();

            var getResultTask = checkResult.Select(info => Task.Run(async () =>
            {
                if (_channelCache.TryGetValue(info.ChannelId, out var channel))
                {
                    try
                    {
                        using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);

                        var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            return await channel.GetMessageAsync(info.MessageId, options: _requestOptions);
                        });

                        if (message is null) 
                        {
                            getResult.Add((info, null));
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
                            // 外链图片以embed形式存在
                            imageAttachment.AddRange(socketMessage.Embeds
                                .Where(embed => embed.Thumbnail is not null && embed.Type == EmbedType.Image || embed.Type == EmbedType.Gifv)
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
                            // 外链图片以embed形式存在
                            imageAttachment.AddRange(restMessage.Embeds
                                .Where(embed => embed.Thumbnail is not null && embed.Type == EmbedType.Image || embed.Type == EmbedType.Gifv)
                                .Select(embed => embed.Thumbnail!.Value.Url)
                                .ToList()
                            );
                        }

                        if (imageAttachment.Count > 0 && !string.IsNullOrWhiteSpace(imageAttachment[0]))
                        {
                            // 无论如何都回库
                            toDbData.Add((info.ChannelId, new Jsonb<List<string>>(imageAttachment)));

                            // 只在包含 dc cdn 链接的情况下回缓存池
                            var newUrl = imageAttachment.FirstOrDefault(url => UrlSignedInfo.IsDiscordCdnUrl(url), defaultValue: null);
                            if (newUrl is not null)
                            {
                                var newInfo = new UrlSignedInfo(newUrl, channel.Id, message.Id);
                                getResult.Add((info, newInfo));
                            }
                            else
                            {
                                // 非 dc cdn 的外链, 移除缓存
                                getResult.Add((info, null));
                            }
                        }
                        else
                        {
                            getResult.Add((info, null));
                            toDbData.Add((info.ChannelId, new Jsonb<List<string>>(null)));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex);
                        getResult.Add((info, null));
                        toDbData.Add((info.ChannelId, new Jsonb<List<string>>(null)));
                        _logger.Error(ex, "❌ 处理频道消息时发生异常，无法提取图片链接 (ChannelId: {ChannelId}, MessageId: {MessageId})", info.ChannelId, info.MessageId);
                    }
                }
                else
                {
                    getResult.Add((info, null));
                    toDbData.Add((info.ChannelId, new Jsonb<List<string>>(null)));
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
            
            var embed = EmbedUtils.SuccessEmbed(_urlSignedInfoCacheService.GetCacheStatistics(), "定时同步缓存更新成功");

            // 判断是否有缓存或者已有缓存是否和配置相同(检查配置更新)
            if (_cacheLogChannel is not IMessageChannel || _cacheLogChannel.Id != _configLoader.Config.AuthConfig.LogThread)
            {
                _cacheLogChannel = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogThread);
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
            _logger.Error(ex, "UrlSignedInfoCacheCheckJob 执行时发生未处理的异常:");
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