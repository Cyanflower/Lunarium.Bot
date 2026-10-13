using Discord;

namespace Lunarium.Bot.VoteBot.InteractionsCache;

public abstract class BaseInteractionsCache<T> : IInteractionsCache where T : class
{
    public string CacheId { get; }
    public DateTimeOffset ExpiresAt { get; private set; } 
    protected readonly T _data;

    public BaseInteractionsCache(T data, DateTimeOffset expiresAt, string id)
    {
        // 确保数据不为 null
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        _data = data;
        ExpiresAt = expiresAt;
        CacheId = id;
    }

    #region 状态操作 (Commands) - 只修改状态
    public void RefreshExpiration(TimeSpan duration)
    {
        ExpiresAt = DateTimeOffset.UtcNow.Add(duration);
    }
    
    #endregion

    #region 渲染 (Query) - 根据当前状态生成视图
    public abstract MessageComponent Render();
    #endregion

}