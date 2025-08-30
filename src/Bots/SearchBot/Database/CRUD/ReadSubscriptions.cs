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
    // SQL模板: 查询用户某一订阅贴
    const string getUserSubscriptionsPostSql = "SELECT 1 FROM user_subscriptions_post WHERE user_id = @userId AND thread_id = @threadId LIMIT 1;";
    const string getSubscribedUserIdsByThreadIdSql = "SELECT user_id AS UserId FROM user_subscriptions_post WHERE thread_id = @threadId;";
    // SQL模板: 查询用户订阅的所有帖子
    const string getAllUserSubscriptionsListSql = "SELECT thread_id AS ThreadId, added_at AS AddedAt FROM user_subscriptions_post WHERE user_id = @UserId;";
    // SQL模板: 查询用户的所有推送更新
    const string getUserPushListSql = "SELECT thread_id AS ThreadId, message_jump_url AS MessageJumpUrl, updated_at AS UpdatedAt FROM user_push_list WHERE user_id = @UserId;";

    /// <summary>
    /// 查询用户是否已经订阅了某个帖子
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="threadId"></param>
    /// <returns></returns>
    public async Task<bool> QueryUserSubscriptionsExistAsync(ulong userId, ulong threadId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var result = await connection.QueryFirstOrDefaultAsync<int?>(getUserSubscriptionsPostSql, new { userId, threadId });
            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行用户订阅存在性检查时出错");
            return false;
        }
    }
    
    /// <summary>
    /// 获取数据库中所有订阅了指定帖子的用户ID列表。
    /// </summary>
    /// <param name="threadId">帖子ID</param>
    /// <returns>订阅了该帖子的用户ID列表。</returns>
    public Task<List<ulong>> GetSubscribedUserIdsByThreadIdAsync(ulong threadId)
    {
        return QueryUlongListAsync(getSubscribedUserIdsByThreadIdSql, new { threadId });
    }

    /// <summary>
    /// 查询指定用户订阅的所有帖子。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>一个包含帖子详细信息的列表，如果出错则返回null。</returns>
    public async Task<List<PostDisplayModel>?> QueryUserSubscriptionsAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            
            // 1. 获取用户所有订阅的 thread_id 和 added_at
            var subscriptionResult = await connection.QueryAsync<FavoriteInfo>(getAllUserSubscriptionsListSql, new { UserId = userId });
            
            if (!subscriptionResult.Any())
            {
                _logger.LogInformation("用户 {UserId} 的订阅列表为空。", userId);
                return new List<PostDisplayModel>();
            }

            var allThreadIds = subscriptionResult.Select(s => s.ThreadId).ToList();

            // 2. 从主表获取数据
            var mainPosts = await connection.QueryAsync<PostDisplayModel>(getMainPostsSql, new { threadIds = allThreadIds });
            var postsDictionary = mainPosts.ToDictionary(p => p.ThreadId);

            // 3. 找出缺失的，从备用表获取
            var missingThreadIds = allThreadIds.Except(postsDictionary.Keys).ToList();
            if (missingThreadIds.Any())
            {
                var fallbackPosts = await connection.QueryAsync<PostDisplayModel>(getFallbackPostsSql, new { threadIds = missingThreadIds });
                foreach (var fallbackPost in fallbackPosts)
                {
                    fallbackPost.IsFallbackData = true;
                    postsDictionary.Add(fallbackPost.ThreadId, fallbackPost);
                }
            }

            // 4. 合并数据，填充 added_at
            var finalResults = new List<PostDisplayModel>();
            foreach (var subInfo in subscriptionResult)
            {
                if (postsDictionary.TryGetValue(subInfo.ThreadId, out var postData))
                {
                    postData.AddedAt = subInfo.AddedAt;
                    finalResults.Add(postData);
                }
            }
            
            _logger.LogInformation("为用户 {UserId} 成功获取 {Count} 条订阅帖子。", userId, finalResults.Count);
            return finalResults;
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "执行查询用户订阅列表时出错");
            return null;
        }
    }

    /// <summary>
    /// 查询指定用户的作品更新推送列表。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>一个包含帖子和更新信息的列表，如果出错则返回null。</returns>
    public async Task<List<PostDisplayModel>?> QueryUserPushListAsync(ulong userId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            
            // 1. 从推送列表获取基础信息
            var pushListResult = await connection.QueryAsync<PushListInfo>(getUserPushListSql, new { UserId = userId });

            if (!pushListResult.Any())
            {
                _logger.LogInformation("用户 {UserId} 的推送列表为空。", userId);
                return new List<PostDisplayModel>();
            }

            var allThreadIds = pushListResult.Select(p => p.ThreadId).ToList();
            var pushInfoDictionary = pushListResult.ToDictionary(p => p.ThreadId);

            // 2. 从主表获取数据
            var mainPosts = await connection.QueryAsync<PostDisplayModel>(getMainPostsSql, new { threadIds = allThreadIds });
            var postsDictionary = mainPosts.ToDictionary(p => p.ThreadId);

            // 3. 从备用表获取缺失数据
            var missingThreadIds = allThreadIds.Except(postsDictionary.Keys).ToList();
            if (missingThreadIds.Any())
            {
                var fallbackPosts = await connection.QueryAsync<PostDisplayModel>(getFallbackPostsSql, new { threadIds = missingThreadIds });
                foreach (var fallbackPost in fallbackPosts)
                {
                    fallbackPost.IsFallbackData = true;
                    postsDictionary.Add(fallbackPost.ThreadId, fallbackPost);
                }
            }

            // 4. 合并数据，填充更新信息
            var finalResults = new List<PostDisplayModel>();
            foreach (var pushInfo in pushListResult)
            {
                if (postsDictionary.TryGetValue(pushInfo.ThreadId, out var postData))
                {
                    // 填充来自 push_list 的特定信息
                    postData.UpdatedAt = pushInfo.UpdatedAt;
                    postData.MessageJumpUrl = pushInfo.MessageJumpUrl;
                    finalResults.Add(postData);
                }
            }

            _logger.LogInformation("为用户 {UserId} 成功获取 {Count} 条更新推送。", userId, finalResults.Count);
            return finalResults;
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "执行查询用户推送列表时出错");
            return null;
        }
    }
}