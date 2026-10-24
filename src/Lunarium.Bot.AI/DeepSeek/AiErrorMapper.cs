using System.Text.Json;

namespace Lunarium.Bot.AI.DeepSeek;

/// <summary>
///     把 HTTP 状态码与响应体映射为 <see cref="AiException" />。
/// </summary>
internal static class AiErrorMapper
{
    private static readonly string[] ContextOverflowMarkers =
    [
        "context length",
        "context window",
        "maximum context",
        "too many tokens",
        "too long",
    ];

    private const int BodySnippetLength = 500;

    public static AiException Map(int statusCode, string? body)
    {
        var message = ExtractMessage(body) ?? $"AI 请求失败（HTTP {statusCode}）。";
        var snippet = Snippet(body);

        return statusCode switch
        {
            400 when IsContextOverflow(body) => new AiContextOverflowException(message, snippet),
            400 or 422 => new AiInvalidRequestException(message, statusCode, snippet),
            401 => new AiAuthenticationException(message, snippet),
            402 => new AiInsufficientBalanceException(message, snippet),
            429 => new AiRateLimitedException(message, snippet),
            >= 500 => new AiTransientException(message, statusCode, snippet),
            _ => new AiInvalidRequestException(message, statusCode, snippet),
        };
    }

    private static bool IsContextOverflow(string? body) =>
        body is not null
        && ContextOverflowMarkers.Any(marker => body.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // 响应体不是 JSON，忽略。
        }

        return null;
    }

    internal static string? Snippet(string? body) =>
        string.IsNullOrEmpty(body)
            ? null
            : body.Length <= BodySnippetLength
                ? body
                : body[..BodySnippetLength];
}
