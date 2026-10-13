using Discord;

namespace Lunarium.Bot.ReleaseBot.InteractionsCache;

public interface IInteractionsCache
{
    public string CacheId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public void RefreshExpiration(TimeSpan duration);
    public MessageComponent Render();
}
