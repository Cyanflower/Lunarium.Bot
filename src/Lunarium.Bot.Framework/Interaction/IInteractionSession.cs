using Discord;

namespace Lunarium.Bot.Framework.Interaction;

public interface IInteractionSession
{
    public string SessionId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public void RefreshExpiration(TimeSpan duration);
    public MessageComponent Render();
}

public interface ISessionContext<out TSession> where TSession : IInteractionSession
{
    TSession CreateSession(string sessionId, DateTimeOffset expiresAt);
}

/*
实际 Context 示例
public record OrderSessionContext(OrderData Data, ILogger Logger) : ISessionContext<OrderSession>
{
    public OrderSession CreateSession(string cacheId, DateTimeOffset expiresAt)
    {
        return new OrderSession(this, expiresAt, cacheId);
    }
}

调用：
var context = new OrderSessionContext(order, _logger);

// 调用：根据 context 自动推导出返回的是 OrderSession
OrderSession session = sessionManager.Register(context);
*/