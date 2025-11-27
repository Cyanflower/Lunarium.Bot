using Discord;

namespace LunariumBot.Net.SearchBot.InteractionsCache;

public interface IInteractionsCache
{
    public string CacheId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public void RefreshExpiration(TimeSpan duration);
    public MessageComponent Render();
}
