using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SearchBot.Utils;
using System.Text;
using System.Threading.Tasks;
using SearchBot.ThreadFetcher;
using SearchBot.Model;
using Dapper; // 添加 Dapper 引用

namespace SearchBot.Database;

/// <summary>
/// 管理 SQLite 内存数据库的连接与全部操作。
/// </summary>
public partial class DatabaseService : IDisposable
{
    private readonly ILogger<DatabaseService> _logger;
    private readonly SqliteConnection _anchorConnection;
    private readonly ITokenizerService _tokenizer;
    private const string _dbFilePath = "Data/SearchBot.db"; // 数据持久化文件路径
    
    // 使用带命名和共享缓存的内存数据库连接字符串, 确保在服务内部能共享同一个内存数据库
    const string _connectionString = "Data Source=SearchBotDB;Mode=Memory;Cache=Shared";
    
    public bool NeedSync { get; private set; }
    public string BaselineVersion { get; private set; } = "";

    public DatabaseService(ILogger<DatabaseService> logger, ITokenizerService tokenizer)
    {
        _logger = logger;
        _logger.LogInformation("🚀 启动 SearchBot 数据库服务...");
        _tokenizer = tokenizer;
        _anchorConnection = new SqliteConnection(_connectionString);
        _anchorConnection.Open();
        _logger.LogInformation(message: "✅ 成功建立内存数据库连接。");

        // 检查持久化文件是否存在
        if (File.Exists(_dbFilePath))
        {
            _logger.LogInformation("🔍 发现持久化数据库文件，执行加载。");
            NeedSync = LoadDB();
        }
        else
        {
            _logger.LogInformation("📄 未发现持久化数据库文件，执行初始化。");
            InitializeDB();
            NeedSync = true;
        }
    }
    
    /// <summary>
    /// 保存内存数据库到文件。
    /// </summary>
    public void SaveDB()
    {
        _logger.LogInformation("💾 开始保存内存数据库到文件: {FilePath}...", _dbFilePath);

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            // 更新最后保存时间
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            connection.Execute(
                "UPDATE db_info SET last_save_at = @timestamp WHERE version = @version",
                new { timestamp, version = DBTable.CURRENT_DB_VERSION }
            );
            
            // 确保目录存在
            var directory = Path.GetDirectoryName(_dbFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // 创建一个到磁盘文件的连接
            using var fileDbConnection = new SqliteConnection($"Data Source={_dbFilePath}");
            fileDbConnection.Open();
            
            // 将内存数据库的内容备份到文件数据库
            connection.BackupDatabase(fileDbConnection);
            _logger.LogInformation("✅ 内存数据库已成功保存到文件。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 保存数据库到文件时发生错误。");
        }
    }

    // === 准备数据库 ===
    /// <summary>
    /// 初始化数据库，创建所有必要的表和索引。
    /// </summary>
    private void InitializeDB()
    {
        _logger.LogInformation("正在创建数据库表...");

        try
        {   
            // 获取当前时间戳
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            // 首先创建版本信息表
            connection.Execute(
                DBTable.GetCreateVersionTableSql(),
                new { 
                    Version = DBTable.CURRENT_DB_VERSION,
                    InitializeAt = timestamp,
                    AppliedAt = timestamp, 
                    LastSaveAt = timestamp 
                }
            );
            BaselineVersion = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToString("yyyy/MM/dd");
            
            // 然后创建所有其他表
            connection.Execute(DBTable.GetCreateAllTablesSql());
            
            _logger.LogInformation("✅ 数据库表创建完成。版本: {Version}", DBTable.CURRENT_DB_VERSION);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 初始化数据库表时发生错误。");
            throw;
        }
    }
    
