using Discord;
using Discord.Rest;
using Discord.API;
using Discord.API.Rest;
using System.Collections.Immutable;

namespace Discord.API;
public static class ApiClientExtensions
{
    internal static Task<RoleV2> ModifyGuildRoleV2Async(this DiscordRestApiClient client, ulong guildId, ulong roleId, Rest.ModifyGuildRoleParamsV2 args, RequestOptions? options = null)
    {
        Preconditions.NotEqual(guildId, 0, nameof(guildId));
        Preconditions.NotEqual(roleId, 0, nameof(roleId));
        Preconditions.NotNull(args, nameof(args));
        // Preconditions.AtLeast(args.Color, 0, nameof(args.Color));
        Preconditions.NotNullOrEmpty(args.Name, nameof(args.Name));
        options = RequestOptions.CreateOrClone(options);

        var ids = new DiscordRestApiClient.BucketIds(guildId: guildId);
        return client.SendJsonAsync<RoleV2>("PATCH", () => $"guilds/{guildId}/roles/{roleId}", args, ids, options: options);
    }

    internal static Task<IReadOnlyCollection<RoleV2>> GetGuildRolesV2Async(this DiscordRestApiClient client, ulong guildId, RequestOptions? options = null)
    {
        Preconditions.NotEqual(guildId, 0, nameof(guildId));
        options = RequestOptions.CreateOrClone(options);

        var ids = new DiscordRestApiClient.BucketIds(guildId: guildId);
        return client.SendAsync<IReadOnlyCollection<RoleV2>>("GET", () => $"guilds/{guildId}/roles", ids, options: options);
    }
}
