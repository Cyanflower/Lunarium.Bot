using Lunarium.Bot.AI.DeepSeek;
using Lunarium.Bot.AI.DependencyInjection;
using Lunarium.Common.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lunarium.Bot.AI;

/// <summary>
///     <see cref="IAiClient" /> 的注册入口。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>内部使用的命名 HttpClient。</summary>
    public const string HttpClientName = "Lunarium.AI";

    /// <summary>
    ///     注册无状态单次推理客户端（直连）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">配置回调。</param>
    public static IServiceCollection AddLunariumAi(
        this IServiceCollection services, Action<AiOptions>? configure = null) =>
        services.AddLunariumAi(proxyUrl: null, configure: configure);

    /// <summary>
    ///     注册无状态单次推理客户端。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="proxyUrl">可选的代理；为 <c>null</c> 时直连。</param>
    /// <param name="configure">配置回调。</param>
    /// <remarks>
    ///     超时由每次调用的 <see cref="CancellationTokenSource" /> 控制，<see cref="HttpClient.Timeout" /> 恒为无限。
    ///     如需自定义 <see cref="HttpMessageHandler" />（例如测试桩），可在此方法之后调用
    ///     <c>services.AddHttpClient(ServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(...)</c>。
    /// </remarks>
    public static IServiceCollection AddLunariumAi(
        this IServiceCollection services,
        ProxyUrl? proxyUrl,
        Action<AiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<AiOptions>()
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true }, "AiOptions.BaseUrl 必须是绝对 URI。")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero, "AiOptions.RequestTimeout 必须大于 0。")
            .Validate(o => o.MaxRetries >= 0, "AiOptions.MaxRetries 不能为负数。")
            .Validate(o => o.MaxInputCharacters > 0, "AiOptions.MaxInputCharacters 必须大于 0。");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        var httpClientBuilder = services
            .AddHttpClient(HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        if (proxyUrl is not null)
        {
            var webProxy = proxyUrl.ToWebProxy();
            httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                Proxy = webProxy,
                UseProxy = true,
            });
        }

        services.TryAddSingleton<InMemoryAiKeyProvider>();
        services.TryAddSingleton<IAiKeyProvider>(sp => sp.GetRequiredService<InMemoryAiKeyProvider>());
        services.TryAddSingleton<IAiKeyConfigurator>(sp => sp.GetRequiredService<InMemoryAiKeyProvider>());
        services.TryAddSingleton<IAiClient, DeepSeekResponsesClient>();

        return services;
    }
}
