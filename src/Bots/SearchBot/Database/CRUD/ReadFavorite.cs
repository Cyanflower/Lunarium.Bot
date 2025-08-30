
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

using Dapper;
using Discord;

using SearchBot.Model;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    // SQL模板: 查询用户收藏夹类别列表
    const string getUserFavoriteTypeSql = "SELECT type_name FROM user_favorite_types WHERE user_id = @userId;";
    // SQL模板: 查询用户某一收藏贴是否存在
    const string getUserFavoritePostSql = "SELECT thread_id AS ThreadId FROM user_favorite_posts WHERE user_id = @userId AND thread_id = @threadId LIMIT 1;";
    // SQL模板: 查询用户收藏的帖子列表
    const string getUserFavoriteListSql = "SELECT thread_id AS ThreadId, favorite_as AS FavoriteAS FROM user_favorite_posts WHERE user_id = @userId AND favorite_as = @favoriteAs;";
    // SQL模板: 查询用户收藏的全部帖子列表
    const string getAllUserFavoriteListSql = "SELECT thread_id AS ThreadId, added_at AS AddedAt, favorite_as AS FavoriteAS FROM user_favorite_posts WHERE user_id = @userId;";
    // SQL模板: 按用户的收藏列表获取帖子数据 (主表)
    const string getMainPostsSql = """
        SELECT 
            thread_id AS ThreadId,
            first_message_id AS FirstMessageId, 
            author_id AS AuthorId,
            channel_id AS ChannelId,
            channel_name AS ChannelName,
            guild_id AS GuildId,
            guild_name AS GuildName,
            created_at AS CreatedAt,
            member_count AS MemberCount,
            reaction_count AS ReactionCount,
            reply_count AS ReplyCount,
            last_active_at AS LastActiveAt,
            jump_url AS JumpUrl,
            attachment_url AS AttachmentUrl,
            tags AS Tags,
            title AS Title,
            content AS Content,
            update_at AS UpdatedAt,
            message_jump_url AS MessageJumpUrl
        FROM posts_main WHERE thread_id IN @threadIds;
        """;
    // SQL模板: 按用户的收藏列表获取帖子数据 (备用表)
    const string getFallbackPostsSql = """
        SELECT 
            thread_id AS ThreadId,
            author_id AS AuthorId,
            title AS Title,
            jump_url AS JumpUrl,
            attachment_url AS AttachmentUrl
        FROM fallback_posts WHERE thread_id IN @threadIds;
        """;

    /// <summary>
    /// 获取数据库中单个用户收藏的类型
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户收藏的帖子类型</returns>
    public async Task<HashSet<string>> GetUserFavoriteTypeAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var types = await connection.QueryAsync<string>(getUserFavoriteTypeSql, new { userId });
            return types.ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行用户收藏的帖子类型查询时出错. SQL: {sqlQuery}", getUserFavoriteTypeSql);
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// 查询用户是否已经收藏了某个帖子
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="threadId"></param>
    /// <returns></returns>
    public async Task<bool> QueryUserFavoriteExistAsync(ulong userId, ulong threadId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var result = await connection.QueryFirstOrDefaultAsync<ulong?>(getUserFavoritePostSql, new { userId, threadId });
            if (result != null)
            {
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("执行用户收藏存在性检查时出错 {ex}", ex);
            return false;
        }
    }

    public async Task<List<PostDisplayModel>?> QueryUserFavoriteAsync(ulong userId, string? favorite = null)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            IEnumerable<FavoriteInfo>? favoriteResult;
            // 根据是否传入收藏夹名称决定查询方式
            if (favorite == null)
            {
                favoriteResult = await connection.QueryAsync<FavoriteInfo>(getAllUserFavoriteListSql, new { userId });
            }
            else
            {
                favoriteResult = await connection.QueryAsync<FavoriteInfo>(getUserFavoriteListSql, new { userId = userId, favoriteAs = favorite });

            }
            _logger.LogDebug("allThreadIdsL {favoriteResult}", favoriteResult);
            // 如果查询结果为空，返回空列表
            if (!favoriteResult.Any())
            {
                _logger.LogInformation("用户 {UserId} 没有收藏任何帖子。", userId);
                return new List<PostDisplayModel>();
            }
            // 提取出所有需要查询详情的 thread_id
            var allThreadIds = favoriteResult.Select(f => f.ThreadId).ToList();
            _logger.LogDebug("allThreadIdsL {allThreadIds}", allThreadIds);

            // 先尝试从 posts_main 中一次性获取所有能找到的帖子
            var mainPosts = await connection.QueryAsync<PostDisplayModel>(getMainPostsSql, new { threadIds = allThreadIds });
            // 转为字典,方便哈希表快速检查存在
            var postsDictionary = mainPosts.ToDictionary(p => p.ThreadId);
            // 计算差集
            var missingThreadIds = allThreadIds.Except(postsDictionary.Keys).ToList();

            // 如果有缺失的 thread_id，从 fallback_posts 中获取数据并填充到字典中
            if (missingThreadIds.Any())
            {
                var fallbackPosts = await connection.QueryAsync<PostDisplayModel>(getFallbackPostsSql, new { threadIds = missingThreadIds });
                // 填充到字典中
                foreach (var fallbcakPost in fallbackPosts)
                {
                    fallbcakPost.IsFallbackData = true;
                    postsDictionary.Add(fallbcakPost.ThreadId, fallbcakPost);
                }
            }

            // 合并数据并填充到最终结果列表中
            var finalResults = new List<PostDisplayModel>();
            // 遍历每个收藏信息，如果在字典中找到对应的帖子数据，则填充添加时间并添加到最终结果列表中
            foreach (var favoriteInfo in favoriteResult)
            {
                if (postsDictionary.TryGetValue(favoriteInfo.ThreadId, out var postData))
                {
                    postData.AddedAt = favoriteInfo.AddedAt;
                    postData.FavoriteAS = favoriteInfo.FavoriteAS;
                    finalResults.Add(postData);
                }
            }
            _logger.LogInformation("为用户 {UserId} 成功获取 {Count} 条收藏帖子。", userId, finalResults.Count);
            return finalResults;
        }
        catch (Exception ex)
        {
            _logger.LogError("执行查询用户收藏时出错 {ex}", ex);
            return null;
        }
    }
}
