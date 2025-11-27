// NuGet 包依赖
using Npgsql;
using Dapper;

using LunariumBot.Net.Extensions;

using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Models.Database;

namespace LunariumBot.Net.SearchBot.Database;

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
            _logger.Error("GetFavoriteTypeAsync Exception", ex);
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
            _logger.Error("CheckFavoriteExistAsync Exception", ex);
            throw;
        }
    }

    public async Task<DeleteResult> RemoveFavoriteAsync(ulong userId, ulong threadId)
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
            await using var connection = GetConnection();
            var affectedRows = await connection.ExecuteAsync(SqlCommand, parameters);

            if (affectedRows > 0)
            {
                return DeleteResult.Success;
            }
            return DeleteResult.NoExists;
        }
        catch (Exception ex)
        {
            _logger.Error("RemoveFavoriteAsync Exception", ex);
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
            var result = await connection.QueryAsync<FavoriteInfo>(SqlCommand, parameters);
            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("QueryUserFavoritesAsync Exception", ex);
            throw;
        }
    }

    public async Task<FavoriteTypeInfo?> CreateFavoriteTypeAsync(ulong userId, string typeName)
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
            await using var connection = GetConnection();

            var result = await connection.QuerySingleOrDefaultAsync<FavoriteTypeInfoDb>(SqlCommand, parameters);

            return result?.ToDomain();
        }
        catch (Exception ex)
        {
            _logger.Error("CreateFavoriteTypeAsync Exception", ex);
            throw;
        }
    }

    public async Task<FavoriteInfo?> AddUserFavoriteAsync(FavoriteInfoDb favoriteDb)
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
            await using var connection = GetConnection();
            
            // QuerySingleOrDefaultAsync:
            // 1. 插入成功 -> RETURNING 返回 1 行 -> 映射为对象
            // 2. 冲突 (DO NOTHING) -> RETURNING 不返回行 -> 返回 null
            var result = await connection.QuerySingleOrDefaultAsync<FavoriteInfoDb>(SqlCommand, favoriteDb);
            
            return result?.ToDomain();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // foreign_key_violation
        {
            _logger.Warning("AddUserFavoriteAsync FK Violation: User {0} tried to add to non-existent folder {1}", favoriteDb.UserId, favoriteDb.FavoriteAs);
            throw new InvalidOperationException("指定的收藏夹不存在", ex);
        }
        catch (Exception ex)
        {
            _logger.Error("AddUserFavoriteAsync Exception", ex);
            throw;
        }
    }
}