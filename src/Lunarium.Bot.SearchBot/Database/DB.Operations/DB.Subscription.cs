// NuGet 包依赖
using Npgsql;
using Dapper;

using Lunarium.Bot.Extensions;

using Lunarium.Bot.SearchBot.Models;
using Lunarium.Bot.SearchBot.Models.Database;

namespace Lunarium.Bot.SearchBot.Database;

public partial class DatabaseService
{
    public async Task<SubscriptionData?> CheckSubscriptionExistAsync(ulong userId, ulong authorId)
    {
        const string SqlCommand = $"""
            SELECT
                user_id AS UserId,
                author_id AS AuthorId,
                added_at AS AddedAt
            FROM user_subscriptions_author
            WHERE user_id = @UserId AND author_id = @AuthorId;
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            AuthorId = authorId.ToDbId()
        };
        try
        {
            await using var connection = GetConnection();
            var result = await connection.QuerySingleOrDefaultAsync<SubscriptionDataDb>(SqlCommand, parameters);

            return result?.ToDomain();
        }
        catch (Exception ex)
        {
            _logger.Error("CheckSubscriptionExistAsync Exception", ex);
            throw;
        }
    }

    public async Task<InsertResult> AddSubscriptionAsync(NpgsqlTransaction transaction, ulong userId, ulong authorId)
    {
        const string SqlCommand = $"""
            INSERT INTO user_subscriptions_author (
                user_id,
                author_id,
                added_at
            )
            VALUES (
                @UserId,
                @AuthorId,
                NOW()
            )
            ON CONFLICT (user_id, thread_id) DO NOTHING;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            AuthorId = authorId.ToDbId()
        };
        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            var affectedRows = await transaction.Connection.ExecuteAsync(SqlCommand, parameters);

            if (affectedRows > 0)
            {
                return InsertResult.Inserted;
            }
            return InsertResult.AlreadyExists;
        }
        catch (Exception ex)
        {
            _logger.Error("AddSubscriptionAsync Exception", ex);
            throw;
        }
    }
    
    public async Task<DeleteResult> RemoveSubscriptionAsync(NpgsqlTransaction transaction, ulong userId, ulong authorId)
    {
        const string SqlCommand = $"""
            DELETE FROM user_subscriptions_author
            WHERE user_id = @UserId AND author_id = @AuthorId;
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            AuthorId = authorId.ToDbId()
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
            _logger.Error("RemoveSubscriptionAsync Exception", ex);
            throw;
        }
    }

    public async Task<List<SubscriptionData>> QueryUserSubscriptionsAsync(ulong userId)
    {
        const string SqlCommand = $"""
            SELECT
                user_id AS UserId,
                author_id AS AuthorId,
                added_at AS AddedAt
            FROM user_subscriptions_author
            WHERE user_id = @UserId
            ORDER BY added_at DESC;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId()
        };

        try
        {
            await using var connection = GetConnection();
            var result = await connection.QueryAsync<SubscriptionDataDb>(SqlCommand, parameters);
            return result.Select(r => r.ToDomain()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("QueryUserFavoritesAsync Exception", ex);
            throw;
        }
    }
}