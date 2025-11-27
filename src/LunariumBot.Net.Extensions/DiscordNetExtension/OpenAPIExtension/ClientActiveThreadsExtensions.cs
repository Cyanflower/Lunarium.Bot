using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Collections.Immutable;

namespace LunariumBot.Net.Extensions.DiscordNetExtension;

public static class ClientAllActiveThreadsExtensions
{
    public static async Task<IReadOnlyCollection<RestThreadChannel>> GetAllActiveThreadsAsync(this DiscordRestClient client, IGuild guild, RequestOptions? options = null)
    {
        var result = await client.ApiClient.GetActiveThreadsAsync(guild.Id, options).ConfigureAwait(false);
        return result.Threads.Select(x => RestThreadChannel.Create(client, guild, x)).ToImmutableArray();
    }

    public static async Task<IReadOnlyCollection<RestThreadChannel>> GetAllActiveThreadsAsync(this DiscordSocketClient client, IGuild guild, RequestOptions? options = null)
    {
        var result = await client.ApiClient.GetActiveThreadsAsync(guild.Id, options).ConfigureAwait(false);
        return result.Threads.Select(x => RestThreadChannel.Create(client.Rest, guild, x)).ToImmutableArray();
    }
}