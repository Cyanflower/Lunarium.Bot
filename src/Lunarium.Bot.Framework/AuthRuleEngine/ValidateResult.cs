
using Discord;

namespace Lunarium.Bot.Framework.AuthRuleEngine;

public enum ResultType
{
    Fail,
    Success,
    Bypass
}

public record struct ValidateResult(ResultType ResultType, MessageComponent? MessageComponent);