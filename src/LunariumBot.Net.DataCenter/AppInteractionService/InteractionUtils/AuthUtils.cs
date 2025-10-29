// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Utils;

namespace LunariumBot.Net.DataCenter.AppInteractionService;

public class AuthUtils : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ConfigLoader _configLoader;
    private readonly ILogger _logger;
    public AuthUtils(ILogger logger, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("CommandAuth");
        _configLoader = configLoader;
    }

    public bool IsAdmin(IEnumerable<ulong> userRoleIds)
    {
        return userRoleIds.Any(_configLoader.Config.AuthConfig.AdminRoleIds.Contains);
    }

    public bool InGuild(ulong guildId)
    {
        return _configLoader.Config.DeployConfig.DeployGuildIds.Contains(guildId);
    }

    public async Task<bool> IsAdmin(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        try
        {
            if (!InGuild(context.Guild.Id))
            {
                await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
                _logger.Warning("鉴权失败: 用户 {context.User.Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
                return false;
            }
            // --- 是否具有任一管理员身份 ---
            // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
            var guildUser = context.User as IGuildUser;
            if (guildUser is not null && IsAdmin(guildUser.RoleIds))
            {
                _logger.Info("鉴权通过: 用户 {context.User.Username} 执行了[/{command}]命令", context.User.Username, command);
                return true;
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能由管理员使用"), ephemeral: true);
                _logger.Warning("鉴权失败: 非管理员角色 用户 {context.User.Username} 执行了[/{command}]命令", context.User.Username, command);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "鉴权过程中发生错误");
            return false;
        }
    }
}