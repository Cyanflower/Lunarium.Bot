
using System.Net.Sockets;

// NuGet 包依赖
using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.Database;

/// <summary>
/// 负责与 PostgreSQL 数据库进行交互的服务。
/// 管理连接、按需执行数据库结构迁移。
/// </summary>
public partial class DatabaseService : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly string _connectionString;

    /// <summary>数据库操作重试策略，处理数据库连接相关的临时性错误</summary>
    private readonly AsyncRetryPolicy _dbRetryPolicy;

    /// <summary>
    /// 初始化数据库服务。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="configuration">应用程序配置，用于获取连接字符串。</param>
    public DatabaseService(ILogger logger, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("DatabaseService");
        _configLoader = configLoader;
        // 从 appsettings.json 读取连接字符串
        _connectionString = _configLoader.Config.DatabaseConnection
            ?? throw new InvalidOperationException("在配置中未找到 'PostgresSQL' 的连接字符串。");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        
        _dbRetryPolicy = DbRetryPolicy();

        _logger.Info("数据库服务已初始化。");
    }

    /// <summary>
    /// 创建并返回一个新的数据库连接。
    /// </summary>
    public NpgsqlConnection GetConnection() => new NpgsqlConnection(_connectionString);

    /// <summary>
    /// 实现 IDisposable 接口，用于在服务生命周期结束时执行异步清理操作。
    /// 这个方法会被 DI 容器在应用程序关闭时自动调用。
    /// </summary>
    public void Dispose()
    {
        _logger.Info("⏳ 开始执行 DatabaseService 的清理操作...");

        // 调用 Npgsql 的静态方法来清理与此连接字符串相关的所有连接池。
        // 这是一个重量级操作，只应在应用程序关闭时执行。
        _logger.Info("正在清理与连接字符串关联的 Npgsql 连接池...");
        NpgsqlConnection.ClearAllPools();

        _logger.Info("✅ Npgsql 连接池已清理。DatabaseService 清理完成。");

        GC.SuppressFinalize(this);
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