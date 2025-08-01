using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SearchBot.Utils;
using System.Text;
using System.Threading.Tasks;
using SearchBot.ThreadFetcher;

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
            LoadDB();
        }
        else
        {
            _logger.LogInformation("📄 未发现持久化数据库文件，执行初始化。");
            InitializeDB();
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
            using var command = connection.CreateCommand();
            
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            command.CommandText = $"UPDATE db_timestamp SET timestamp = {timestamp}";
            command.ExecuteNonQuery();
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
            connection.OpenAsync();
            using var command = connection.CreateCommand();
            // 合并所有建表语句，一次性执行
            command.CommandText = DBTable.GetCreateAllTablesSql();
            command.Parameters.AddWithValue("@timestamp", timestamp);
            command.ExecuteNonQuery();
            _logger.LogInformation("✅ 数据库表创建完成。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 初始化数据库表时发生错误。");
            throw; // 抛出异常，让上层知道初始化失败了
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
            connection.OpenAsync();
            
            // 创建一个到磁盘文件的连接
            using var fileDbConnection = new SqliteConnection($"Data Source={_dbFilePath}");
            fileDbConnection.OpenAsync();

            // 将文件数据库的内容备份(恢复)到内存数据库
            fileDbConnection.BackupDatabase(connection);
            
            _logger.LogInformation("✅ 数据库已成功从文件加载到内存。");

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT timestamp FROM db_timestamp";
            // 表 db_timestamp timestamp 为 INTEGER
            var db_timestamp = command.ExecuteScalar() ?? 0;
            
            var utcTimePoint = DateTimeOffset.FromUnixTimeSeconds((long)db_timestamp);
            var timeInUtcPlus8 = utcTimePoint.ToOffset(TimeSpan.FromHours(8));

            _logger.LogInformation("数据库保存时间: {DbTime}", timeInUtcPlus8.ToString("yyyy-MM-dd HH:mm:ss"));
            _logger.LogInformation("当前UTC+8时间: {CurrentTime}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (timestamp - (long)db_timestamp > 10 * 60)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            // 如果加载失败，可能是文件损坏或不是一个有效的数据库文件。
            // 在这种情况下，记录错误，并重新初始化一个空的数据库。
            _logger.LogError(ex, "❌ 从文件加载数据库时发生错误。将重新初始化一个空数据库。");
            InitializeDB(); // 加载失败，则创建一个新的空表结构
            return true;
        }
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