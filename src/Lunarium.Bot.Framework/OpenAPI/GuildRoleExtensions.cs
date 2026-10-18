using Discord;
using Discord.Rest;
using System.Collections.Immutable;

namespace Lunarium.Bot.Framework.OpenAPI;

public static class GuildRoleExtensions
{
    /// <summary>
    ///     通过 REST 直取 <c>guilds/{guildId}/roles</c>（payload 只含角色，比 GET guilds/{guildId} 更轻），
    ///     并复用库内部 <see cref="RestRole.Create"/> 将 API 模型转换为公开实体。
    /// </summary>
    /// <remarks>
    ///     依赖本仓库 Discord.Net 的 <c>InternalsVisibleTo</c> 配置（见 Discord.Net/Directory.Build.props）
    ///     访问 <see cref="BaseDiscordClient.ApiClient"/> 与 <see cref="RestRole.Create"/>。
    /// </remarks>
    public static async Task<IReadOnlyCollection<RestRole>> GetGuildRolesAsync(
        this BaseDiscordClient client,
        IGuild guild,
        RequestOptions? options = null)
    {
        var models = await client.ApiClient.GetGuildRolesAsync(guild.Id, options).ConfigureAwait(false);
        return models
            .Select(model => RestRole.Create(client, guild, model))
            .ToImmutableArray();
    }
}
