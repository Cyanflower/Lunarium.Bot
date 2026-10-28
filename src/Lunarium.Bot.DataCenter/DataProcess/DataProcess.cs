using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using Discord;
using Discord.Net;
using Polly;
using Polly.Retry;

using Lunarium.Common.Net;
using Lunarium.Logging;

using Lunarium.Bot.DataCenter.Database;
using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.DataCenter.Utils;
using Discord.WebSocket;

namespace Lunarium.Bot.DataCenter.DataProcess;

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
        ProxyUrl? proxyUrl,
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
        _httpClient = HttpClientConfig(proxyUrl);

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
    /// <param name="proxyUrl">代理配置，未启用时为 <see langword="null"/></param>
    /// <returns>配置好的 HttpClient 实例</returns>
    private static HttpClient HttpClientConfig(ProxyUrl? proxyUrl)
    {
        if (proxyUrl is not null)
        {
            return new HttpClient(new HttpClientHandler
            {
                Proxy = proxyUrl.ToWebProxy(),
                UseProxy = true
            });
        }
        else
        {
            return new HttpClient();
        }
    }
}