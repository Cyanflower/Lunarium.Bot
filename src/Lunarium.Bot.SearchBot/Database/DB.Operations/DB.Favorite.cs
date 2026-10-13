// NuGet 包依赖
using Npgsql;
using Dapper;

using Lunarium.Bot.Extensions;

using Lunarium.Bot.SearchBot.Models;
using Lunarium.Bot.SearchBot.Models.Database;

namespace Lunarium.Bot.SearchBot.Database;

public partial class DatabaseService
{
    public async Task<List<FavoriteTypeInfo>> GetFavoriteTypeAsync(ulong userId)
    {
        const string SqlCommand = $"""
            SELECT
                user_id AS UserId,
                favorite_id AS FavoriteId,
                type_name AS TypeName,
                created_at AS CreatedAt
            FROM user_favorite_types
            WHERE user_id = @UserId;
            ORDER BY created_at ASC; 
            """;
        var parameters = new
        {
            UserId = userId.ToDbId()
        };
        try
        {
            await using var connection = GetConnection();
            var result = await connection.QueryAsync<FavoriteTypeInfo>(SqlCommand, parameters);
            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetFavoriteTypeAsync Exception");
            throw;
        }
    }

    public async Task<FavoriteInfo?> CheckFavoriteExistAsync(ulong userId, ulong threadId)
    {
        const string SqlCommand = $"""
            SELECT
                user_id AS UserId,
                thread_id AS ThreadId,
                favorite_type_name AS FavoriteTypeName,
                allow_push AS AllowPush,
                added_at AS AddedAt
            FROM user_favorite_posts
            WHERE user_id = @UserId AND thread_id = @ThreadId;
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            ThreadId = threadId.ToDbId()
        };
        try
        {
            await using var connection = GetConnection();
            var result = await connection.QuerySingleOrDefaultAsync<FavoriteInfo>(SqlCommand, parameters);

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CheckFavoriteExistAsync Exception");
            throw;
        }
    }

    public async Task<DeleteResult> RemoveFavoriteAsync(NpgsqlTransaction transaction, ulong userId, ulong threadId)
    {
        const string SqlCommand = $"""
            DELETE FROM user_favorite_posts
            WHERE user_id = @UserId AND thread_id = @ThreadId;
            """;
        var parameters = new
        {
            UserId = userId.ToDbId(),
            ThreadId = threadId.ToDbId()
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
            _logger.Error(ex, "RemoveFavoriteAsync Exception");
            throw;
        }
    }

    public async Task<List<FavoriteInfo>> QueryUserFavoritesAsync(ulong userId, long? favoriteAs)
    {
        const string SqlCommand = $"""
            SELECT
                user_id AS UserId,
                thread_id AS ThreadId,
                favorite_type_name AS FavoriteTypeName,
                allow_push AS AllowPush,
                added_at AS AddedAt
            FROM user_favorite_posts
            WHERE
                user_id = @UserId 
                AND (@FavoriteAs IS NULL OR favorite_as = @FavoriteAs)
            ORDER BY added_at DESC;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            FavoriteAs = favoriteAs
        };

