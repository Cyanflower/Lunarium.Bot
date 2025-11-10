// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    // SQL模板: 删除单条用户收藏记录
    const string favoriteDeleteOneSqlCommand = "DELETE FROM user_favorite_posts WHERE user_id = @UserId AND thread_id = @ThreadId;";
    // SQL模板: 删除一个收藏夹类别
    const string favoriteCategoryDeleteSqlCommand = "DELETE FROM user_favorite_types WHERE user_id = @UserId AND type_name = @CategoryName;";
    // SQL模板: 删除一个收藏夹下的所有帖子收藏记录
    const string favoritePostsByCategoryDeleteSqlCommand = "DELETE FROM user_favorite_posts WHERE user_id = @UserId AND favorite_as = @CategoryName;";
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
            
            _logger.Info("用户 {userId} 的帖子 {threadId} 收藏已移除, 影响行数: {affectedRows}。", userId, threadId, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.Error("RemoveFavoriteAsync Ex:", ex);
        }
    }

    /// <summary>
    /// 删除用户的某个收藏夹
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="categoryName">收藏夹名称</param>
    /// <param name="deletePosts">是否同时删除该收藏夹下的帖子</param>
    /// <returns>操作影响的总行数</returns>
    public async Task<int> RemoveFavoriteCategoryAsync(ulong userId, string categoryName, bool deletePosts)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            int totalAffectedRows = 0;

            // 总是删除收藏夹类别本身
            totalAffectedRows += await connection.ExecuteAsync(favoriteCategoryDeleteSqlCommand, new { UserId = userId, CategoryName = categoryName }, transaction);

            // 根据 deletePosts 参数决定如何处理帖子
            if (deletePosts)
            {
                // 移除收藏: 从 user_favorite_posts 删除相关记录
                totalAffectedRows += await connection.ExecuteAsync(favoritePostsByCategoryDeleteSqlCommand, new { UserId = userId, CategoryName = categoryName }, transaction);
                 _logger.Info("用户 {userId} 删除收藏夹 '{categoryName}' 并移除了其中的帖子。", userId, categoryName);
            }
            else
            {
                // 保留收藏: 将 favorite_as 更新为 NULL
                const string sql = "UPDATE user_favorite_posts SET favorite_as = NULL WHERE user_id = @UserId AND favorite_as = @CategoryName;";
                totalAffectedRows += await connection.ExecuteAsync(sql, new { UserId = userId, CategoryName = categoryName }, transaction);
                _logger.Info("用户 {userId} 删除收藏夹 '{categoryName}' 并将帖子保留至未分类。", userId, categoryName);
            }

            await transaction.CommitAsync();
            return totalAffectedRows;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "删除用户 {userId} 的收藏夹 '{categoryName}' 时出错。", userId, categoryName);
            return 0;
        }
    }
}