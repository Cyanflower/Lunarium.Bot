using System.Text.RegularExpressions;
using System.Collections.Immutable;

// NuGet 包依赖
using Npgsql;
using Dapper;

// 解决方案内引用项目
using Lunarium.Bot.Logger;

// 项目内命名空间
using Lunarium.Bot.DataCenter.Models;

namespace Lunarium.Bot.DataCenter.Database;

/// <summary>
/// DatabaseService 的部分类，专门负责数据库迁移逻辑。
/// </summary>
public partial class DatabaseService
{
    /// <summary>
    /// 检查并执行数据库迁移。
    /// 主要用于启动时外部调用一次。
    /// </summary>
    public async Task<(bool Success, DatabaseInfo? LatestInfo)> CheckAndApplyMigrationsAsync()
    {
        _logger.Info("🚀 开始检查数据库迁移更新...");

        try
        {
            // 检查迁移
            var migrationInfo = await CheckMigrationAsync();

            if (migrationInfo.MigrationScripts.Count == 0)
            {
                _logger.Info("✅ 数据库已是最新版本，无需迁移。");
                return (true, migrationInfo.CurrentVersionInfo);
            }

            // 应用迁移
            var (success, latestInfo) = await ApplyMigrationsAsync(migrationInfo);

            if (success)
            {
                _logger.Info("🎉 数据库迁移成功完成！");
            }
            else
            {
                _logger.Warning("⚠️ 数据库迁移部分完成或失败。");
            }

            return (success, latestInfo);
        }
        catch (Exception ex)
        {
            _logger.Critical("数据库迁移过程遭遇无法恢复的错误。", ex);
            throw;
        }
    }

