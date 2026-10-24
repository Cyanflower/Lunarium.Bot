namespace Lunarium.Bot.AI;

/// <summary>
///     运行期设置 API Key。
/// </summary>
/// <remarks>
///     默认实现只把 Key 保存在进程内存中：不落盘、不修改环境变量，
///     因此重启后会自动回落到环境变量 / <see cref="AiOptions.ApiKey" />。
///     传入 <see langword="null" /> 或空白表示清除运行期设置。
/// </remarks>
public interface IAiKeyConfigurator
{
    /// <summary>当前是否存在运行期设置的 Key。</summary>
    bool HasRuntimeKey { get; }

    /// <summary>设置运行期 Key；<paramref name="apiKey" /> 为 <see langword="null" /> 或空白时清除。</summary>
    void SetApiKey(string? apiKey);
}
