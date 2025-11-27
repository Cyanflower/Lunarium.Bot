// NuGet 包依赖
using Npgsql;
using Dapper;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Models;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.DataCenter.Models.Database;

namespace LunariumBot.Net.DataCenter.Database;

/// <summary>
/// 数据分析相关的查询方法
/// </summary>
public partial class DatabaseService
{
    public async Task<string?> GetDatabaseSize()
    {
        // 查询所有数据库总大小
        const string SqlCommand = """
            SELECT pg_size_pretty(SUM(pg_database_size(datname))::bigint) AS total_size
            FROM pg_database;
            """;
        // // 同时看到各个数据库大小和总和
        // const string SqlCommand = """
        //     SELECT 
        //         datname AS database_name,
        //         pg_size_pretty(pg_database_size(datname)) AS size
        //     FROM pg_database
        //     UNION ALL
        //     SELECT 
        //         'TOTAL' AS database_name,
        //         pg_size_pretty(SUM(pg_database_size(datname))::bigint) AS size
        //     FROM pg_database
        //     ORDER BY database_name;
        //     """;
        // // 查询当前数据库大小
        // const string SqlCommand = """
        //     SELECT pg_size_pretty(pg_database_size(current_database()));
        //     """;

        try
        {
            using var connection = GetConnection();
            return await connection.ExecuteScalarAsync<string?>(SqlCommand);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetDatabaseSize: 查询数据库大小信息失败");
            return "❌ GetDatabaseSize: 查询数据库大小信息失败";
        }
    } 

    public async Task<string?> GetDatabaseState()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            using var connection = GetConnection();
            await connection.OpenAsync();
            
            // // 测量连接延迟
            // var connectionLatency = stopwatch.ElapsedMilliseconds;
            
            // // 测量简单查询延迟（ping）
            // stopwatch.Restart();
            // await connection.ExecuteScalarAsync<int>("SELECT 1");
            // var pingLatency = stopwatch.ElapsedMilliseconds;
            
            // 获取当前数据库名
            const string dbNameSql = "SELECT current_database()";
            var dbName = await connection.ExecuteScalarAsync<string?>(dbNameSql);
            
            // 查询当前数据库大小
            const string currentDbSizeSql = "SELECT pg_size_pretty(pg_database_size(current_database()))";
            var currentDbSize = await connection.ExecuteScalarAsync<string?>(currentDbSizeSql);
            
            // 查询所有数据库总大小
            const string totalSizeSql = """
                SELECT pg_size_pretty(SUM(pg_database_size(datname))::bigint)
                FROM pg_database;
                """;
            var totalSize = await connection.ExecuteScalarAsync<string?>(totalSizeSql);
            
            // 获取连接数
            const string connectionCountSql = """
                SELECT count(*) 
                FROM pg_stat_activity 
                WHERE datname = current_database();
                """;
            var activeConnections = await connection.ExecuteScalarAsync<int>(connectionCountSql);
            
            stopwatch.Stop();
            // var totalTime = stopwatch.ElapsedMilliseconds;
            
            // // 格式化输出
            // var result = $"""
            //     📊 数据库状态信息
            //     ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            //     🔌 连接延迟:      {connectionLatency,6} ms
            //     ⚡ Ping延迟:      {pingLatency,6} ms
            //     ⏱️ 总查询时间:    {totalTime,6} ms
            //     ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            //     📁 当前数据库:    {dbName}
            //     💾 当前库大小:    {currentDbSize}
            //     🗄️ 所有库总大小:  {totalSize}
            //     👥 活跃连接数:    {activeConnections}
            //     ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            //     """;
            var result = $"""
                📊 数据库状态信息
                ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                📁 当前数据库:    {dbName}
                💾 当前库大小:    {currentDbSize}
                🗄️ 所有库总大小:  {totalSize}
                👥 活跃连接数:    {activeConnections}
                ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                """;
            _logger.Info("✅ GetDatabaseState: 查询成功");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetDatabaseState: 查询数据库状态信息失败");
            return $"❌ GetDatabaseState 失败: {ex.Message}";
        }
    }
}