    /// <summary>
    /// 从磁盘文件加载数据到内存数据库。
    /// </summary>
    public bool LoadDB()
    {
        _logger.LogInformation("正在从文件 {FilePath} 加载数据到内存数据库...", _dbFilePath);
        
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            // 创建一个到磁盘文件的连接
            using var fileDbConnection = new SqliteConnection($"Data Source={_dbFilePath}");
            fileDbConnection.Open();

            // 将文件数据库的内容备份(恢复)到内存数据库
            fileDbConnection.BackupDatabase(connection);
            
            _logger.LogInformation("✅ 数据库已成功从文件加载到内存。");

            // 检查并执行数据库迁移
            bool needsUpdate = CheckAndRunMigrations(connection);
            
            // 获取数据库信息 - 使用 Dapper
            var dbInfo = connection.QueryFirstOrDefault<DbInfo>(
                "SELECT version, applied_at AS AppliedAt, initialize_at AS InitializeAt, last_save_at AS LastSaveAt FROM db_info WHERE version = (SELECT MAX(version) FROM db_info)"
            );

            if (dbInfo != null)
            {
                BaselineVersion = DateTimeOffset.FromUnixTimeSeconds(dbInfo.InitializeAt).ToString("yyyy/MM/dd");
                var appliedTime = DateTimeOffset.FromUnixTimeSeconds(dbInfo.AppliedAt).ToOffset(TimeSpan.FromHours(8));
                var saveTime = DateTimeOffset.FromUnixTimeSeconds(dbInfo.LastSaveAt).ToOffset(TimeSpan.FromHours(8));
                var currentTime = DateTimeOffset.Now;

                _logger.LogInformation("数据库版本: {Version}", dbInfo.Version);
                _logger.LogInformation("数据库初始化时间: {AppliedTime}", appliedTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("数据库保存时间: {SaveTime}", saveTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("当前UTC+8时间: {CurrentTime}", currentTime.ToString("yyyy-MM-dd HH:mm:ss"));

                // 检查是否需要更新（距离上次保存超过10分钟或发生了迁移）
                long currentTimestamp = currentTime.ToUnixTimeSeconds();
                bool timeBasedUpdate = (currentTimestamp - dbInfo.LastSaveAt) > (10 * 60);

                return needsUpdate || timeBasedUpdate;
            }
            else
            {
                // 如果没有找到版本信息，说明数据库已损坏或处于不一致的状态。
                // 这是一个严重错误，必须停止程序以防数据丢失。
                _logger.LogCritical("CRITICAL: 数据库完整性错误! 在 '{FilePath}' 中未找到 'db_info' 表或版本信息。", _dbFilePath);
                throw new InvalidOperationException(
                    $"数据库文件 '{_dbFilePath}' 处于不一致的状态，关键的 'db_info' 表或版本信息缺失。" +
                    "为防止数据丢失，Bot将立即关闭。" +
                    "请手动检查数据库文件。如果确认要重新开始，请删除该文件后重启Bot。"
                );
                
            }
        }
        catch (Exception ex)
        {
            // 如果加载失败，可能是文件损坏或不是一个有效的数据库文件。
            // 在这种情况下，必须停止程序以防数据丢失。
            var errorMessage = $"❌ 致命错误：从文件 '{_dbFilePath}' 加载数据库时发生无法恢复的错误。程序将终止以保护数据。";
            _logger.LogError(ex, errorMessage);
            throw;
        }
    }
    
    /// <summary>
    /// 检查并运行数据库迁移
    /// </summary>
    /// <param name="connection">数据库连接</param>
    /// <returns>是否执行了迁移</returns>
    private bool CheckAndRunMigrations(SqliteConnection connection)
    {
        bool hasUpdates = false;
        
        try
        {
            // 确保版本表存在（处理从旧版本升级的情况）- 使用 Dapper
            var tableExists = connection.QueryFirstOrDefault<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='db_info'"
            ) != null;
            
            if (!tableExists)
            {
                _logger.LogInformation("🔧 检测到旧版本数据库，创建版本信息表...");
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                connection.Execute(
                    DBTable.GetCreateVersionTableSql(),
                    new { 
                        Version = DBTable.CURRENT_DB_VERSION,
                        AppliedAt = timestamp, 
                        LastSaveAt = timestamp 
                    }
                );
                
                hasUpdates = true;
                _logger.LogInformation("✅ 版本信息表创建完成。");
            }
            
            var latestDbInfo = connection.QueryFirstOrDefault<(int Version, long InitializeAt)>(
                "SELECT version, initialize_at FROM db_info ORDER BY version DESC LIMIT 1"
            );

            var currentVersion = latestDbInfo.Version;
            if (currentVersion == 0)
            {
                //们直接抛出异常，中断程序，防止数据损坏。
                _logger.LogCritical("CRITICAL: 数据库完整性错误! 在 '{FilePath}' 中找到了空的 'db_info' 表。", _dbFilePath);
                throw new InvalidOperationException(
                    $"数据库文件 '{_dbFilePath}' 处于不一致的状态，关键的 'db_info' 表为空。" +
                    "为防止数据丢失，Bot将立即关闭。" +
                    "请手动检查数据库文件。如果确认要重新开始，请删除该文件后重启Bot。"
                );
            }
            
            _logger.LogInformation("当前数据库版本: {CurrentVersion}, 目标版本: {TargetVersion}", 
                currentVersion, DBTable.CURRENT_DB_VERSION);

            if (currentVersion < DBTable.CURRENT_DB_VERSION)
            {
                _logger.LogInformation("🔄 检测到数据库需要更新，开始执行迁移...");

                var migrations = DBTable.GetMigrationScripts();
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // 执行从当前版本+1到目标版本的所有迁移
                for (int version = currentVersion + 1; version <= DBTable.CURRENT_DB_VERSION; version++)
                {
                    if (migrations.ContainsKey(version))
                    {
                        _logger.LogInformation("📝 执行版本 {Version} 的迁移脚本...", version);

                        connection.Execute(migrations[version]);

                        _logger.LogInformation("✅ 版本 {Version} 迁移脚本执行完成", version);
                    }

                    // 记录迁移版本（无论是否有具体的迁移脚本）- 使用 Dapper
                    connection.Execute(
                        "INSERT OR REPLACE INTO db_info (version, applied_at, initialize_at, last_save_at) VALUES (@version, @appliedAt, @initializeAt, @lastSaveAt)",
                        new { version, appliedAt = timestamp, initializeAt = latestDbInfo.InitializeAt, lastSaveAt = timestamp }
                    );

                    _logger.LogInformation("✅ 版本 {Version} 迁移完成", version);
                    hasUpdates = true;
                }

                if (hasUpdates)
                {
                    _logger.LogInformation("🎉 数据库迁移完成！当前版本: {Version}", DBTable.CURRENT_DB_VERSION);
                }
            }
            else if (currentVersion > DBTable.CURRENT_DB_VERSION)
            {
                _logger.LogCritical("⚠️ 数据库版本 [{DbVersion}] 高于程序版本 [{AppVersion}]，可能存在兼容性问题",
                    currentVersion, DBTable.CURRENT_DB_VERSION);
                throw new InvalidOperationException(
                    $"数据库文件 '{_dbFilePath}' 处于不一致的状态，本地数据库文件内部版本 [{currentVersion}] 高于程序版本 [{DBTable.CURRENT_DB_VERSION}] ，可能存在兼容性问题。" +
                    "为防止数据丢失，Bot将立即关闭。" +
                    "请手动检查数据库文件。如果确认要重新开始，请删除该文件后重启Bot。"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 数据库迁移过程中发生错误");
            throw;
        }
        
        return hasUpdates;
    }
    
    public void Dispose()
    {
        _logger.LogInformation("Disposing DatabaseService and closing anchor connection.");
        _anchorConnection.Close();
        _anchorConnection.Dispose();
        // 清理连接池，确保共享的内存数据库被彻底释放
        SqliteConnection.ClearAllPools();
    }
}
