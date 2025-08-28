using Dapper;
using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    // SQL模板: 基于ID列表删除帖子
    const string postsDeleteSqlCommand = "DELETE FROM posts_main WHERE thread_id IN @PostIds;";
    // SQL模板: 基于ID列表删除标签
    const string tagsDeleteSqlCommand = "DELETE FROM tags WHERE tag_id IN @TagIds;";
    // SQL模板: 基于ID列表删除频道
    const string channelsDeleteSqlCommand = "DELETE FROM channels WHERE channel_id IN @ChannelIds;";
    // SQL模板: 删除用户收藏和订阅的帖子后备表
    const string FavoriteAndSubscriptionPostsDeleteSqlCommand = "DELETE FROM fallback_posts WHERE thread_id IN @ThreadIds;";
    // SQL模板: 删除用户收藏的帖子
    const string userFavoritePostsDeleteSqlCommand = "DELETE FROM user_favorite_posts WHERE thread_id IN @ThreadIds;";
    // SQL模板: 删除用户订阅的帖子
    const string userSubscriptionsPostDeleteSqlCommand = "DELETE FROM user_subscriptions_post WHERE thread_id IN @ThreadIds;";
    // SQL模板: 删除用户待推送的帖子
    const string userPushListDeleteSqlCommand = "DELETE FROM user_push_list WHERE thread_id IN @ThreadIds;";
    
    /// <summary>
    /// 批量移除帖子及其关联数据 (帖子对象列表)。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="threadChannelList">要移除的帖子对象列表。</param>
    /// <returns>数据库成功更改的行数</returns>
    public async Task<int> RemovePostAsync(List<IThreadChannel> threadChannelList)
    {
        try
        {
            // 校验: 若无数据则提前返回
            if (threadChannelList == null || !threadChannelList.Any())
            {
                _logger.LogWarning("无帖子数据需要移除, 操作跳过。");
                return 0;
            }

            // 准备: 提取所有帖子的ID
            var postIds = threadChannelList.Select(t => t.Id).ToList();

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            // 开始: 数据库事务
            await using var transaction = await connection.BeginTransactionAsync();

            int affectedRows = 0;

            try
            {
                // 执行: Dapper 批量删除
                affectedRows = await connection.ExecuteAsync(postsDeleteSqlCommand, new { PostIds = postIds }, transaction);

                // 成功: 提交事务
                await transaction.CommitAsync();
                _logger.LogInformation("成功移除 {AffectedRows} 条帖子及其关联数据 (处理了 {TotalCount} 个ID)。", affectedRows, postIds.Count);
            }
            catch (Exception ex)
            {
                // 失败: 回滚事务
                await transaction.RollbackAsync();
                _logger.LogError(ex, "批量删除帖子时发生错误, 事务已回滚。");
                return 0;
            }
            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.LogError("{ex}", ex);
            return 0;
}
    }
    
    /// <summary>
    /// 批量移除帖子及其关联数据 (帖子ID列表)。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="postIds">要移除的帖子ID列表</param>
    /// <returns>数据库成功更改的行数</returns>
    public async Task<int> RemovePostAsync(List<ulong> postIds)
    {
        try
        {
            // 校验: 若无数据则提前返回
            if (postIds == null || !postIds.Any())
            {
                _logger.LogWarning("无帖子数据需要移除, 操作跳过。");
                return 0;
            }

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            // 开始: 数据库事务
            await using var transaction = await connection.BeginTransactionAsync();

            int affectedRows;

            try
            {
                // 执行: Dapper 批量删除
                affectedRows = await connection.ExecuteAsync(postsDeleteSqlCommand, new { PostIds = postIds }, transaction);

                // 成功: 提交事务
                await transaction.CommitAsync();
                _logger.LogInformation("成功移除 {AffectedRows} 条帖子及其关联数据 (处理了 {TotalCount} 个ID)。", affectedRows, postIds.Count);
            }
            catch (Exception ex)
            {
                // 失败: 回滚事务
                await transaction.RollbackAsync();
                _logger.LogError(ex, "批量删除帖子时发生错误, 事务已回滚。");
                throw;
            }
            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.LogError("{ex}", ex);
            return 0;
}
    }

    /// <summary>
    /// 清理数据库中不再存在于当前配置中的频道和标签
    /// </summary>
    /// <param name="validForumChannels">当前有效的论坛频道列表</param>
    /// <param name="allValidTags">当前所有标签列表</param>
    public async Task CleanupObsoleteDataAsync(List<IForumChannel> validForumChannels, List<IForumTag> allValidTags)
    {
        try
        {
            _logger.LogInformation("开始清理过时的频道和标签数据...");
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
    
            try
            {
                var currentChannelIds = validForumChannels.Select(c => c.Id).ToHashSet();
                var dbChannelIds = await connection.QueryAsync<ulong>("SELECT channel_id FROM channels;", transaction: transaction);
                var channelsToDelete = dbChannelIds.Where(id => !currentChannelIds.Contains(id)).ToList();
    
                if (channelsToDelete.Any())
                {
                    int affectedRows = await connection.ExecuteAsync(channelsDeleteSqlCommand, new { ChannelIds = channelsToDelete }, transaction);
                    _logger.LogInformation("成功清理 {Count} 个过时的频道。", affectedRows);
                }
                else
                {
                    _logger.LogInformation("无过时频道需要清理。");
                }
    
                var currentTagIds = allValidTags
                    .Where(t => t.Id.HasValue)
                    .Select(t => t.Id!.Value)
                    .ToHashSet();
                var dbTagIds = await connection.QueryAsync<ulong>("SELECT tag_id FROM tags;", transaction: transaction);
                var tagsToDelete = dbTagIds.Where(id => !currentTagIds.Contains(id)).ToList();
    
                if (tagsToDelete.Any())
                {
                    int affectedRows = await connection.ExecuteAsync(tagsDeleteSqlCommand, new { TagIds = tagsToDelete }, transaction);
                    _logger.LogInformation("成功清理 {Count} 个过时的标签。", affectedRows);
                }
                else
                {
                    _logger.LogInformation("无过时标签需要清理。");
                }
                
                await transaction.CommitAsync();
                _logger.LogInformation("数据清理完成。保留频道 {ChannelCount} 个, 标签 {TagCount} 个。", 
                    currentChannelIds.Count, currentTagIds.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "清理过时数据时发生错误, 事务已回滚。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CleanupObsoleteDataAsync {ex}", ex);
        }
    }
}