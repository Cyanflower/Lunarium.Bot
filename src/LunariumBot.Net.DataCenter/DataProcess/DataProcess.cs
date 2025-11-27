using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using Discord;
using Discord.Net;
using Polly;
using Polly.Retry;

using LunariumBot.Net.Logger;

using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Models;
using LunariumBot.Net.DataCenter.Utils;
using Discord.WebSocket;

namespace LunariumBot.Net.DataCenter.DataProcess;

public partial class DataProcessService
{
    public readonly ILogger _logger;
    public readonly ConfigLoader _configLoader;
    private readonly DatabaseService _databaseService;
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _apiRequestRateLimiter;
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly AsyncRetryPolicy _dbRetryPolicy;


    public DataProcessService(
        ILogger logger,
        ConfigLoader configLoader,
        DatabaseService databaseService,
        DiscordSocketClient discordSocketClient,
        ProxyConfig proxyConfig,
        RateLimiter rateLimiter,
        RequestOptions requestOptions,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy,
        [FromKeyedServices("DbPolicy")] AsyncRetryPolicy dbRetryPolicy
    )
    {
        _logger = logger.ForContext("DataProcessService");
        _configLoader = configLoader;
        _databaseService = databaseService;
        _discordSocketClient = discordSocketClient;
        _httpClient = HttpClientConfig(proxyConfig);

        _apiRequestRateLimiter = rateLimiter;
        _requestOptions = requestOptions;
        _httpRetryPolicy = httpRetryPolicy;
        _dbRetryPolicy = dbRetryPolicy;

        _logger.Info("✅ 数据处理服务已初始化。");
    }

    /// <summary>
    /// 配置 HTTP 客户端。
    /// 如果启用了代理, 则创建使用代理的客户端；否则创建默认客户端。
    /// 该客户端主要用于下载附件（如检查作品更新的 PNG 图片）。
    /// </summary>
    /// <param name="proxyConfig">代理配置对象</param>
    /// <returns>配置好的 HttpClient 实例</returns>
    private static HttpClient HttpClientConfig(ProxyConfig proxyConfig)
    {
        if (proxyConfig.Enable && !string.IsNullOrEmpty(proxyConfig.ProxyUrl))
        {
            return new HttpClient(new HttpClientHandler
            {
                Proxy = new WebProxy(proxyConfig.ProxyUrl),
                UseProxy = true
            });
        }
        else
        {
            return new HttpClient();
        }
    }
}