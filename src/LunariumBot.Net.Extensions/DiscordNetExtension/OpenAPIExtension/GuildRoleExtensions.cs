using Discord;
using Discord.API;
using Discord.Rest;
using System.Collections.Immutable;

namespace LunariumBot.Net.Extensions.DiscordNetExtension;
public static class GuildRoleExtensions
{
    public static async Task<IReadOnlyCollection<RestRoleV2>> GetGuildRolesAsync(
        this BaseDiscordClient client, 
        IGuild guild, 
        RequestOptions? options = null)
    {
        // 通过 ApiClient (internal) 获取原始数据 (API.Role 列表)
        var models = await client.ApiClient.GetGuildRolesV2Async(guild.Id, options).ConfigureAwait(false);

        // 通过 RestRole.Create (internal) 将 DTO 转换为实体
        return models
            .Select(model => RestRoleV2.Create(client, guild, model))
            .ToImmutableArray();
    }
}
