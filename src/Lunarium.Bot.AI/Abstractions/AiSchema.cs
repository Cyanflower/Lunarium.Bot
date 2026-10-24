using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Lunarium.Bot.AI;

/// <summary>
///     由 <see cref="JsonTypeInfo{T}" /> 导出 JSON Schema。
/// </summary>
internal static class AiSchema
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        // 未标注可空性的引用类型按不可空处理，避免 schema 出现无意义的 nullable。
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = static (_, node) =>
        {
            // 对象节点补 additionalProperties: false，约束模型不要编造字段。
            // 仅处理「有 properties 且未显式声明」的对象，字典类型不受影响。
            if (node is JsonObject obj
                && obj["type"] is JsonValue typeValue
                && typeValue.TryGetValue<string>(out var type)
                && type == "object"
                && obj.ContainsKey("properties")
                && !obj.ContainsKey("additionalProperties"))
            {
                obj["additionalProperties"] = false;
            }

            return node;
        },
    };

    /// <summary>按类型信息导出 JSON Schema。</summary>
    public static JsonNode Export<T>(JsonTypeInfo<T> typeInfo) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(typeInfo.Options, typeInfo.Type, ExporterOptions);

    /// <summary>把类型名转换成符合 <c>^[a-zA-Z0-9_-]{1,64}$</c> 的 Schema 名称。</summary>
    public static string NameFor(Type type)
    {
        var chars = type.Name
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray();

        var name = new string(chars);
        if (name.Length == 0)
        {
            return "response";
        }

        return name.Length <= 64 ? name : name[..64];
    }
}
