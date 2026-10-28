using Discord;
using Lunarium.Bot.Framework.Interaction;

namespace Lunarium.Bot.Lum.Features.Shared;

/// <summary>
/// 会话查找：校验会话 ID 前缀（创建者）并把会话从 <see cref="SessionManager"/> 取出。
/// </summary>
public static class SessionLookup
{
    public static bool TryGet<T>(
        SessionManager sessionManager,
        IInteractionContext context,
        string sessionId,
        out T session,
        out string error)
        where T : class, IInteractionSession
    {
        session = null!;
        error = string.Empty;

        var parsed = HasPrefixSessionId.TryParse(sessionId);
        if (parsed is null || !ulong.TryParse(parsed.Prefix, out var userId))
        {
            error = "未能解析 ID 前缀";
            return false;
        }

        if (userId != context.User.Id)
        {
            error = "非交互创建者不可交互";
            return false;
        }

        // 注册侧走"含前缀"的重载，字典里的键是完整 key（"ulong|nanoId"），这里也必须用完整串查
        if (!sessionManager.TryGet<T>(sessionId, out var found) || found is null)
        {
            error = "交互已过期";
            return false;
        }

        session = found;
        return true;
    }
}
