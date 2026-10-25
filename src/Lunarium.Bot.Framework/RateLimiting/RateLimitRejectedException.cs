namespace Lunarium.Bot.Framework.RateLimiting;

public sealed class RateLimitRejectedException : Exception
{
    public RateLimitRejectedException(string message) : base(message) { }
}
