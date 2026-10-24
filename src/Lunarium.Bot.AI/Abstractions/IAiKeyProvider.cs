namespace Lunarium.Bot.AI;

/// <summary>
///     解析当前可用的 API Key。
/// </summary>
/// <remarks>
///     每次调用都会重新解析，因此实现必须便宜且线程安全；
///     调用方（<see cref="IAiClient" />）在每次请求开始时读取一次并固定使用该值。
/// </remarks>
public interface IAiKeyProvider
{
    /// <summary>返回当前可用的 Key；未配置时返回 <see langword="null" />。</summary>
    string? GetApiKey();
}
