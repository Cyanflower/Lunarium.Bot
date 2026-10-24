using System.Text.Json;

namespace Lunarium.Bot.AI;

/// <summary>模型停止生成的原因。</summary>
public enum AiFinishReason
{
    /// <summary>正常结束。</summary>
    Completed,

    /// <summary>触达 <c>max_output_tokens</c>，输出被截断。</summary>
    MaxOutputTokens,

    /// <summary>被内容过滤拦截。</summary>
    ContentFilter,

    /// <summary>其它（未知状态）。</summary>
    Other,
}

/// <summary>一次调用的 token 用量，取自服务端 <c>usage</c>。</summary>
public sealed record AiUsage
{
    /// <summary>输入 token 数（含命中缓存的部分）。</summary>
    public required int InputTokens { get; init; }

    /// <summary>命中 KV 缓存前缀的输入 token 数。正常情况下应接近 <see cref="InputTokens" />。</summary>
    public int CachedInputTokens { get; init; }

    /// <summary>可见输出 token 数。</summary>
    public required int OutputTokens { get; init; }

    /// <summary>推理（思维链）token 数。关闭思考模式时应为 0。</summary>
    public int ReasoningTokens { get; init; }

    /// <summary>输入 + 输出。</summary>
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>一次推理调用的结果。</summary>
public sealed record AiCompletion
{
    /// <summary>模型的文本输出。结构化模式下即 JSON 原文。</summary>
    public required string Text { get; init; }

    /// <summary>结构化模式（<see cref="AiOutputFormatKind.JsonSchema" />）下已解析的 JSON；否则为 <c>null</c>。</summary>
    public JsonElement? Structured { get; init; }

    /// <summary>token 用量。</summary>
    public required AiUsage Usage { get; init; }

    /// <summary>实际使用的模型。</summary>
    public required string Model { get; init; }

    /// <summary>服务端响应 id，便于与账单/日志对齐。</summary>
    public string? ResponseId { get; init; }

    /// <summary>停止原因。</summary>
    public AiFinishReason FinishReason { get; init; } = AiFinishReason.Completed;

    /// <summary>本次调用（含重试）耗时。</summary>
    public TimeSpan Duration { get; init; }
}
