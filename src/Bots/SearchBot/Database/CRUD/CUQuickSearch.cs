// src/Bots/SearchBot/Service/Database/CRUD/CUQuickSearch.cs
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SearchBot.Model;

namespace SearchBot.Database;

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
        _logger.LogInformation("用户 {UserId} 保存了快捷命令 '{CommandName}', 影响行数: {rows}", command.UserId, command.CommandName, affectedRows);
        return affectedRows;
    }
}