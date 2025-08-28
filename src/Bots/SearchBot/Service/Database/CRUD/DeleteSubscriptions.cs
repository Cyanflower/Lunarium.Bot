// src/Bots/SearchBot/Service/Database/CRUD/DeleteSubscriptions.cs
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    // SQL模板: 删除单条用户订阅记录
    const string subscriptionDeleteOneSqlCommand = "DELETE FROM user_subscriptions_post WHERE user_id = @UserId AND thread_id = @ThreadId;";
    // SQL模板: 删除用户所有订阅记录
    const string subscriptionDeleteAllByUserSqlCommand = "DELETE FROM user_subscriptions_post WHERE user_id = @UserId;";
    // ✨ 新增SQL模板
    // SQL模板: 清空用户的所有推送记录
    const string pushListDeleteAllByUserSqlCommand = "DELETE FROM user_push_list WHERE user_id = @UserId;";

    /// <summary>
    /// 移除指定用户的某一个帖子订阅。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="threadId">帖子ID</param>
    public async Task RemoveSubscriptionAsync(ulong userId, ulong threadId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            int affectedRows = await connection.ExecuteAsync(subscriptionDeleteOneSqlCommand, new { UserId = userId, ThreadId = threadId });
            
            _logger.LogInformation("用户 {UserId} 的帖子 {ThreadId} 订阅已移除, 影响行数: {AffectedRows}。", userId, threadId, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("RemoveSubscriptionAsync: {ex}", ex);
        }

    }

    /// <summary>
    /// 清空指定用户的所有帖子订阅。
    /// </summary>
    /// <param name="userId">用户ID</param>
    public async Task ClearSubscriptionsAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            int affectedRows = await connection.ExecuteAsync(subscriptionDeleteAllByUserSqlCommand, new { UserId = userId });
            
            _logger.LogInformation("已清空用户 {UserId} 的所有帖子订阅, 共移除 {AffectedRows} 条记录。", userId, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("ClearSubscriptionsAsync: {ex}", ex);
        }

    }

    /// <summary>
    /// 清空指定用户的所有更新推送记录。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>受影响的行数</returns>
    public async Task<int> ClearPushListAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            int affectedRows = await connection.ExecuteAsync(pushListDeleteAllByUserSqlCommand, new { UserId = userId });

            _logger.LogInformation("已清空用户 {UserId} 的所有更新推送, 共移除 {AffectedRows} 条记录。", userId, affectedRows);
            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.LogError("ClearPushListAsync {ex}", ex);
            return 0;
        }
    }
}