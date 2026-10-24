namespace Lunarium.Bot.AI;

/// <summary>
///     <see cref="IAiClient" /> 的配置。
/// </summary>
public sealed class AiOptions
{
    /// <summary>API 根地址，默认 DeepSeek 官方端点。可指向自建反向代理。</summary>
    public Uri BaseUrl { get; set; } = new("https://api.deepseek.com");

    /// <summary>直接指定 API Key。留空时从 <see cref="ApiKeyEnvironmentVariable" /> 读取（推荐）。</summary>
    public string? ApiKey { get; set; }

    /// <summary>存放 API Key 的环境变量名。</summary>
    public string ApiKeyEnvironmentVariable { get; set; } = "DEEPSEEK_API_KEY";

    /// <summary>请求未指定模型时使用的模型。</summary>
    public string DefaultModel { get; set; } = "deepseek-flash";

    /// <summary>单次尝试的超时时间。</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>输入文本的字符数上限；超过时本地直接拒绝，避免无效计费。</summary>
    public int MaxInputCharacters { get; set; } = 200_000;

    /// <summary>传输层最大重试次数（仅针对 429 / 5xx / 超时 / 网络异常）。</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>重试的基础退避时长；实际按指数递增并附加随机抖动。</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
}
