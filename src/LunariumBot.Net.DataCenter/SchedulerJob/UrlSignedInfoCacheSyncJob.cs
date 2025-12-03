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

namespace LunariumBot.Net.DataCenter.Scheduler;

public class UrlSignedInfoCacheSyncAsyncJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly UrlSignedInfoCacheService _urlSignedInfoCacheService;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _client;
    private IChannel? _cacheLogChannel;
    private readonly AsyncRetryPolicy _dbRetryPolicy;

    public UrlSignedInfoCacheSyncAsyncJob(
        ILogger logger,
        ConfigLoader configLoder,
        DatabaseService databaseService,
        UrlSignedInfoCacheService urlSignedInfoCacheService,
        DiscordSocketClient client,
        [FromKeyedServices("DbPolicy")] AsyncRetryPolicy dbRetryPolicy
    )
    {
        _logger = logger.ForContext("UrlSignedInfoCacheSyncJob");
        _configLoader = configLoder;
        _databaseService = databaseService;
        _urlSignedInfoCacheService = urlSignedInfoCacheService;
        _client = client;
        _dbRetryPolicy = dbRetryPolicy;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            _logger.Info("URL同步开始");
            // 执行同步查询
            var queryResult = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                var result = await _databaseService.GetAllUrlInfoAsync();
                return result;
            });
            // 写入缓存
            _urlSignedInfoCacheService.DailySync(queryResult);

            _logger.Info("URL同步完成");

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
            _logger.Error("UrlSignedInfoCacheSyncJob 执行时发生未处理的异常:", ex);
        }
    }
}