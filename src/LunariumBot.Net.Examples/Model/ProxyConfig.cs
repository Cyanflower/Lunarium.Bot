namespace LunariumBot.Net.Examples.Model;

/// <summary>
/// 存储代理配置信息的数据记录。
/// 使用 record (默认为 record class) 获得引用类型语义和不可变性，非常适合用于依赖注入的配置数据。
/// </summary>
public record class ProxyConfig
{
    /// <summary>
    /// 是否启用代理。
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// 代理服务器的 URL 地址。
    /// </summary>
    public string? ProxyUrl { get; set; }
}