using System.Text.RegularExpressions;

// NuGet 包依赖
using Npgsql;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Model;
using LunariumBot.Net.DataCenter.Utils;

namespace LunariumBot.Net.DataCenter.Database;

/// <summary>
/// 负责与 PostgreSQL 数据库进行交互的服务。
/// 管理连接、按需执行数据库结构迁移。
/// </summary>
public partial class DatabaseService : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader1;
    private readonly string _connectionString;
    private readonly string _migrationsPath;

    /// <summary>
    /// 初始化数据库服务。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="configuration">应用程序配置，用于获取连接字符串。</param>
    public DatabaseService(ILogger logger, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("DatabaseService");
        _configLoader1 = configLoader;
        // 从 appsettings.json 读取连接字符串
        _connectionString = _configLoader1.Config.DatabaseConnection
            ?? throw new InvalidOperationException("在配置中未找到 'PostgresSQL' 的连接字符串。");

        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        // 定义迁移脚本的存放路径
        _migrationsPath = Path.Combine(AppContext.BaseDirectory, "DBMigrations");

        _logger.Info("数据库服务已初始化。");
    }

    /// <summary>
    /// 创建并返回一个新的数据库连接。
    /// </summary>
    public NpgsqlConnection GetConnection() => new NpgsqlConnection(_connectionString);

    /// <summary>
    /// 检查并应用所有新的数据库迁移脚本。
    /// </summary>
    public async Task MigrateAsync()
    {
        _logger.Info("🚀 开始检查并执行数据库迁移...");

        if (!Directory.Exists(_migrationsPath))
        {
            _logger.Warning("迁移文件夹 '{_migrationsPath}' 不存在，跳过迁移。", _migrationsPath);
            return;
        }

        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();

            // 1. 确保版本追踪表存在，并获取数据库的 "生日"
            var initializeTimestamp = await EnsureSchemaInfoTableExistsAsync(connection);

            // 2. 获取当前数据库版本
            var currentVersion = await GetCurrentSchemaVersionAsync(connection);
            _logger.Info("当前数据库版本: {currentVersion}", currentVersion);

            // 3. 查找需要应用的迁移脚本
            var pendingMigrations = FindAndSortMigrationFiles(currentVersion);

            if (!pendingMigrations.Any())
            {
                _logger.Info("✅ 数据库已是最新版本，无需迁移。");
                return;
            }

            _logger.Info("发现 {pendingMigrations.Count} 个待处理的迁移脚本。", pendingMigrations.Count);

            // 4. 在单个事务中执行所有待处理的迁移
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                foreach (var migration in pendingMigrations)
                {
                    _logger.Info("... 正在应用 v{migration.Version}: {migration.FileName}", migration.Version, migration.FileName);

                    string scriptContent = await File.ReadAllTextAsync(migration.FilePath);
                    await connection.ExecuteAsync(scriptContent, transaction: transaction);

                    // 更新版本号，并传入 initializeTimestamp
                    await UpdateSchemaVersionAsync(connection, transaction, migration.Version, initializeTimestamp);
                }

                await transaction.CommitAsync();
                _logger.Info("🎉 所有迁移脚本已成功应用！当前数据库版本: {pendingMigrations.Last().Version}", pendingMigrations.Last().Version);
            }
            catch (Exception ex)
            {
                _logger.Error("迁移过程中发生严重错误，事务已回滚！", ex);
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Critical("数据库迁移过程遭遇无法恢复的错误。", ex);
            throw;
        }
    }

    /// <summary>
    /// 确保用于追踪数据库版本的 `db_info` 表存在，并返回数据库的初始化时间戳。
    /// </summary>
    /// <returns>数据库的初始化时间戳 (Unix Timestamp)。</returns>
    private async Task<long> EnsureSchemaInfoTableExistsAsync(NpgsqlConnection connection)
    {
        // SQL to create the table if it doesn't exist.
        // NOTE: In PostgreSQL, INTEGER is a 4-byte integer. BIGINT is an 8-byte integer.
        // Unix timestamps fit in BIGINT.
        const string createTableSql = """
            CREATE TABLE IF NOT EXISTS db_info (
                version INT PRIMARY KEY,
                applied_at BIGINT NOT NULL,
                initialize_at BIGINT NOT NULL
            );
            """;
        await connection.ExecuteAsync(createTableSql);

        // 检查表中是否已有记录，特别是 `initialize_at`
        var initializeAt = await connection.QueryFirstOrDefaultAsync<long?>(
            "SELECT initialize_at FROM db_info ORDER BY version ASC LIMIT 1"
        );

        if (initializeAt.HasValue)
        {
            // 如果已存在，直接返回这个值
            return initializeAt.Value;
        }
        else
        {
            // 如果不存在（说明是全新的数据库），生成新的时间戳并返回
            var newInitializeTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _logger.Info("这是一个全新的数据库，记录初始化时间戳: {newInitializeTimestamp}", newInitializeTimestamp);
            return newInitializeTimestamp;
        }
    }

    /// <summary>
    /// 从数据库中获取当前的版本号。
    /// </summary>
    private async Task<int> GetCurrentSchemaVersionAsync(NpgsqlConnection connection)
    {
        const string getVersionSql = "SELECT COALESCE(MAX(version), 0) FROM db_info;";
        return await connection.QuerySingleAsync<int>(getVersionSql);
    }

    /// <summary>
    /// 更新数据库中的版本号记录。
    /// </summary>
    private async Task UpdateSchemaVersionAsync(NpgsqlConnection connection, System.Data.Common.DbTransaction transaction, int newVersion, long initializeTimestamp)
    {
        const string updateVersionSql = "INSERT INTO db_info (version, applied_at, initialize_at) VALUES (@Version, @AppliedAt, @InitializeAt);";
        await connection.ExecuteAsync(updateVersionSql, new
        {
            Version = newVersion,
            AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            InitializeAt = initializeTimestamp // 每次都写入相同的初始化时间
        }, transaction);
    }

    /// <summary>
    /// 查找、解析并排序所有需要应用的迁移脚本文件。
    ///  这个方法的返回值类型签名是公开的，所以它内部使用的类型不能是 file-local 的。
    /// </summary>
    private List<MigrationScript> FindAndSortMigrationFiles(int currentVersion)
    {
        var versionRegex = new Regex(@"^v(\d+)\..*\.sql$", RegexOptions.IgnoreCase);

        return Directory.GetFiles(_migrationsPath, "v*.sql")
            .Select(filePath =>
            {
                var fileName = Path.GetFileName(filePath);
                var match = versionRegex.Match(fileName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int version))
                {
                    return new MigrationScript(version, filePath, fileName);
                }
                _logger.Warning("已忽略格式不正确的文件: {fileName}", fileName);
                return null;
            })
            .Where(script => script != null && script.Version > currentVersion)
            .OrderBy(script => script!.Version)
            .ToList()!;
    }
    
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
}

/// <summary>
/// 一个简单的数据记录，用于封装迁移脚本的信息。
/// 使用 'internal' 关键字，使其在项目内部可见，解决了 CS9051 错误。
/// </summary>
internal record MigrationScript(int Version, string FilePath, string FileName);