using Microsoft.Extensions.Options;

namespace Lunarium.Bot.AI.DependencyInjection;

/// <summary>
///     默认的 Key 解析链：显式配置 → 运行期设置 → 环境变量。
/// </summary>
/// <remarks>
///     运行期值只存在内存里（<c>volatile</c> 引用赋值是原子的），
///     因此读取无需加锁，也不会产生任何磁盘或环境变量副作用。
/// </remarks>
internal sealed class InMemoryAiKeyProvider : IAiKeyProvider, IAiKeyConfigurator
{
    private readonly IOptionsMonitor<AiOptions> _options;

    private volatile string? _runtimeKey;

    public InMemoryAiKeyProvider(IOptionsMonitor<AiOptions> options)
    {
        _options = options;
    }

    public bool HasRuntimeKey => _runtimeKey is not null;

    public void SetApiKey(string? apiKey)
    {
        _runtimeKey = Normalize(apiKey);
    }

    public string? GetApiKey()
    {
        var options = _options.CurrentValue;

        if (Normalize(options.ApiKey) is { } configured)
        {
            return configured;
        }

        if (_runtimeKey is { } runtime)
        {
            return runtime;
        }

        var variable = options.ApiKeyEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(variable))
        {
            return null;
        }

        return Normalize(Environment.GetEnvironmentVariable(variable));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
