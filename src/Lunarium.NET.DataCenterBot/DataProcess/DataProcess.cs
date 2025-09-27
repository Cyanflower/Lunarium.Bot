using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

using Npgsql;
using Discord;
using Discord.Net;
using Polly;
using Polly.Retry;

using LunariumBot.Net.Logger;

using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;
using LunariumBot.Net.DataCenter.Utils;

namespace LunariumBot.Net.DataCenter.DataProcess;

public partial class DataProcessService
{
    public readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly RequestOptions _requestOptions;
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    private readonly SemaphoreSlim _semaphore;
    private readonly ITokenizerService _tokenizerService;
    private readonly ConcurrentDictionary<ulong, (string MessageContent, TaskCompletionSource<bool> TaskCompletionSource)> _messageDataToThreadDataForCreateThread = new();
    public DataProcessService(ILogger logger, DatabaseService databaseService, ProxyConfig proxyConfig, ITokenizerService tokenizerService)
    {
        _logger = logger.ForContext("DataProcessService");
        _databaseService = databaseService;
        _tokenizerService = tokenizerService;
        _requestOptions = RequestOptionsConfig();
        _httpClient = HttpClientConfig(proxyConfig);
        _httpRetryPolicy = HttpRetryPolicyConfig();
        _dbRetryPolicy = DbRetryPolicy();
        _semaphore = new SemaphoreSlim(25, 25);
    }

    private static HttpClient HttpClientConfig(ProxyConfig proxyConfig)
    {
        // http客户端单例, 用于检查作品更新的png下载
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

    public static RequestOptions RequestOptionsConfig()
    {
        // RequestOptions: 维持 Discord.Net 的默认重试机制
        // Timeout设置一个比HttpClient超时更长的时间, 确保Polly有足够时间完成重试
        return new RequestOptions
        {
            Timeout = 20 * 60 * 1000, // 设置总超时为20分钟
            RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试
        };
    }

    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        // --- Polly 策略配置 ---
        // 配置一个重试策略, 专门用于处理Http的网络重试逻辑
        return Policy
            // 定义需要处理的异常:
            // - 处理任意类型的 Discord.HttpException (例如 500, 502, 503, 429 等, 如果是404(不存在), 403(权限不足), 400(请求格式错误)则不重试)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 || (int)ex.HttpCode != 403 || (int)ex.HttpCode != 400)
            // - 处理网络层级的异常，比如 SSL 握手失败、连接被重置等
            .Or<HttpRequestException>()
            // - 对 HttpClient 超时的处理
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)

            // 定义重试逻辑 (这部分保持不变)
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试3次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 等待时长: 2s, 4s, 8s
                onRetry: (exception, timespan, attempt, context) => // 每次重试时执行的回调
                {
                    // 在日志中可以更清晰地看到是哪种异常触发了重试
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Error(exception, "HTTP请求失败 ({errorType})，在 {timespan.TotalSeconds}秒 后进行第 {attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }

    private AsyncRetryPolicy DbRetryPolicy()
    {
        // --- Polly 策略配置 ---
        // 配置一个重试策略, 专门用于处理数据库操作的重试逻辑
        return Policy
            // 定义需要处理的异常:
            // - 处理 Npgsql 的特定暂时性故障
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            // - 处理连接超时
            .Or<TimeoutException>()
            // - 处理更底层的网络连接问题
            .Or<SocketException>()

            // 使用指数退避策略进行重试
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)), // 2s, 4s, 8s + 随机抖动
                onRetry: (exception, timespan, attempt, context) =>
                {
                    _logger.Error(exception,
                        "数据库操作失败，将在 {Timespan} 秒后进行第 {Attempt}/3 次重试",
                        timespan.TotalSeconds, attempt);
                }
            );
    }
}