using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Collections.Immutable;

namespace Lunarium.Bot.Framework.OpenAPI;

public static class ClientAllActiveThreadsExtensions
{
    public static async Task<IReadOnlyCollection<RestThreadChannel>> GetAllActiveThreadsAsync(this DiscordRestClient client, IGuild guild, RequestOptions? options = null)
    {
        var result = await client.ApiClient.GetActiveThreadsAsync(guild.Id, options);
        return result.Threads.Select(x => RestThreadChannel.Create(client, guild, x)).ToImmutableArray();
    }

    public static async Task<IReadOnlyCollection<RestThreadChannel>> GetAllActiveThreadsAsync(this DiscordSocketClient client, IGuild guild, RequestOptions? options = null)
    {
        var result = await client.ApiClient.GetActiveThreadsAsync(guild.Id, options);
        return result.Threads.Select(x => RestThreadChannel.Create(client.Rest, guild, x)).ToImmutableArray();
    }

    public static async Task<IReadOnlyList<ActiveThreadSnapshot>> GetActiveThreadSnapshotsAsync(
        this DiscordSocketClient client, IGuild guild, RequestOptions? options = null)
    {
        var result = await client.ApiClient.GetActiveThreadsAsync(guild.Id, options);
        return [.. result.Threads.Select(x => new ActiveThreadSnapshot(
            RestThreadChannel.Create(client.Rest, guild, x),
            x.LastMessageId,
            x.LastMessageId is { } id ? SnowflakeUtils.FromSnowflake(id) : null))];
    }
}

public readonly record struct ActiveThreadSnapshot(
    RestThreadChannel Thread,
    ulong? LastMessageId,
    DateTimeOffset? LastMessageAt);
