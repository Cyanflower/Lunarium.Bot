using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Framework.Configuration;

namespace Lunarium.Bot.Archive.AppInteractionService;

/// <summary>
/// 管理频道（<see cref="GuildConfig.StaffChannelId"/>）收窄。
/// <para>
/// 该频道仅管理组可见，把维护命令与卡片按钮全部限制在此频道，等于用「频道可见性」做第一道权限墙；
/// 未配置（0）时不限制，由角色规则兜底。
/// </para>
/// </summary>
internal static class StaffChannelGuard
{
    /// <summary>取本服务器的管理频道 Id；未配置返回 0。</summary>
    public static ulong GetChannelId(ConfigLoader<ArchiveConfig> configLoader, ulong guildId)
        => configLoader.Value.DeployGuildConfig.TryGetValue(guildId, out var config) && config.StaffChannelId != 0
            ? config.StaffChannelId
            : 0;

    /// <summary>构造频道收窄规则（供斜杠命令拼接）；未配置时返回空数组。</summary>
    public static IAuthRule[] RulesFor(ConfigLoader<ArchiveConfig> configLoader, ulong guildId)
    {
        var channelId = GetChannelId(configLoader, guildId);
        return channelId == 0
            ? []
            : [new BaseRuleSet.RequireInChannel(new HashSet<ulong> { channelId })];
    }

    /// <summary>组件/模态回调用：当前交互是否发生在管理频道内（未配置时放行）。</summary>
    public static bool IsInStaffChannel(ConfigLoader<ArchiveConfig> configLoader, ulong guildId, ulong channelId)
    {
        var staffChannelId = GetChannelId(configLoader, guildId);
        return staffChannelId == 0 || staffChannelId == channelId;
    }
}
