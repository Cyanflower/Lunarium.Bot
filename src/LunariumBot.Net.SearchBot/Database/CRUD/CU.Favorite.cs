using Microsoft.Data.Sqlite;

using Dapper;
using Discord;

namespace LunariumBot.Net.SearchBot.Database;

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
    // SQL模板: 重命名收藏夹类别
    const string favoriteCategoryRenameSqlCommand = "UPDATE user_favorite_types SET type_name = @NewName WHERE user_id = @UserId AND type_name = @OldName;";
    // SQL模板: 更新收藏帖子中的类别名称
    const string favoritePostsCategoryRenameSqlCommand = "UPDATE user_favorite_posts SET favorite_as = @NewName WHERE user_id = @UserId AND favorite_as = @OldName;";

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
                _logger.Warning("用户或帖子对象为空, 操作跳过。");
                return;
            }
            var threadFallbackData = new
            {
                ThreadId = thread.Id,
                Title = thread.Name,
                AuthorId = thread.OwnerId,
                JumpUrl = $"https://discord.com/channels/{thread.Guild.Id}/{thread.Id}/0",
            };
            // 准备: 构造收藏关系数据
            var favoriteData = new { UserId = userId, ThreadId = thread.Id, Favorite = favorite, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

            // 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 更新对应的帖子后备表
            await connection.ExecuteAsync(fallbackPostsUpsertSqlCommand, threadFallbackData);
            // 执行: 更新收藏关系
            int affectedRows = await connection.ExecuteAsync(userFavoritePostsUpsertSqlCommand, favoriteData);

            _logger.Info($"更新用户 {userId} 的帖子 {thread.Id} 收藏, 影响 {affectedRows} 行。");
        }
        catch (Exception ex)
        {
            _logger.Error("InsertUserFavoritePostAsync Ex:", ex);
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
                _logger.Info($"成功为用户 {userId} 添加了新的收藏夹类型 '{newType}'。");
            }
            else
            {
                _logger.Info($"用户 {userId} 的收藏夹类型 '{newType}' 已存在, 跳过添加。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error( $"为用户 {userId} 添加收藏夹类型时发生错误。", ex);
        }
    }
    
    /// <summary>
    /// 重命名用户的某个收藏夹
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="oldName">旧名称</param>
    /// <param name="newName">新名称</param>
    /// <returns>操作影响的总行数</returns>
    public async Task<int> RenameFavoriteCategoryAsync(ulong userId, string oldName, string newName)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            
            int totalAffectedRows = 0;
            
            // 步骤 1: 更新 user_favorite_types 表
            totalAffectedRows += await connection.ExecuteAsync(favoriteCategoryRenameSqlCommand, new { UserId = userId, OldName = oldName, NewName = newName }, transaction);

            // 步骤 2: 更新 user_favorite_posts 表
            totalAffectedRows += await connection.ExecuteAsync(favoritePostsCategoryRenameSqlCommand, new { UserId = userId, OldName = oldName, NewName = newName }, transaction);

            await transaction.CommitAsync();

            _logger.Info($"用户 {userId} 成功将收藏夹 '{oldName}' 重命名为 '{newName}', 影响了 {totalAffectedRows} 行记录。");
            return totalAffectedRows;
        }
        catch (Exception ex)
        {
            _logger.Error($"重命名用户 {userId} 的收藏夹 '{oldName}' 时出错。", ex);
            return 0;
        }
    }
}