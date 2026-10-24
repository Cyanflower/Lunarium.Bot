using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunarium.Bot.AI;

/// <summary>思考模式的力度。</summary>
public enum AiReasoningEffort
{
    /// <summary>关闭思考。<b>默认值</b>：单次判定类任务不需要思维链，关闭后更快、更便宜，且 <c>temperature</c> 生效。</summary>
    None,

    /// <summary>轻度思考。</summary>
    Low,

    /// <summary>高强度思考。</summary>
    High,

    /// <summary>最大力度思考。</summary>
    Max,
}

/// <summary>输出格式类型。</summary>
public enum AiOutputFormatKind
{
    /// <summary>纯文本。</summary>
    Text,

    /// <summary>按 JSON Schema 约束的结构化输出。</summary>
    JsonSchema,
}

/// <summary>期望的输出格式。</summary>
public sealed record AiOutputFormat
{
    private AiOutputFormat()
    {
    }

    /// <summary>纯文本输出。</summary>
    public static AiOutputFormat Text { get; } = new() { Kind = AiOutputFormatKind.Text };

    /// <summary>输出格式类型。</summary>
    public AiOutputFormatKind Kind { get; private init; }

    /// <summary>JSON Schema 的名称，仅 <see cref="AiOutputFormatKind.JsonSchema" /> 时有值。</summary>
    public string? SchemaName { get; private init; }

    /// <summary>JSON Schema 本体，仅 <see cref="AiOutputFormatKind.JsonSchema" /> 时有值。</summary>
    public JsonElement? Schema { get; private init; }

    /// <summary>
    ///     要求模型按指定 JSON Schema 输出。
    /// </summary>
    /// <param name="name">Schema 名称，只能包含字母、数字、下划线与连字符，最长 64 字符。</param>
    /// <param name="schema">JSON Schema 本体。</param>
    public static AiOutputFormat JsonSchema(string name, JsonNode schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(schema);
        ValidateSchemaName(name);

        return new AiOutputFormat
        {
            Kind = AiOutputFormatKind.JsonSchema,
            SchemaName = name,
            Schema = JsonSerializer.SerializeToElement(schema),
        };
    }

    private static void ValidateSchemaName(string name)
    {
        if (name.Length > 64)
        {
            throw new ArgumentException("JSON Schema 名称最长 64 个字符。", nameof(name));
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
            {
                throw new ArgumentException(
                    "JSON Schema 名称只能包含字母、数字、下划线与连字符。", nameof(name));
            }
        }
    }
}

/// <summary>
///     一次单次推理请求。无状态：每次调用互相独立。
/// </summary>
public sealed record AiCompletionRequest
{
    /// <summary>用户输入（单条 user 消息）。</summary>
    public required string Input { get; init; }

    /// <summary>
    ///     系统指令，走请求顶层 <c>instructions</c> 字段。
    ///     <para>
    ///         请保持内容<b>恒定</b>：DeepSeek 的上下文缓存按前缀匹配，指令固定时几乎每次都能命中缓存（价格相差约 50 倍）。
    ///         不要把每次变化的业务数据插值进来 —— 那些内容应放进 <see cref="Input" />。
    ///     </para>
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>模型 id。为 <c>null</c> 时使用 <see cref="AiOptions.DefaultModel" />。</summary>
    public string? Model { get; init; }

    /// <summary>思考力度，默认关闭。</summary>
    public AiReasoningEffort Effort { get; init; } = AiReasoningEffort.None;

    /// <summary>输出格式，默认纯文本。</summary>
    public AiOutputFormat Output { get; init; } = AiOutputFormat.Text;

    /// <summary>输出 token 上限。为 <c>null</c> 时由服务端决定。</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>采样温度，取值 0~2。为 <c>null</c> 时由服务端决定（默认 1）。思考模式下该参数无效。</summary>
    public double? Temperature { get; init; }
}
