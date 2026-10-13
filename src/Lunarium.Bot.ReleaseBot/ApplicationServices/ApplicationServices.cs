using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

using Lunarium.Bot.Logger;

using Lunarium.Bot.ReleaseBot.Database;

namespace Lunarium.Bot.ReleaseBot.ApplicationServices;

public partial class ApplicationService
{
    /// <summary>日志记录器</summary>
    public readonly ILogger _logger;

    /// <summary>配置</summary>
    public readonly ConfigLoader _configLoader;

    /// <summary>数据库服务，用于执行数据库操作</summary>
    private readonly DatabaseService _databaseService;

    /// <summary>数据库操作重试策略，处理数据库连接相关的临时性错误</summary>
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    
    public ApplicationService(ILogger logger, ConfigLoader configLoader, DatabaseService databaseService, [FromKeyedServices("DbPolicy")] AsyncRetryPolicy dbRetryPolicy)
    {
        _logger = logger.ForContext("ApplicationService");
        _configLoader = configLoader;
        _databaseService = databaseService;
        _dbRetryPolicy = dbRetryPolicy;
    }
}