using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

using Dapper;
using Discord;

using SearchBot.Model;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // SQL模板: 插入或忽略用户收藏帖子关系表 (INSERT OR IGNORE)
    const string userFavoritePostsUpsertSqlCommand = """
        INSERT OR IGNORE INTO user_favorite_posts (user_id, thread_id, favorite_as, added_at)
        VALUES (@UserId, @ThreadId, @Favorite, @AddedAt)
        ON CONFLICT(user_id, thread_id) DO UPDATE SET
            favorite_as = excluded.favorite_as,
            added_at = excluded.added_at;
        """;
    // SQL模板: 插入或忽略用户收藏帖子类型表 (INSERT OR IGNORE)
    const string userFavoriteTypeInsertSqlCommand = """
        INSERT OR IGNORE INTO user_favorite_types (user_id, type_name)
        VALUES (@UserId, @TypeName);
        """;
    /// <summary>
    /// 更新(插入或更新)单个用户的帖子收藏。
    /// </summary>
    /// <param name="user">待处理的用户。</param>
    /// <param name="thread">待处理的帖子对象。</param>
    public async Task InsertUserFavoritePostAsync(ulong userId, IThreadChannel thread, string? favorite = null)
    {
        try
        {
            // 校验: 用户或帖子对象为空
            if (thread == null)
            {
                _logger.LogWarning("用户或帖子对象为空, 操作跳过。");
                return;
            }
            var threadFallbackData = new
            {
                ThreadId = thread.Id,
                Title = thread.Name,
                JumpUrl = $"https://discord.com/channels/{thread.Guild.Id}/{thread.Id}",
            };
            // 准备: 构造收藏关系数据
            var favoriteData = new { UserId = userId, ThreadId = thread.Id, Favorite = favorite, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

            // 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 更新对应的帖子后备表
            await connection.ExecuteAsync(fallbackPostsUpsertSqlCommand, threadFallbackData);
            // 执行: 更新收藏关系
            int affectedRows = await connection.ExecuteAsync(userFavoritePostsUpsertSqlCommand, favoriteData);

            _logger.LogInformation("更新用户 {UserId} 的帖子 {ThreadId} 收藏, 影响 {AffectedRows} 行。", userId, thread.Id, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("InsertUserFavoritePostAsync {ex}", ex);
        }
    }
    /// <summary>
    /// 为用户添加一个新的收藏夹类型。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="newType">要添加的新类型名称</param
    public async Task AddUserFavoriteTypeAsync(ulong userId, string newType)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(
                userFavoriteTypeInsertSqlCommand, 
                new { UserId = userId, TypeName = newType }
            );

            if (affectedRows > 0)
            {
                _logger.LogInformation("成功为用户 {UserId} 添加了新的收藏夹类型 '{NewType}'。", userId, newType);
            }
            else
            {
                _logger.LogInformation("用户 {UserId} 的收藏夹类型 '{NewType}' 已存在, 跳过添加。", userId, newType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "为用户 {UserId} 添加收藏夹类型时发生错误。", userId);
        }
    }
}