using Lunarium.Bot.Framework.Health;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Common.Net;
using Lunarium.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

namespace Lunarium.Bot.Framework;

/// <summary>
/// Bot 的 DI 注册扩展。
/// <para>
/// 每个方法只做一件事，调用方按需挑选——"核心"只有 Socket 客户端与交互服务，
/// 通知 / 命令同步 / 健康监控都是可选支持能力。
/// </para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册 <see cref="DiscordSocketClient"/>（可选代理）。</summary>
    public static IServiceCollection AddDiscordSocketClient(
        this IServiceCollection services,
        ProxyUrl? proxyUrl = null,
        Action<DiscordSocketConfig>? configure = null)
    {
        return services.AddSingleton(provider =>
        {
            var config = new DiscordSocketConfig();
            if (proxyUrl is not null)
            {
                var webProxy = proxyUrl.ToWebProxy();
                config.RestClientProvider = DefaultRestClientProvider.Create(useProxy: true, webProxy: webProxy);
                config.WebSocketProvider = DefaultWebSocketProvider.Create(webProxy);
            }

            configure?.Invoke(config);
            return new DiscordSocketClient(config);
        });
    }

    /// <summary>注册 <see cref="InteractionService"/>（依赖 <see cref="DiscordSocketClient"/>）。</summary>
    public static IServiceCollection AddInteractionService(this IServiceCollection services)
    {
        services.TryAddSingleton(provider =>
            new InteractionService(provider.GetRequiredService<DiscordSocketClient>()));

        return services;
    }

    /// <summary>注册 Discord.Net 日志到 Lunarium 日志的桥接器。</summary>
    public static IServiceCollection AddDiscordLogAdapter(this IServiceCollection services)
    {
        services.TryAddSingleton<DiscordLogAdapter>();
        return services;
    }

    /// <summary>注册上线 / 重连 / 网关事件通知器（发送目标由调用方在调用时传入）。</summary>
    public static IServiceCollection AddStatusNotifier(this IServiceCollection services)
    {
        services.TryAddSingleton<Notifier>();
        return services;
    }

    /// <summary>注册 @bot 重新注册斜杠命令的处理器。</summary>
    public static IServiceCollection AddCommandSynchronizer(this IServiceCollection services)
    {
        services.TryAddSingleton<CommandSynchronizer>();
        return services;
    }

    /// <summary>
    /// 注册健康监控。
    /// <para>
    /// 配置从独立的 <c>appsettings.health.json</c> 读取：运维配置与业务配置分文件，
    /// 业务侧的命令写回不会覆盖运维侧的修改。文件缺失或配置非法都会在启动时抛出。
    /// 健康监控只读取配置，不参与业务配置的写回/重载。
    /// </para>
    /// </summary>
    public static IServiceCollection AddHealthMonitor(this IServiceCollection services)
    {
        services.TryAddSingleton(provider =>
        {
            var logger = provider.GetRequiredService<ILogger>();
            var config = new ConfigLoader<HealthMonitorConfig>(logger, HealthMonitorConfig.DefaultFileName).Value;

            return new HealthMonitor(
                provider.GetRequiredService<DiscordSocketClient>(),
                logger,
                config);
        });

        return services;
    }
}
