// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    const string addBlockedAuthorSql = "INSERT OR IGNORE INTO user_blocked_authors (user_id, blocked_user_id, added_at) VALUES (@UserId, @BlockedUserId, @AddedAt);";
    const string removeBlockedAuthorSql = "DELETE FROM user_blocked_authors WHERE user_id = @UserId AND blocked_user_id = @BlockedUserId;";
    const string getBlockedAuthorsSql = "SELECT blocked_user_id FROM user_blocked_authors WHERE user_id = @UserId;";
    const string isAuthorBlockedSql = "SELECT 1 FROM user_blocked_authors WHERE user_id = @UserId AND blocked_user_id = @BlockedUserId LIMIT 1;";

    /// <summary>
    /// 添加一个屏蔽作者记录。
    /// </summary>
    /// <returns>如果成功添加返回 true，如果已存在则返回 false。</returns>
    public async Task<bool> AddBlockedAuthorAsync(ulong userId, ulong blockedUserId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(addBlockedAuthorSql, new { UserId = userId, BlockedUserId = blockedUserId, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            _logger.Error("AddBlockedAuthorAsync Ex:", ex);
            return false;
        }
    }

    /// <summary>
    /// 移除一个屏蔽作者记录。
    /// </summary>
    /// <returns>如果成功移除返回 true，如果记录不存在则返回 false。</returns>
    public async Task<bool> RemoveBlockedAuthorAsync(ulong userId, ulong blockedUserId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(removeBlockedAuthorSql, new { UserId = userId, BlockedUserId = blockedUserId });
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            _logger.Error("RemoveBlockedAuthorAsync Ex:", ex);
            return false;
        }
    }

    /// <summary>
    /// 获取一个用户的所有屏蔽作者ID。
    /// </summary>
    public async Task<List<ulong>> GetBlockedAuthorsAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var result = await connection.QueryAsync<ulong>(getBlockedAuthorsSql, new { UserId = userId });
            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("GetBlockedAuthorsAsync Ex:", ex);
            return new List<ulong>();
        }
    }
    
    /// <summary>
    /// 检查一个作者是否被用户屏蔽。
    /// </summary>
    public async Task<bool> IsAuthorBlockedAsync(ulong userId, ulong blockedUserId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var result = await connection.QueryFirstOrDefaultAsync<int?>(isAuthorBlockedSql, new { UserId = userId, BlockedUserId = blockedUserId });
            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.Error("IsAuthorBlockedAsync Ex:", ex);
            return false;
        }
    }
}