// NuGet 包依赖
using Npgsql;
using Dapper;
using System.Data;
using System.Text.Json;

// 解决方案内引用项目
using Lunarium.Bot.Logger;

// 项目内命名空间
using Lunarium.Bot.DataCenter.Models;

namespace Lunarium.Bot.DataCenter.Database;

/// <summary>
/// 负责与 PostgreSQL 数据库进行交互的服务。
/// 管理连接、按需执行数据库结构迁移。
/// </summary>
public partial class DatabaseService : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly string _connectionString;
    internal readonly string _migrationsPath;

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

        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new JsonTypeHandler<List<string>>());

        // 定义迁移脚本的存放路径
        _migrationsPath = Path.Combine(AppContext.BaseDirectory, "DBMigrations");

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

    public class JsonTypeHandler<T> : SqlMapper.TypeHandler<Jsonb<T>>
    {
        public override void SetValue(IDbDataParameter parameter, Jsonb<T> value)
        {
            // 要序列化的是 value.Value (内部数据)，而不是 value (包装)
            // 注意：如果是 struct，value 本身不会为 null，但 value.Value 可能是 null
            if (value.Value is null)
            {
                parameter.Value = DBNull.Value;
            }
            else
            {
                parameter.Value = JsonSerializer.Serialize(value.Value);
            }

            // 显式指定 jsonb
            ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
        }

        public override Jsonb<T> Parse(object value)
        {
            // 反序列化出 T，然后包进 Json<T>
            if (value is string json)
            {
                var innerValue = JsonSerializer.Deserialize<T>(json);
                return new Jsonb<T>(innerValue);
            }

            // 如果数据库是 null，或者类型不对，返回默认值
            return default; 
        }
    }
}