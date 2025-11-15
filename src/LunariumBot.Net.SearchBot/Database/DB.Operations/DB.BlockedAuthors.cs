// NuGet 包依赖
using Npgsql;
using Dapper;

using LunariumBot.Net.Extensions;

using LunariumBot.Net.SearchBot.Model;
using LunariumBot.Net.SearchBot.Model.Database;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    /// <summary>
    /// 添加一个屏蔽作者记录。
    /// </summary>
    /// <returns>如果成功添加返回 true，如果已存在则返回 false。</returns>
    public async Task<InsertResult> AddBlockedAuthorAsync(NpgsqlTransaction transaction, ulong userId, ulong blockedUserId)
    {
        const string SqlCommand = $"""
            INSERT INTO user_blocked_authors (
                user_id,
                blocked_author_id,
                added_at
            )
            VALUES
            (
                @UserId,
                @BlockedAuthorId,
                NOW()
            );
            ON CONFLICT (user_id, blocked_author_id) DO NOTHING
            RETURNING '{InsertedStatus}';
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            BlockedAuthorId = blockedUserId.ToDbId()
        };
        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            var result = await transaction.Connection.ExecuteScalarAsync<string>(SqlCommand, parameters);
            if (string.Equals(result, "INSERTED", StringComparison.Ordinal))
            {
                return InsertResult.Inserted;
            }
            return InsertResult.AlreadyExists;
        }
        catch (Exception ex)
        {
            _logger.Error("AddBlockedAuthorAsync Ex:", ex);
            throw;
        }
    }

    /// <summary>
    /// 查询用户对某一作者的屏蔽情况
    /// </summary>
    /// <param name="transaction"></param>
    /// <param name="userId"></param>
    /// <param name="blockedUserId"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<DateTimeOffset> QueryUserBlockedAuthorInfoAsync(ulong userId, ulong blockedUserId)
    {
        const string SqlCommand = $"""
            SELECT added_at
            FROM user_blocked_authors
            WHERE user_id = @UserId AND blocked_author_id = @BlockedAuthorId;
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            BlockedAuthorId = blockedUserId.ToDbId(),
        };
        try
        {
            var connection = GetConnection();
            var result = await connection.QueryFirstAsync<DateTimeOffset>(SqlCommand, parameters);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error("AddBlockedAuthorAsync Ex:", ex);
            throw;
        }
    }

    /// <summary>
    /// 移除一个屏蔽作者记录。
    /// </summary>
    /// <returns>如果成功移除返回 true，如果记录不存在则返回 false。</returns>
    public async Task<DeleteResult> RemoveBlockedAuthorAsync(NpgsqlTransaction transaction, ulong userId, ulong blockedUserId)
    {
        const string SqlCommand = $"""
            DELETE FROM user_blocked_authors
            WHERE user_id = @UserId AND blocked_author_id = @BlockedAuthorId;
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            BlockedAuthorId = blockedUserId.ToDbId(),
        };
        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            var affectedRows = await transaction.Connection.ExecuteAsync(SqlCommand, parameters);
            if (affectedRows > 0)
            {
                return DeleteResult.Success;
            }
            return DeleteResult.NoExists;
        }
        catch (Exception ex)
        {
            _logger.Error("AddBlockedAuthorAsync Ex:", ex);
            throw;
        }
    }

    /// <summary>
    /// 获取一个用户的所有屏蔽作者ID。
    /// </summary>
    public async Task<List<BlackedInfo>> QueryUserAllBlockedAsync(ulong userId)
    {
        const string SqlCommand = $"""
            SELECT
                blocked_author_id AS BlockedAuthorId,
                added_at AS AddedAt
            FROM user_blocked_authors
            WHERE user_id = @UserId
            """;
        try
        {
            var connection = GetConnection();
            var result = await connection.QueryAsync<BlackedInfoDb>(SqlCommand, new { UserId = userId.ToDbId() });
            return result.Select(info => info.ToDomain()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("GetBlockedAuthorsAsync Ex:", ex);
            throw;
        }
    }
}