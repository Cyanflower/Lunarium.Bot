using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lunarium.Bot.Framework.Configuration;

/// <summary>
/// 配置文件读写使用的 JSON 选项。
/// </summary>
public static class JsonConfigOptions
{
    /// <summary>
    /// 读取选项：属性名大小写不敏感；<b>未知键一律报错</b>——键名写错（如 <c>DashboardChannelID</c>）
    /// 默认会被静默忽略，症状是"配了却不生效"，极难排查，宁可启动即失败。
    /// </summary>
    public static JsonSerializerOptions Read { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>写入选项：缩进 + 不转义非 ASCII，便于人工 diff 与阅读。</summary>
    public static JsonSerializerOptions Write { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
