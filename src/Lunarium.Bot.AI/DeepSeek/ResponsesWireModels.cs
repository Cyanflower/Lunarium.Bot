using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunarium.Bot.AI.DeepSeek;

// ─────────────────────────── 请求 ───────────────────────────

internal sealed class ResponsesRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("input")]
    public required List<InputItem> Input { get; init; }

    [JsonPropertyName("reasoning")]
    public required ReasoningOptions Reasoning { get; init; }

    [JsonPropertyName("text")]
    public required TextOptions Text { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }
}

internal sealed class InputItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public required List<InputContent> Content { get; init; }
}

internal sealed class InputContent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "input_text";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

internal sealed class ReasoningOptions
{
    [JsonPropertyName("effort")]
    public required string Effort { get; init; }
}

internal sealed class TextOptions
{
    [JsonPropertyName("format")]
    public required TextFormat Format { get; init; }
}

internal sealed class TextFormat
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("schema")]
    public JsonElement? Schema { get; init; }
}

// ─────────────────────────── 响应 ───────────────────────────

internal sealed class ResponsesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("error")]
    public ResponseError? Error { get; init; }

    [JsonPropertyName("incomplete_details")]
    public IncompleteDetails? IncompleteDetails { get; init; }

    [JsonPropertyName("output")]
    public List<OutputItem>? Output { get; init; }

    [JsonPropertyName("usage")]
    public ResponseUsage? Usage { get; init; }
}

internal sealed class ResponseError
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

internal sealed class IncompleteDetails
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

internal sealed class OutputItem
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("content")]
    public List<OutputContent>? Content { get; init; }
}

internal sealed class OutputContent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class ResponseUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }

    [JsonPropertyName("input_tokens_details")]
    public InputTokensDetails? InputTokensDetails { get; init; }

    [JsonPropertyName("output_tokens_details")]
    public OutputTokensDetails? OutputTokensDetails { get; init; }
}

internal sealed class InputTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; init; }
}

internal sealed class OutputTokensDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; init; }
}
