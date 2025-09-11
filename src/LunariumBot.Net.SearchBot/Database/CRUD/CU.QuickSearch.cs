// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    // SQL模板: 插入或更新快捷搜索命令
    const string quickSearchUpsertSqlCommand = """
        INSERT INTO user_fast_search_command (user_id, command_name, criteria_json)
        VALUES (@UserId, @CommandName, @CriteriaJson)
        ON CONFLICT(user_id, command_name) DO UPDATE SET
            criteria_json = excluded.criteria_json;
        """;

    /// <summary>
    /// 添加或更新用户的快捷搜索命令。
    /// </summary>
    /// <param name="command">要保存的命令对象</param>
    /// <returns>影响的行数</returns>
    public async Task<int> InsertQuickSearchAsync(QuickSearchCommandModel command)
    {
        using var connection = new SqliteConnection(_connectionString);
        var affectedRows = await connection.ExecuteAsync(quickSearchUpsertSqlCommand, command);
        _logger.Info("用户 {command.UserId} 保存了快捷命令 '{command.CommandName}', 影响行数: {affectedRows}");
        return affectedRows;
    }
}