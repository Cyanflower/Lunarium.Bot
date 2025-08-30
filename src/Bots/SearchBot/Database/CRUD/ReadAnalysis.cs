// src/Bots/SearchBot/Service/Database/CRUD/ReadAnalysis.cs
using Dapper;
using Microsoft.Data.Sqlite;
using SearchBot.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    
    private const string _postDisplayModelSelectClause = """
        SELECT 
            thread_id AS ThreadId, first_message_id AS FirstMessageId, author_id AS AuthorId,
            channel_id AS ChannelId, channel_name AS ChannelName, guild_id AS GuildId,
            guild_name AS GuildName, created_at AS CreatedAt, member_count AS MemberCount,
            reaction_count AS ReactionCount, reply_count AS ReplyCount, last_active_at AS LastActiveAt,
            jump_url AS JumpUrl, attachment_url AS AttachmentUrl, tags AS Tags, title AS Title,
            content AS Content, update_at AS UpdatedAt, message_jump_url AS MessageJumpUrl
        FROM posts_main
        """;

    /// <summary>
    /// 获取帖子总数。
    /// </summary>
    /// <param name="channelIds">可选的频道ID列表，用于过滤。</param>
    /// <returns>帖子总数。</returns>
    public async Task<int> GetTotalPostCountAsync(List<ulong>? channelIds = null)
    {
        // ✨ 优点2: 动态构建SQL, 保证性能
        var sqlBuilder = new StringBuilder("SELECT COUNT(*) FROM posts_main");
        var parameters = new DynamicParameters();

        if (channelIds != null && channelIds.Any())
        {
            sqlBuilder.Append(" WHERE channel_id IN @ChannelIds");
            parameters.Add("ChannelIds", channelIds);
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(sqlBuilder.ToString(), parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询总帖子数时出错");
            return 0;
        }
    }

    /// <summary>
    /// 从 server_data 表中获取自指定日期以来的新增帖子数。
    /// </summary>
    /// <param name="startDate">起始日期字符串 (格式: "YYYY-MM-DD")</param>
    /// <param name="channelIds">可选的频道ID列表，用于过滤。</param>
    /// <returns>新增帖子数。</returns>
    public async Task<int> GetPostCountSinceDateAsync(string startDate, List<ulong>? channelIds = null)
    {
        var sqlBuilder = new StringBuilder("SELECT COALESCE(SUM(created_posts_count), 0) FROM server_data WHERE date >= @StartDate");
        var parameters = new DynamicParameters();
        parameters.Add("StartDate", startDate);

        if (channelIds != null && channelIds.Any())
        {
            sqlBuilder.Append(" AND channel_id IN @ChannelIds");
            parameters.Add("ChannelIds", channelIds);
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(sqlBuilder.ToString(), parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 server_data 查询新增帖子数时出错");
            return 0;
        }
    }
    /// <summary>
    /// 从 posts_main 表中获取所有帖子的回复总数。
    /// </summary>
    /// <remarks>
    /// 这个方法直接查询主数据表，与 GetTotalReplyCountFromStatsAsync 不同，后者查询的是每日统计表。
    /// </remarks>
    /// <param name="channelIds">可选的频道ID列表，用于过滤特定频道的帖子。</param>
    /// <returns>回复总数。</returns>
    public async Task<int> GetTotalReplyCountFromPostsAsync(List<ulong>? channelIds = null)
    {
        var sqlBuilder = new StringBuilder("SELECT COALESCE(SUM(reply_count), 0) FROM posts_main");
        var parameters = new DynamicParameters();

        if (channelIds != null && channelIds.Any())
        {
            sqlBuilder.Append(" WHERE channel_id IN @ChannelIds");
            parameters.Add("ChannelIds", channelIds);
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(sqlBuilder.ToString(), parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 posts_main 查询总回复数时出错");
            return 0;
        }
    }

    /// <summary>
    /// 从 server_data 表中获取回复数总和。
    /// </summary>
    /// <param name="sinceDate">可选的起始日期字符串 (格式: "YYYY-MM-DD")，用于统计某段时间内的回复。</param>
    /// <param name="channelIds">可选的频道ID列表，用于过滤。</param>
    /// <returns>回复总数。</returns>
    public async Task<int> GetTotalReplyCountFromStatsAsync(string? sinceDate = null, List<ulong>? channelIds = null)
    {
        var sqlBuilder = new StringBuilder("SELECT COALESCE(SUM(reply_count), 0) FROM server_data");
        var parameters = new DynamicParameters();
        var whereClauses = new List<string>();

        if (!string.IsNullOrEmpty(sinceDate))
        {
            whereClauses.Add("date >= @SinceDate");
            parameters.Add("SinceDate", sinceDate);
        }
        if (channelIds != null && channelIds.Any())
        {
            whereClauses.Add("channel_id IN @ChannelIds");
            parameters.Add("ChannelIds", channelIds);
        }

        if (whereClauses.Any())
        {
            sqlBuilder.Append(" WHERE ").Append(string.Join(" AND ", whereClauses));
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(sqlBuilder.ToString(), parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 server_data 查询总回复数时出错");
            return 0;
        }
    }

    /// <summary>
    /// 获取最热门的标签。
    /// </summary>
    /// <param name="limit">返回的标签数量上限。</param>
    /// <param name="sinceTimestamp">可选的时间戳，用于查询近期热门标签。</param>
    /// <param name="channelIds">可选的频道ID列表，用于过滤。</param>
    /// <returns>一个字典，包含TagInfo和其使用次数。</returns>
    public async Task<Dictionary<TagInfo, long>> GetTopTagsAsync(int limit, long? sinceTimestamp = null, List<ulong>? channelIds = null)
    {
        var sqlBuilder = new StringBuilder("""
            SELECT 
                t.tag_id AS TagId, 
                t.channel_id as ChannelId,
                t.tag_name AS TagName, 
                t.channel_name AS ChannelName,
                t.guild_name AS GuildName,
                COUNT(ptj.tag_id) as TagCount
            FROM post_tags_junction ptj
            INNER JOIN tags t ON ptj.tag_id = t.tag_id
            """);
        var parameters = new DynamicParameters();
        var whereClauses = new List<string>();

        if (sinceTimestamp.HasValue)
        {
            // 如果需要按时间过滤，必须连接 posts_main 表
            sqlBuilder.Append(" INNER JOIN posts_main p ON ptj.thread_id = p.thread_id");
            whereClauses.Add("p.created_at >= @SinceTimestamp");
            parameters.Add("SinceTimestamp", sinceTimestamp.Value);
        }

        if (channelIds != null && channelIds.Any())
        {
            // ptj 表有 channel_id，可以直接用它来过滤
            whereClauses.Add("ptj.channel_id IN @ChannelIds");
            parameters.Add("ChannelIds", channelIds);
        }

        if (whereClauses.Any())
        {
            sqlBuilder.Append(" WHERE ").Append(string.Join(" AND ", whereClauses));
        }

        sqlBuilder.Append(" GROUP BY ptj.tag_id ORDER BY TagCount DESC LIMIT @Limit");
        parameters.Add("Limit", limit);

        try
        {
            using var connection = new SqliteConnection(_connectionString);

            var queryResult = await connection.QueryAsync<TagInfo, long, KeyValuePair<TagInfo, long>>(
                sqlBuilder.ToString(),
                (tag, count) => new KeyValuePair<TagInfo, long>(tag, count),
                parameters,
                splitOn: "TagCount"
            );
            return queryResult.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询热门标签时出错");
            return new Dictionary<TagInfo, long>();
        }
    }
    
    /// <summary>
    /// 根据特定指标（如回应、回复数）获取排名靠前的帖子。
    /// </summary>
    /// <param name="metric">要排序的列名（'reaction_count' 或 'reply_count'）。</param>
    /// <param name="limit">返回的帖子数量。</param>
    /// <param name="sinceTimestamp">可选的时间戳，用于查询近期热门帖子。</param>
    /// <param name="channelIds">可选的频道ID列表，用于过滤。</param>
    /// <returns>帖子显示模型列表。</returns>
    public async Task<List<PostDisplayModel>> GetTopPostsByMetricAsync(string metric, int limit, long? sinceTimestamp = null, List<ulong>? channelIds = null)
    {
        // 为了安全，我们白名单化列名
        if (metric != "reaction_count" && metric != "reply_count")
        {
            throw new ArgumentException("Invalid metric. Only 'reaction_count' or 'reply_count' are allowed.", nameof(metric));
        }

        var sqlBuilder = new StringBuilder(_postDisplayModelSelectClause);
        var parameters = new DynamicParameters();
        var whereClauses = new List<string>();

        if (sinceTimestamp.HasValue)
        {
            whereClauses.Add("created_at >= @SinceTimestamp");
            parameters.Add("SinceTimestamp", sinceTimestamp.Value);
        }

        if (channelIds != null && channelIds.Any())
        {
            whereClauses.Add("channel_id IN @ChannelIds");
            parameters.Add("ChannelIds", channelIds);
        }

        if (whereClauses.Any())
        {
            sqlBuilder.Append(" WHERE ").Append(string.Join(" AND ", whereClauses));
        }
        
        sqlBuilder.Append($" ORDER BY {metric} DESC LIMIT @Limit");
        parameters.Add("Limit", limit);
        
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var result = await connection.QueryAsync<PostDisplayModel>(sqlBuilder.ToString(), parameters);
            return result.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按指标查询热门帖子时出错");
            return new List<PostDisplayModel>();
        }
    }
}