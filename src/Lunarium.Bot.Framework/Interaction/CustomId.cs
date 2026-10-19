namespace Lunarium.Bot.Framework.Interaction;

/// <summary>
///     交互 customId 的三段式结构：<c>scope:action:sessionId</c>。
/// </summary>
public record CustomId
{
    public const int MaxLength = 100;

    public string Scope { get; }
    public string Action { get; }
    public string SessionId { get; }

    public CustomId(string scope, string action, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (scope.Contains(':'))
            throw new ArgumentException("scope 不能含 ':'", nameof(scope));
        if (action.Contains(':'))
            throw new ArgumentException("action 不能含 ':'", nameof(action));
        if (sessionId.Contains(':'))
            throw new ArgumentException("sessionId 不能含 ':'", nameof(sessionId));

        // scope:action:sessionId —— 两处分隔符
        var length = scope.Length + action.Length + sessionId.Length + 2;
        if (length > MaxLength)
            throw new ArgumentException($"customId 超长: {length} > {MaxLength}");

        Scope = scope;
        Action = action;
        SessionId = sessionId;
    }

    public static string CreateIdString(string scope, string action, string sessionId)
        => new CustomId(scope, action, sessionId).ToString();

    public override string ToString() => $"{Scope}:{Action}:{SessionId}";
}