    /// <summary>
    /// 检查当前数据库版本，并获取待执行的迁移脚本列表。
    /// </summary>
    /// <returns>包含当前版本信息、预测版本信息和待执行迁移脚本的 DatabaseMigrationInfo。</returns>
    public async Task<DatabaseMigrationInfo> CheckMigrationAsync()
    {
        _logger.Info("正在检查数据库版本和待执行的迁移脚本...");

        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();

            // 1. 确保 db_info 表存在
            await EnsureDbInfoTableExistsAsync(connection);

            // 2. 获取当前数据库版本信息
            var currentInfo = await GetCurrentDatabaseInfoAsync(connection);

            if (currentInfo is null)
            {
                // 如果数据库为空（没有版本记录），返回版本0的空信息
                currentInfo = new DatabaseInfo
                {
                    Version = 0,
                    AppliedAt = DateTimeOffset.UtcNow,
                    LastSyncAt = DateTimeOffset.UtcNow,
                    InitializeAt = DateTimeOffset.UtcNow
                };
            }

            _logger.Info("当前数据库版本: {Version}", currentInfo.Version);

            // 3. 读取 DBMigrations 目录，查找所有版本大于当前版本的迁移脚本
            var migrationScripts = await FindPendingMigrationScriptsAsync(currentInfo.Version);

            // 4. 根据最新的迁移脚本，计算预测的版本信息
            DatabaseInfo predictInfo;
            if (migrationScripts.Count > 0)
            {
                // 获取最新版本号（最后一个脚本的版本号）
                var latestVersion = migrationScripts.Max(s => ExtractVersionFromFileName(s.ScriptFileName));

                predictInfo = new DatabaseInfo
                {
                    Version = latestVersion,
                    AppliedAt = DateTimeOffset.UtcNow, // 预测时间，实际执行时会使用 NOW()
                    LastSyncAt = currentInfo.LastSyncAt, // 复制当前的 last_sync_at
                    InitializeAt = currentInfo.InitializeAt // 复制当前的 initialize_at
                };

                _logger.Info("发现 {Count} 个待处理的迁移脚本，预测版本: {PredictVersion}",
                    migrationScripts.Count, latestVersion);
            }
            else
            {
                predictInfo = currentInfo; // 无需更新
            }

            return new DatabaseMigrationInfo
            {
                CurrentVersionInfo = currentInfo,
                PredictVersionInfo = predictInfo,
                MigrationScripts = migrationScripts
            };
        }
        catch (Exception ex)
        {
            _logger.Error("检查数据库迁移时发生错误。", ex);
            throw;
        }
    }

    /// <summary>
    /// 接受 DatabaseMigrationInfo，按顺序执行迁移脚本。
    /// </summary>
    /// <param name="migrationInfo">包含待执行迁移脚本的信息。</param>
    /// <returns>是否成功的 bool 和最新的 DatabaseInfo。</returns>
    public async Task<(bool Success, DatabaseInfo? LatestInfo)> ApplyMigrationsAsync(DatabaseMigrationInfo migrationInfo)
    {
        if (migrationInfo.MigrationScripts.Count == 0)
        {
            _logger.Info("没有待执行的迁移脚本。");
            return (true, migrationInfo.CurrentVersionInfo);
        }

        _logger.Info("开始应用 {Count} 个迁移脚本...", migrationInfo.MigrationScripts.Count);

        DatabaseInfo? latestInfo = migrationInfo.CurrentVersionInfo;
        bool allSuccess = true;

        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();

            // 按版本顺序循环迁移脚本
            var sortedScripts = migrationInfo.MigrationScripts
                .OrderBy(s => ExtractVersionFromFileName(s.ScriptFileName))
                .ToList();

            foreach (var script in sortedScripts)
            {
                var version = ExtractVersionFromFileName(script.ScriptFileName);

                try
                {
                    _logger.Info("... 正在应用 v{Version}: {FileName}", version, script.ScriptFileName);

                    // 使用事务包裹每个独立的迁移流程
                    await using var transaction = await connection.BeginTransactionAsync();

                    try
                    {
                        // 1. 执行迁移脚本
                        await connection.ExecuteAsync(script.ScriptSQLCommandContent, transaction: transaction);

                        // 2. 获取上一个版本的 last_sync_at 和 initialize_at
                        var previousInfo = await GetDatabaseInfoByVersionAsync(connection, transaction, version - 1);

                        DateTimeOffset lastSyncAt;
                        DateTimeOffset initializeAt;

                        if (previousInfo is not null)
                        {
                            // 复制上一个版本的值
                            lastSyncAt = previousInfo.LastSyncAt;
                            initializeAt = previousInfo.InitializeAt;
                        }
                        else
                        {
                            // 如果是第一个版本（version = 1），则使用当前时间初始化
                            var now = DateTimeOffset.UtcNow;
                            lastSyncAt = now;
                            initializeAt = now;
                        }

                        // 3. 更新 db_info 表（使用 NOW() 作为 applied_at）
                        const string updateSql = """
                            INSERT INTO db_info (version, applied_at, last_sync_at, initialize_at)
                            VALUES (@Version, NOW(), @LastSyncAt, @InitializeAt);
                            """;

                        await connection.ExecuteAsync(updateSql, new
                        {
                            Version = version,
                            LastSyncAt = lastSyncAt,
                            InitializeAt = initializeAt
                        }, transaction);

                        // 4. 提交事务
                        await transaction.CommitAsync();

                        // 5. 获取刚刚插入的最新信息（包含数据库生成的 applied_at）
                        latestInfo = await GetDatabaseInfoByVersionAsync(connection, null, version);

                        _logger.Info("✅ v{Version} 应用成功。", version);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("应用迁移 v{Version} 时发生错误，事务已回滚。", ex, version);
                        await transaction.RollbackAsync();

                        // 遇到错误，直接跳出循环
                        allSuccess = false;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("处理迁移 v{Version} 时发生严重错误。", ex, version);
                    allSuccess = false;
                    break;
                }
            }

            return (allSuccess, latestInfo);
        }
        catch (Exception ex)
        {
            _logger.Critical("执行数据库迁移时发生无法恢复的错误。", ex);
            return (false, latestInfo);
        }
    }

    /// <summary>
    /// 确保 db_info 表存在。
    /// </summary>
    private async Task EnsureDbInfoTableExistsAsync(NpgsqlConnection connection)
    {
        const string createTableSql = """
            CREATE TABLE IF NOT EXISTS db_info (
                version INT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL,
                last_sync_at TIMESTAMPTZ NOT NULL,
                initialize_at TIMESTAMPTZ NOT NULL
            );
            """;

        await connection.ExecuteAsync(createTableSql);
    }

    /// <summary>
    /// 获取当前数据库的最新版本信息。
    /// </summary>
    private async Task<DatabaseInfo?> GetCurrentDatabaseInfoAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT version, applied_at, last_sync_at, initialize_at
            FROM db_info
            ORDER BY version DESC
            LIMIT 1;
            """;

        var result = await connection.QueryFirstOrDefaultAsync<DatabaseInfoRow?>(sql);

        if (result is null)
        {
            return null;
        }

        return new DatabaseInfo
        {
            Version = result.Version,
            AppliedAt = result.AppliedAt,
            LastSyncAt = result.LastSyncAt,
            InitializeAt = result.InitializeAt
        };
    }

    /// <summary>
    /// 根据版本号获取数据库信息。
    /// </summary>
    private async Task<DatabaseInfo?> GetDatabaseInfoByVersionAsync(
        NpgsqlConnection connection,
        System.Data.IDbTransaction? transaction,
        int version)
    {
        const string sql = """
            SELECT version, applied_at, last_sync_at, initialize_at
            FROM db_info
            WHERE version = @Version;
            """;

        var result = await connection.QueryFirstOrDefaultAsync<DatabaseInfoRow?>(
            sql,
            new { Version = version },
            transaction);

        if (result is null)
        {
            return null;
        }

        return new DatabaseInfo
        {
            Version = result.Version,
            AppliedAt = result.AppliedAt,
            LastSyncAt = result.LastSyncAt,
            InitializeAt = result.InitializeAt
        };
    }

    /// <summary>
    /// 查找所有待执行的迁移脚本（版本号大于 currentVersion）。
    /// </summary>
    private async Task<ImmutableList<DatabaseMigrationScript>> FindPendingMigrationScriptsAsync(int currentVersion)
    {
        if (!Directory.Exists(_migrationsPath))
        {
            _logger.Warning("迁移文件夹 '{MigrationsPath}' 不存在。", _migrationsPath);
            return ImmutableList<DatabaseMigrationScript>.Empty;
        }

        var versionRegex = new Regex(@"^v(\d+)\..*\.pgsql$", RegexOptions.IgnoreCase);
        var scripts = new List<(int Version, string FileName, string Content)>();

        var files = Directory.GetFiles(_migrationsPath, "v*.pgsql");

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var match = versionRegex.Match(fileName);

            if (match.Success && int.TryParse(match.Groups[1].Value, out int version))
            {
                if (version > currentVersion)
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    scripts.Add((version, fileName, content));
                }
            }
            else
            {
                _logger.Warning("已忽略格式不正确的迁移文件: {FileName}", fileName);
            }
        }

        // 按版本号排序
        var sortedScripts = scripts
            .OrderBy(s => s.Version)
            .Select(s => new DatabaseMigrationScript
            {
                ScriptFileName = s.FileName,
                ScriptSQLCommandContent = s.Content
            })
            .ToImmutableList();

        return sortedScripts;
    }

    /// <summary>
    /// 从文件名中提取版本号。
    /// </summary>
    private int ExtractVersionFromFileName(string fileName)
    {
        var versionRegex = new Regex(@"^v(\d+)\.", RegexOptions.IgnoreCase);
        var match = versionRegex.Match(fileName);

        if (match.Success && int.TryParse(match.Groups[1].Value, out int version))
        {
            return version;
        }

        return 0;
    }

    /// <summary>
    /// 数据库行映射类（用于 Dapper 查询结果映射）。
    /// </summary>
    private class DatabaseInfoRow
    {
        public int Version { get; set; }
        public DateTimeOffset AppliedAt { get; set; }
        public DateTimeOffset LastSyncAt { get; set; }
        public DateTimeOffset InitializeAt { get; set; }
    }
}
