using Discord;
using Discord.WebSocket;

namespace Lunarium.Bot.Framework.AuthRuleEngine;

public static class RuleEngine
{
    public static async Task<MessageComponent?> ValidateAsync(IInteractionContext ctx, params IAuthRule[] rules)
    {
        foreach (var rule in rules)
        {
            var failReason = await rule.ValidateAsync(ctx);
            switch (failReason.ResultType)
            {
                case ResultType.Bypass:
                    return null;
                case ResultType.Success:
                    continue;
                case ResultType.Fail:
                    return failReason.MessageComponent;
            }
        }
        // 全部通过
        return null;
    }

    public static async Task<MessageComponent?> ValidateAsync(SocketMessage msg, params IMsgBasedAuthRule[] rules)
    {
        foreach (var rule in rules)
        {
            var failReason = await rule.ValidateAsync(msg);
            switch (failReason.ResultType)
            {
                case ResultType.Bypass:
                    return null;
                case ResultType.Success:
                    continue;
                case ResultType.Fail:
                    return failReason.MessageComponent;
            }
        }
        // 全部通过
        return null;
    }
}