// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Discord;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot.Database;

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
    // SQL模板，用于在删除前获取帖子的统计相关信息
    private const string getPostInfoForStatsSql = """
        SELECT 
            thread_id AS ThreadId, 
            created_at AS CreatedAt,
            guild_id AS GuildId,
            channel_id AS ChannelId,
            reply_count AS ReplyCount
        FROM posts_main 
        WHERE thread_id IN @PostIds;
        """;

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
                _logger.Warning("无帖子数据需要移除, 操作跳过。");
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
                _logger.Debug($"成功移除 {affectedRows} 条帖子及其关联数据 (处理了 {postIds.Count} 个ID)。");
            }
            catch (Exception ex)
            {
                // 失败: 回滚事务
                await transaction.RollbackAsync();
                _logger.Error( "批量删除帖子时发生错误, 事务已回滚。",ex);
                return 0;
            }
            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.Error("RemovePostAsync Ex:", ex);
            return 0;
        }
    }

    /// <summary>
    /// [重构后] 批量移除帖子及其关联数据 (帖子ID列表)。
    /// 该方法现在会自动处理统计数据的更新。
    /// </summary>
    /// <param name="postIds">要移除的帖子ID列表</param>
    /// <returns>数据库成功更改的行数</returns>
    public async Task<int> RemovePostAsync(List<ulong> postIds)
    {
        try
        {
            if (postIds == null || !postIds.Any())
            {
                _logger.Warning("无帖子数据需要移除, 操作跳过。");
                return 0;
            }

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            int affectedRows = 0;

            try
            {
                // 在删除前，查询这些帖子的统计信息
                var postsToProcess = await connection.QueryAsync<PostInfoForStats>(
                    getPostInfoForStatsSql,
                    new { PostIds = postIds },
                    transaction
                );

                if (!postsToProcess.Any())
                {
                    _logger.Warning($"尝试删除 {postIds.Count} 个帖子, 但在 posts_main 中未找到任何记录, 可能已被删除。");
                    // 即使 posts_main 里没有，也继续尝试删除，因为可能存在于其他关联表中
                    // (虽然 CASCADE 会处理，但这样更明确)
                    affectedRows = await connection.ExecuteAsync(postsDeleteSqlCommand, new { PostIds = postIds }, transaction);
                    await transaction.CommitAsync();
                    return affectedRows;
                }

                var statsGroupedByChannel = postsToProcess
                    .GroupBy(p => new { p.GuildId, p.ChannelId }) // <-- 复合键分组
                    .Select(g => new
                    {
                        // 分组的键，包含了 GuildId 和 ChannelId
                        g.Key.GuildId,
                        g.Key.ChannelId,

                        // 对每个分组内部进行聚合计算
                        // PostsRemoved: 这个分组里有多少个帖子被删除
                        PostsRemoved = g.Count(),

                        // RepliesRemoved: 这个分组里所有帖子的回复数总和
                        RepliesRemoved = g.Sum(p => p.ReplyCount)
                    });

                // 获取当前的统计日期，对于所有分组都是一样的
                // 这里的目的是: 我们更希望获得一个 实时 的数据概况, 反映“当下”变化的实时数据参考
                // 因为删除帖子导致回复数下降是发生在当下, 减去当下的数据更能表示是现在发生了什么事, 而不是当做从来没存在过
                var currentDate = DateTimeOffset.UtcNow.AddHours(4).ToString("yyyy-MM-dd");

                // 遍历每一个分组，为各自的统计行执行更新
                _logger.Info($"准备更新 {statsGroupedByChannel.Count()} 个不同频道的每日统计数据。");
                foreach (var stat in statsGroupedByChannel)
                {
                    _logger.Info($"  -> 更新频道 {stat.ChannelId}: 帖子数 -{stat.PostsRemoved}, 回复数 -{stat.RepliesRemoved}");

                    await DecrementPostStatsAsync(
                       currentDate,          // 使用【当前日期】
                       stat.GuildId,         // 使用当前分组的 GuildId
                       stat.ChannelId,       // 使用当前分组的 ChannelId
                       stat.PostsRemoved,    // 使用当前分组计算出的帖子减少数
                       stat.RepliesRemoved,  // 使用当前分组计算出的回复减少数
                       transaction           // 在同一个事务中执行！
                   );
                }

                // 所有统计都更新完毕后，再统一执行帖子的删除操作
                affectedRows = await connection.ExecuteAsync(postsDeleteSqlCommand, new { PostIds = postIds }, transaction);

                // 提交事务，完成所有操作
                await transaction.CommitAsync();
                _logger.Info($"成功移除 {affectedRows} 条帖子，并精确更新了 {statsGroupedByChannel.Count()} 个频道的当日统计数据。");

                return affectedRows;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error("批量删除帖子时发生错误, 事务已回滚。", ex);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("在 RemovePostAsync(List<ulong> postIds) 中发生异常。",ex);
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
            _logger.Info("开始清理过时的频道和标签数据...");
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
                    _logger.Info($"成功清理 {affectedRows} 个过时的频道。");
                }
                else
                {
                    _logger.Info("无过时频道需要清理。");
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
                    _logger.Info($"成功清理 {affectedRows} 个过时的标签。");
                }
                else
                {
                    _logger.Info("无过时标签需要清理。");
                }

                await transaction.CommitAsync();
                _logger.Info($"数据清理完成。保留频道 {currentChannelIds.Count} 个, 标签 {currentTagIds.Count} 个。");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error("清理过时数据时发生错误, 事务已回滚。",ex);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("CleanupObsoleteDataAsync Ex", ex);
        }
    }
}