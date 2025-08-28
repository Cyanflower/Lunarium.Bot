// src/Bots/SearchBot/Service/Database/CRUD/DeleteFavorite.cs
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    // SQL模板: 删除单条用户收藏记录
    const string favoriteDeleteOneSqlCommand = "DELETE FROM user_favorite_posts WHERE user_id = @UserId AND thread_id = @ThreadId;";
    // SQL模板: 删除用户所有收藏记录
    const string favoriteDeleteAllByUserSqlCommand = "DELETE FROM user_favorite_posts WHERE user_id = @UserId;";
    
    /// <summary>
    /// 移除指定用户的某一个帖子收藏。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="threadId">帖子ID</param>
    public async Task RemoveFavoriteAsync(ulong userId, ulong threadId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            int affectedRows = await connection.ExecuteAsync(favoriteDeleteOneSqlCommand, new { UserId = userId, ThreadId = threadId });
            
            _logger.LogInformation("用户 {UserId} 的帖子 {ThreadId} 收藏已移除, 影响行数: {AffectedRows}。", userId, threadId, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("RemoveFavoriteAsync {ex}", ex);
        }
    }

    /// <summary>
    /// 清空指定用户的所有帖子收藏。
    /// </summary>
    /// <param name="userId">用户ID</param>
    public async Task ClearFavoritesAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            int affectedRows = await connection.ExecuteAsync(favoriteDeleteAllByUserSqlCommand, new { UserId = userId });
            
            _logger.LogInformation("已清空用户 {UserId} 的所有帖子收藏, 共移除 {AffectedRows} 条记录。", userId, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("ClearFavoritesAsync {ex}", ex);
        }
    }
}