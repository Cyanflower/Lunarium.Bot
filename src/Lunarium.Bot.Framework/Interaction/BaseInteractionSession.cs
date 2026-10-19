using Discord;

namespace Lunarium.Bot.Framework.Interaction;

/// <summary>
/// 交互会话可选基类，封装了 SessionId、ExpiresAt 与延期逻辑的样板代码。
/// </summary>
/// <typeparam name="TContext">关联的上下文或业务数据类型。</typeparam>
public abstract class BaseInteractionsSession<TContext> : IInteractionSession
{
    public string SessionId { get; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// 会话上下文/数据
    /// </summary>
    public TContext Context { get; }

    protected BaseInteractionsSession(string sessionId, DateTimeOffset expiresAt, TContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(context);

        SessionId = sessionId;
        ExpiresAt = expiresAt;
        Context = context;
    }

    protected abstract string Scope { get; }

    protected string CustomId(string action) => Interaction.CustomId.CreateIdString(Scope, action, SessionId);

    public void RefreshExpiration(TimeSpan duration)
    {
        ExpiresAt = DateTimeOffset.UtcNow.Add(duration);
    }

    public abstract MessageComponent Render();
}