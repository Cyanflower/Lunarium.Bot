// // NuGet 包依赖
// using Microsoft.Data.Sqlite;
// using Dapper;

// // 解决方案内引用项目
// using LunariumBot.Net.Logger;

// namespace LunariumBot.Net.SearchBot.Database;

// public partial class DatabaseService
// {
//     // SQL模板: 删除用户的单个快捷搜索命令
//     const string quickSearchDeleteOneSqlCommand = "DELETE FROM user_fast_search_command WHERE user_id = @UserId AND command_name = @CommandName;";
    
//     /// <summary>
//     /// 移除用户的指定快捷搜索命令。
//     /// </summary>
//     /// <param name="userId">用户ID</param>
//     /// <param name="commandName">命令名称</param>
//     /// <returns>影响的行数</returns>
//     public async Task<int> RemoveQuickSearchAsync(ulong userId, string commandName)
//     {
//         using var connection = new SqliteConnection(_connectionString);
//         var affectedRows = await connection.ExecuteAsync(quickSearchDeleteOneSqlCommand, new { UserId = userId, CommandName = commandName });
        
//         _logger.Info("用户 {userId} 删除了快捷命令 '{commandName}', 影响行数: {affectedRows}", userId, commandName, affectedRows);
//         return affectedRows;
//     }
// }