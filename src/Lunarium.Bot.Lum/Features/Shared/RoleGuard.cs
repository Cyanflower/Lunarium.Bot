using Discord.WebSocket;

namespace Lunarium.Bot.Lum.Features.Shared;

/// <summary>
/// 身份组可分配性校验（入群自动身份组与自助身份组共用）。
/// </summary>
public static class RoleGuard
{
    /// <summary>
    /// 判断机器人当前能否给成员分配该身份组；不能时返回原因。
    /// </summary>
    public static bool CanAssign(SocketGuild guild, SocketRole? role, out string reason)
    {
        reason = string.Empty;

        if (role is null)
        {
            reason = "身份组不存在";
            return false;
        }

        if (role.IsEveryone)
        {
            reason = "@everyone 不是可分配的身份组";
            return false;
        }

        if (role.IsManaged)
        {
            reason = "托管身份组（由集成创建）不可手动分配";
            return false;
        }

        var bot = guild.CurrentUser;
        if (bot is null)
        {
            reason = "无法获取机器人在服务器内的成员信息";
            return false;
        }

        if (!bot.GuildPermissions.ManageRoles)
        {
            reason = "机器人缺少管理身份组权限";
            return false;
        }

        // 机器人只能分配比自己最高身份组更低的位置
        if (role.Position >= bot.Hierarchy)
        {
            reason = "身份组层级不低于机器人，无法分配";
            return false;
        }

        return true;
    }
}
