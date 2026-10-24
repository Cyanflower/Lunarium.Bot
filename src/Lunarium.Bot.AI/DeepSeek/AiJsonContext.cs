using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunarium.Bot.AI.DeepSeek;

/// <summary>
///     DeepSeek Responses API 的线格式源生成上下文（AOT / 裁剪安全）。
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResponsesRequest))]
[JsonSerializable(typeof(ResponsesResponse))]
internal sealed partial class AiJsonContext : JsonSerializerContext
{
    /// <summary>
    ///     实际使用的上下文：中文不转义成 <c>\uXXXX</c>、忽略 null 字段。
    ///     <para>源生成无法在特性里指定 <c>Encoder</c>，只能通过选项实例构造，并全局复用这一个实例。</para>
    /// </summary>
    public static readonly AiJsonContext Relaxed = new(new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
