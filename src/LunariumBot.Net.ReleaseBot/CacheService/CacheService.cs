using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Polly;
using Polly.Retry;


using LunariumBot.Net.Logger;

using LunariumBot.Net.ReleaseBot.Models;

namespace LunariumBot.Net.ReleaseBot.CacheServices;

public class CacheService
{
    private readonly DiscordRestClient _client;
    private readonly ILogger _logger;
    private ConcurrentDictionary<ulong, RestGuild> _guildCache = new();
    private ConcurrentDictionary<ulong, RestGuildChannel> _threadCache = new();
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    public CacheService(ILogger logger,DiscordSocketClient socketClient, [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy)
    {
        _logger = logger.ForContext("CacheService");
        _client = socketClient.Rest;
        _httpRetryPolicy = httpRetryPolicy;
    }

    public async Task<IMessage?> GetMessageAsync(WorkData data)
    {
        try
        {
            var message = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                if (!_guildCache.TryGetValue(data.CacheGuildId, out var guild))
                {
                    guild = await _client.GetGuildAsync(data.CacheGuildId);
                    _guildCache.TryAdd(guild.Id, guild);
                }
                if (guild == null) return null;

                if (!_threadCache.TryGetValue(data.CacheThreadId, out var thread))
                {
                    thread = await guild.GetChannelAsync(data.CacheThreadId);
                    if (_threadCache.Count > 10000)
                    {
                        _threadCache.Clear();
                    }
                    _threadCache.TryAdd(thread.Id, thread);
                }
                if (thread == null) return null;

                if (thread is not IThreadChannel restThread) return null;

                return await restThread.GetMessageAsync(data.CacheMessageId);
            });
            return message;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetMessageAsync Exception");
            throw;
        }
    }
}