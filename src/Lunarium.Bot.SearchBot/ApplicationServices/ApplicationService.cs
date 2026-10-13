using System.Net.Sockets;

using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

using Lunarium.Bot.Logger;

using Lunarium.Bot.SearchBot.Database;
using Lunarium.Bot.SearchBot.SearchCache;
using Lunarium.Bot.SearchBot.Paginator;

namespace Lunarium.Bot.SearchBot.ApplicationServices;

public partial class ApplicationService
{
    /// <summary>日志记录器</summary>
    public readonly ILogger _logger;

    /// <summary>配置</summary>
    public readonly ConfigLoader _configLoader;

    /// <summary>数据库服务，用于执行数据库操作</summary>
    private readonly DatabaseService _databaseService;

    /// <summary>帖子对象弱引用缓存池，用于所有用户交互共用的帖子信息对象</summary>
    private readonly SearchCacheService _searchCacheService;

    /// <summary>泛型翻页器缓存池，用于所有用户交互共用的页面缓存</summary>
    private readonly PaginatorManager _paginatorManager;

    /// <summary>数据库操作重试策略，处理数据库连接相关的临时性错误</summary>
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    
    public ApplicationService(ILogger logger, ConfigLoader configLoader,DatabaseService databaseService, SearchCacheService searchCacheService, PaginatorManager paginatorManager)
    {
        _logger = logger.ForContext("ApplicationService");
        _configLoader = configLoader;
        _databaseService = databaseService;
        _searchCacheService = searchCacheService;
        _paginatorManager = paginatorManager;
        _dbRetryPolicy = DbRetryPolicy();
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
}