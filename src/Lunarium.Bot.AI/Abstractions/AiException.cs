namespace Lunarium.Bot.AI;

/// <summary>
///     AI 调用的异常基类。
/// </summary>
public abstract class AiException : Exception
{
    protected AiException(
        string message,
        int? statusCode = null,
        string? responseBody = null,
        bool isRetryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        IsRetryable = isRetryable;
    }

    /// <summary>HTTP 状态码；非 HTTP 失败时为 <c>null</c>。</summary>
    public int? StatusCode { get; }

    /// <summary>已截断到 500 字符的响应体，用于排查。</summary>
    public string? ResponseBody { get; }

    /// <summary>传输层是否适合重试。调用方不应据此自行重试。</summary>
    public bool IsRetryable { get; }
}

/// <summary>未配置 API Key，无法发起请求。调用方应先检查 <see cref="IAiClient.IsAvailable" />。</summary>
public sealed class AiNotConfiguredException(string message) : AiException(message);

/// <summary>API Key 无效（HTTP 401）。不应重试。</summary>
public sealed class AiAuthenticationException(string message, string? responseBody)
    : AiException(message, 401, responseBody);

/// <summary>账户余额不足（HTTP 402）。不应重试，应告警。</summary>
public sealed class AiInsufficientBalanceException(string message, string? responseBody)
    : AiException(message, 402, responseBody);

/// <summary>请求被限流（HTTP 429）。传输层已自动重试，抛出时表示重试次数已用尽。</summary>
public sealed class AiRateLimitedException(string message, string? responseBody)
    : AiException(message, 429, responseBody, isRetryable: true);

/// <summary>服务端错误或网络异常（HTTP 5xx / 超时）。</summary>
public sealed class AiTransientException : AiException
{
    public AiTransientException(string message, Exception? innerException = null)
        : base(message, isRetryable: true, innerException: innerException)
    {
    }

    public AiTransientException(string message, int statusCode, string? responseBody)
        : base(message, statusCode, responseBody, isRetryable: true)
    {
    }
}

/// <summary>请求本身不合法（HTTP 400 / 422）。这是代码 bug，不应重试。</summary>
public sealed class AiInvalidRequestException(string message, int statusCode, string? responseBody)
    : AiException(message, statusCode, responseBody);

/// <summary>输入超出模型上下文窗口。调用方应截断输入后再试。</summary>
public sealed class AiContextOverflowException(string message, string? responseBody)
    : AiException(message, 400, responseBody);

/// <summary>输入在本地就超过了 <see cref="AiOptions.MaxInputCharacters" />，未发起请求。</summary>
public sealed class AiInputTooLargeException(int length, int limit)
    : AiException($"输入长度 {length} 字符，超过上限 {limit} 字符。");

/// <summary>模型返回了 <c>status = failed</c>。</summary>
public sealed class AiRequestFailedException(string message, string? code, string? responseBody)
    : AiException(message, responseBody: responseBody)
{
    /// <summary>服务端错误码，如 <c>server_error</c>。</summary>
    public string? Code { get; } = code;
}

/// <summary>响应无法按预期解析（空输出、非法 JSON、结构化输出被截断等）。</summary>
public sealed class AiResponseFormatException(string message, string? responseBody = null)
    : AiException(message, responseBody: responseBody);