        try
        {
            await using var connection = GetConnection();
            var result = await connection.QueryAsync<FavoriteInfoDb>(SqlCommand, parameters);
            return result.Select(r => r.ToDomain()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "QueryUserFavoritesAsync Exception");
            throw;
        }
    }

    public async Task<FavoriteTypeInfo?> CreateFavoriteTypeAsync(NpgsqlTransaction transaction, ulong userId, string typeName)
    {
        const string SqlCommand = $"""
            INSERT INTO user_favorite_types (
                user_id,
                type_name,
                created_at
            )
            VALUES (
                @UserId,
                @TypeName,
                NOW()
            )
            ON CONFLICT (user_id, type_name) DO NOTHING
            RETURNING 
                user_id AS UserId,
                favorite_id AS FavoriteId, 
                type_name AS TypeName, 
                created_at AS CreatedAt;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            TypeName = typeName
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            var result = await transaction.Connection.QuerySingleOrDefaultAsync<FavoriteTypeInfoDb>(SqlCommand, parameters);

            return result?.ToDomain();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CreateFavoriteTypeAsync Exception");
            throw;
        }
    }

    public async Task<FavoriteInfo?> AddUserFavoriteAsync(NpgsqlTransaction transaction, FavoriteInfoDb favoriteDb)
    {
        const string SqlCommand = $"""
            INSERT INTO user_favorite_posts (
                user_id,
                thread_id,
                favorite_as,
                favorite_type_name, 
                allow_push,
                added_at
            )
            VALUES (
                @UserId,
                @ThreadId,
                @FavoriteAs,
                -- 强一致性设计：直接使用子查询获取分类名称，忽略传入的 FavoriteTypeName
                (SELECT type_name FROM user_favorite_types WHERE favorite_id = @FavoriteAs), 
                @AllowPush,
                NOW()
            )
            ON CONFLICT (user_id, thread_id) DO NOTHING
            RETURNING 
                user_id AS UserId,
                thread_id AS ThreadId,
                favorite_as AS FavoriteAs,
                favorite_type_name AS FavoriteTypeName,
                allow_push AS AllowPush,
                added_at AS AddedAt;
            """;

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            // QuerySingleOrDefaultAsync:
            // 1. 插入成功 -> RETURNING 返回 1 行 -> 映射为对象
            // 2. 冲突 (DO NOTHING) -> RETURNING 不返回行 -> 返回 null
            var result = await transaction.Connection.QuerySingleOrDefaultAsync<FavoriteInfoDb>(SqlCommand, favoriteDb);

            return result?.ToDomain();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // foreign_key_violation
        {
            _logger.Warning("AddUserFavoriteAsync FK Violation: User {0} tried to add to non-existent folder {1}", favoriteDb.UserId, favoriteDb.FavoriteAs);
            throw new InvalidOperationException("指定的收藏夹不存在", ex);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "AddUserFavoriteAsync Exception");
            throw;
        }
    }

    public async Task<string?> GetFavoriteTypeNameAsync(NpgsqlTransaction transaction, ulong userId, long favoriteId)
    {
        const string sql = """
            SELECT type_name 
            FROM user_favorite_types 
            WHERE user_id = @UserId AND favorite_id = @FavoriteId;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            FavoriteId = favoriteId
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            return await transaction.Connection.QuerySingleOrDefaultAsync<string>(sql, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetFavoriteTypeNameAsync Exception");
            throw;
        }
    }

    public async Task<int> MoveFavoritePostsAsync(NpgsqlTransaction transaction, ulong userId, long oldFavoriteId, long newFavoriteId)
    {
        const string sql = """
            UPDATE user_favorite_posts 
            SET 
                favorite_as = @NewFavoriteId,
                added_at = NOW()
            WHERE 
                user_id = @UserId AND favorite_as = @OldFavoriteId;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            OldFavoriteId = oldFavoriteId,
            NewFavoriteId = newFavoriteId
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            return await transaction.Connection.ExecuteAsync(sql, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MoveFavoritePostsAsync Exception");
            throw;
        }
    }

    public async Task<int> DeleteFavoriteCategoryAsync(NpgsqlTransaction transaction, ulong userId, long favoriteId)
    {
        const string sql = """
            DELETE FROM user_favorite_types 
            WHERE user_id = @UserId AND favorite_id = @FavoriteId;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            FavoriteId = favoriteId
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            return await transaction.Connection.ExecuteAsync(sql, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DeleteFavoriteCategoryAsync Exception");
            throw;
        }
    }

    public async Task<bool> CheckOtherFavoriteTypeExistsAsync(NpgsqlTransaction transaction, ulong userId, string typeName, long excludeId)
    {
        const string sql = """
            SELECT 1 
            FROM user_favorite_types 
            WHERE user_id = @UserId 
              AND type_name = @TypeName 
              AND favorite_id != @ExcludeId
            LIMIT 1;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            TypeName = typeName,
            ExcludeId = excludeId
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            var result = await transaction.Connection.ExecuteScalarAsync<int?>(sql, parameters, transaction);
            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// 更新收藏夹名称
    /// 注意：数据库触发器 trigger_sync_favorite_type_name 会自动同步更新 user_favorite_posts 表
    /// </summary>
    public async Task<int> RenameFavoriteTypeAsync(NpgsqlTransaction transaction, ulong userId, long favoriteId, string newTypeName)
    {
        const string sql = """
            UPDATE user_favorite_types 
            SET type_name = @NewTypeName 
            WHERE user_id = @UserId AND favorite_id = @FavoriteId;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            FavoriteId = favoriteId,
            NewTypeName = newTypeName
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            return await transaction.Connection.ExecuteAsync(sql, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }
}