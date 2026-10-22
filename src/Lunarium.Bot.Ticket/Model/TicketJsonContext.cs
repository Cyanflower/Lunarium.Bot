using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunarium.Bot.Ticket.Model;

/// <summary>
/// <see cref="TicketData"/> 的源生成序列化上下文（AOT / 裁剪安全）。
/// </summary>
/// <remarks>
/// <see cref="JsonSourceGenerationOptionsAttribute"/> 没有 <c>Encoder</c> 参数，
/// 因此上下文由显式的 <see cref="JsonSerializerOptions"/> 实例构造并复用；
/// 不要改用 <c>JsonSerializer.Serialize(value, options)</c> 的反射重载（IL2026/IL3050）。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(TicketData))]
internal partial class TicketJsonContext : JsonSerializerContext
{
}

/// <summary>工单本地 JSON 的序列化设置。</summary>
internal static class TicketJson
{
    /// <summary>缩进 + 中文不转义，便于运维直接打开文件排查。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly TicketJsonContext Context = new(Options);
}
