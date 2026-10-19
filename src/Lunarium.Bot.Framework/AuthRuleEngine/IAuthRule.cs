using Discord;
using Discord.WebSocket;

namespace Lunarium.Bot.Framework.AuthRuleEngine;

public interface IAuthRule
{
    // 返回 null 表示通过；返回具体的失败消息表示拦截
    ValueTask<ValidateResult> ValidateAsync(IInteractionContext context);
}

public interface IMsgBasedAuthRule
{
    // 返回 null 表示通过；返回具体的失败消息表示拦截
    ValueTask<ValidateResult> ValidateAsync(SocketMessage msg);
}