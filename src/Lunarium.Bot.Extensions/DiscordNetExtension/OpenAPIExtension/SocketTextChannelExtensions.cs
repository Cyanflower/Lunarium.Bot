using Discord;
using Discord.Rest;
using Discord.WebSocket;
using System.Collections.Immutable;

namespace Lunarium.Bot.Extensions.DiscordNetExtension;

public static class SocketTextChannelExtensions
{
    public static Task<IReadOnlyCollection<RestThreadChannel>> GetPublicArchivedThreadsAsync(
        this SocketTextChannel channel,
        int? limit = null,
        DateTimeOffset? before = null,
        RequestOptions? options = null)
    {
        return ThreadHelper.GetPublicArchivedThreadsAsync(
            channel, 
            channel.Discord,
            limit, 
            before, 
            options);
    }

    public static Task<IReadOnlyCollection<RestThreadChannel>> GetPrivateArchivedThreadsAsync(
        this SocketTextChannel channel,
        int? limit = null,
        DateTimeOffset? before = null,
        RequestOptions? options = null)
    {
        return ThreadHelper.GetPrivateArchivedThreadsAsync(channel, channel.Discord, limit, before, options);
    }

    public static Task<IReadOnlyCollection<RestThreadChannel>> GetJoinedPrivateArchivedThreadsAsync(
        this SocketTextChannel channel,
        int? limit = null,
        DateTimeOffset? before = null,
        RequestOptions? options = null)
    {
        return ThreadHelper.GetJoinedPrivateArchivedThreadsAsync(channel, channel.Discord, limit, before, options);
    }
}
