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
using Discord.WebSocket;

namespace LunariumBot.Net.DataCenter.DataProcess;

/// <summary>
/// 数据处理服务，负责处理 Discord 事件数据并将其持久化到数据库。
/// 该服务通过部分类（partial class）的方式拆分为多个文件：
/// - DataProcess.cs: 核心配置和依赖管理
/// - ChannelProcessor.cs: 频道处理逻辑
/// - ThreadProcessor.cs: 帖子处理逻辑
/// - MessageProcess.cs: 消息处理逻辑
/// </summary>
public partial class DataProcessService
{
    #region 字段与依赖
    /// <summary>日志记录器</summary>
    public readonly ILogger _logger;

    /// <summary>配置</summary>
    public readonly ConfigLoader _configLoader;

    /// <summary>数据库服务，用于执行数据库操作</summary>
    private readonly DatabaseService _databaseService;

    /// <summary>DiscordSocket 客户端</summary>
    private readonly DiscordSocketClient _discordSocketClient;

    /// <summary>Discord API 请求选项配置</summary>
    private readonly RequestOptions _requestOptions;

    /// <summary>HTTP 客户端，用于下载附件等网络操作</summary>
    private readonly HttpClient _httpClient;

    /// <summary>HTTP 请求重试策略，处理网络相关的临时性错误</summary>
    private readonly AsyncRetryPolicy _httpRetryPolicy;

    /// <summary>数据库操作重试策略，处理数据库连接相关的临时性错误</summary>
    private readonly AsyncRetryPolicy _dbRetryPolicy;

    /// <summary>信号量，用于控制 Discord API 请求的并发数量，避免超过速率限制</summary>
    private readonly SemaphoreSlim _semaphore;

    /// <summary>中文分词服务，用于处理搜索功能</summary>
    private readonly ITokenizerService _tokenizerService;

    #endregion

    #region 构造函数
    /// <summary>
    /// 初始化数据处理服务。
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="databaseService">数据库服务</param>
    /// <param name="proxyConfig">代理配置</param>
    /// <param name="tokenizerService">分词服务</param>
    /// <param name="semaphore">信号量</param>
    public DataProcessService(ILogger logger, ConfigLoader configLoader,DatabaseService databaseService, DiscordSocketClient discordSocketClient, ProxyConfig proxyConfig, ITokenizerService tokenizerService, SemaphoreSlim semaphore)
    {
        _logger = logger.ForContext("DataProcessService");
        _configLoader = configLoader;
        _databaseService = databaseService;
        _discordSocketClient = discordSocketClient;
        _tokenizerService = tokenizerService;
        _requestOptions = RequestOptionsConfig();
        _httpClient = HttpClientConfig(proxyConfig);
        _httpRetryPolicy = HttpRetryPolicyConfig();
        _dbRetryPolicy = DbRetryPolicy();
        _semaphore = semaphore;

        _logger.Info("✅ 数据处理服务已初始化。");
    }
    #endregion

    #region 配置方法
    /// <summary>
    /// 配置 HTTP 客户端。
    /// 如果启用了代理，则创建使用代理的客户端；否则创建默认客户端。
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

    /// <summary>
    /// 配置 Discord API 请求选项。
    /// 设置较长的超时时间以配合 Polly 重试策略，并启用 Discord.Net 的内置重试机制。
    /// </summary>
    /// <returns>配置好的 RequestOptions 实例</returns>
    public static RequestOptions RequestOptionsConfig()
    {
        // RequestOptions: 维持 Discord.Net 的默认重试机制
        // Timeout 设置为 20 分钟，确保 Polly 有足够时间完成重试
        return new RequestOptions
        {
            Timeout = 20 * 60 * 1000, // 设置总超时为 20 分钟
            RetryMode = RetryMode.AlwaysRetry // 启用 Discord.Net 的内置重试
        };
    }

    /// <summary>
    /// 配置 HTTP 请求重试策略（基于 Polly）。
    /// 该策略专门处理 Discord API 和网络相关的临时性错误。
    /// 使用指数退避策略：2秒、4秒、8秒。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        return Policy
            // 定义需要处理的异常:
            // - Discord.HttpException: 处理 5xx 服务器错误和 429 速率限制
            //   不重试: 404(资源不存在)、403(权限不足)、400(请求格式错误)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 && (int)ex.HttpCode != 403 && (int)ex.HttpCode != 400)
            // - HttpRequestException: 处理网络层级异常（SSL 握手失败、连接被重置等）
            .Or<HttpRequestException>()
            // - TaskCanceledException: 处理 HttpClient 超时
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)

            // 定义重试逻辑：指数退避
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2秒、4秒、8秒
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // 在日志中清晰显示触发重试的异常类型
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Warning("⚠️ HTTP 请求失败 ({ErrorType})，将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }

    /// <summary>
    /// 配置数据库操作重试策略（基于 Polly）。
    /// 该策略专门处理 PostgreSQL 数据库的临时性错误。
    /// 使用指数退避策略加随机抖动：2秒、4秒、8秒 + 0-100ms 随机延迟。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    private AsyncRetryPolicy DbRetryPolicy()
    {
        return Policy
            // 定义需要处理的异常:
            // - NpgsqlException: 处理 Npgsql 的暂时性故障（如连接池耗尽）
            .Handle<NpgsqlException>(ex => ex.IsTransient)
            // - TimeoutException: 处理连接或查询超时
            .Or<TimeoutException>()
            // - SocketException: 处理底层网络连接问题
            .Or<SocketException>()

            // 使用指数退避策略 + 随机抖动
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                onRetry: (exception, timespan, attempt, context) =>
                {
                    _logger.Warning("⚠️ 数据库操作失败，将在 {Seconds:F2} 秒后进行第 {Attempt}/3 次重试",
                        timespan.TotalSeconds, attempt);
                }
            );
    }
    #endregion
}