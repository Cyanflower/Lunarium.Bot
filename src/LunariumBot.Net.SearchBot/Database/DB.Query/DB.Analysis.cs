// NuGet 包依赖
using Npgsql;
using Dapper;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.SearchBot.Models.Database;

namespace LunariumBot.Net.SearchBot.Database;

/// <summary>
/// 数据分析相关的查询方法
/// </summary>
public partial class DatabaseService
{
    // =====================================================
    // 帖子统计查询
    // =====================================================

    /// <summary>
    /// 获取指定服务器/频道的帖子总数
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="channelIds">可选：频道ID列表。为 null 时统计整个服务器</param>
    /// <returns>帖子总数</returns>
    public async Task<int> GetPostCountAsync(ulong guildId, List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT COUNT(*)
            FROM posts_main
            WHERE guild_id = @GuildId
              AND is_deleted = FALSE
              AND (@ChannelIds::bigint[] IS NULL OR channel_id = ANY(@ChannelIds))
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            ChannelIds = channelIds?.Select(id => id.ToDbId()).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            return await connection.ExecuteScalarAsync<int>(SqlCommand, parameters);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetPostCountAsync: 查询服务器 {GuildId} 的帖子总数失败", guildId);
            throw;
        }
    }

    /// <summary>
    /// 获取指定时间范围内的帖子数
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="startTime">开始时间（包含）</param>
    /// <param name="endTime">结束时间（不包含）</param>
    /// <param name="channelIds">可选：频道ID列表</param>
    /// <returns>该时间范围内的帖子数</returns>
    public async Task<int> GetPostCountInTimeRangeAsync(
        ulong guildId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT COUNT(*)
            FROM posts_main
            WHERE guild_id = @GuildId
              AND created_at >= @StartTime
              AND created_at < @EndTime
              AND is_deleted = FALSE
              AND (@ChannelIds::bigint[] IS NULL OR channel_id = ANY(@ChannelIds))
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            StartTime = startTime,
            EndTime = endTime,
            ChannelIds = channelIds?.Select(id => id.ToDbId() ).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            return await connection.ExecuteScalarAsync<int>(SqlCommand, parameters);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetPostCountInTimeRangeAsync: 查询服务器 {GuildId} 在 [{StartTime} - {EndTime}] 的帖子数失败",
                guildId, startTime, endTime);
            throw;
        }
    }

    // =====================================================
    // 回复统计查询
    // =====================================================

    /// <summary>
    /// 获取回复总数（从 posts_main.reply_count 聚合）
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="channelIds">可选：频道ID列表</param>
    /// <returns>回复总数</returns>
    public async Task<int> GetTotalReplyCountAsync(ulong guildId, List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT COALESCE(SUM(reply_count), 0)
            FROM posts_main
            WHERE guild_id = @GuildId
              AND is_deleted = FALSE
              AND (@ChannelIds::bigint[] IS NULL OR channel_id = ANY(@ChannelIds))
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            ChannelIds = channelIds?.Select(id => id.ToDbId()).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            var result = await connection.ExecuteScalarAsync<long?>(SqlCommand, parameters);
            return (int)(result ?? 0);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetTotalReplyCountAsync: 查询服务器 {GuildId} 的回复总数失败", guildId);
            throw;
        }
    }

    /// <summary>
    /// 获取指定时间范围内的回复数（从 message_main 统计）
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="startTime">开始时间（包含）</param>
    /// <param name="endTime">结束时间（不包含）</param>
    /// <param name="channelIds">可选：频道ID列表</param>
    /// <returns>该时间范围内的回复数</returns>
    public async Task<int> GetReplyCountInTimeRangeAsync(
        ulong guildId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT COUNT(*)
            FROM message_main
            WHERE guild_id = @GuildId
              AND created_at >= @StartTime
              AND created_at < @EndTime
              AND thread_id IS NOT NULL
              AND is_create_thread = FALSE
              AND is_deleted = FALSE
              AND (@ChannelIds::bigint[] IS NULL OR channel_id = ANY(@ChannelIds))
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            StartTime = startTime,
            EndTime = endTime,
            ChannelIds = channelIds?.Select(id => id.ToDbId()).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            return await connection.ExecuteScalarAsync<int>(SqlCommand, parameters);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetReplyCountInTimeRangeAsync: 查询服务器 {GuildId} 在 [{StartTime} - {EndTime}] 的回复数失败",
                guildId, startTime, endTime);
            throw;
        }
    }

    // =====================================================
    // 标签统计查询
    // =====================================================

    /// <summary>
    /// 获取热门标签 Top N（可选时间范围过滤）
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="topN">返回前N个标签</param>
    /// <param name="sinceTime">可选：只统计该时间之后创建的帖子。为 null 时统计全部历史</param>
    /// <param name="channelIds">可选：频道ID列表</param>
    /// <returns>标签信息及其使用次数的字典</returns>
    public async Task<Dictionary<TagInfo, int>> GetTopTagsAsync(
        ulong guildId,
        int topN,
        DateTimeOffset? sinceTime = null,
        List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT
                t.tag_id AS TagId,
                t.channel_id AS ChannelId,
                t.tag_name AS TagName,
                t.channel_name AS ChannelName,
                t.guild_name AS GuildName,
                COUNT(ptj.thread_id)::int AS UsageCount
            FROM tags t
            INNER JOIN post_tags_junction ptj ON t.tag_id = ptj.tag_id
            INNER JOIN posts_main p ON ptj.thread_id = p.thread_id
            WHERE p.guild_id = @GuildId
              AND p.is_deleted = FALSE
              AND (@SinceTime IS NULL OR p.created_at >= @SinceTime)
              AND (@ChannelIds::bigint[] IS NULL OR p.channel_id = ANY(@ChannelIds))
            GROUP BY t.tag_id, t.channel_id, t.tag_name, t.channel_name, t.guild_name
            ORDER BY UsageCount DESC
            LIMIT @TopN;
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            TopN = topN,
            SinceTime = sinceTime,
            ChannelIds = channelIds?.Select(id => id.ToDbId()).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            var results = await connection.QueryAsync<TagInfo>(SqlCommand, parameters);

            // 转换为 Dictionary<TagInfo, int>
            var dictionary = new Dictionary<TagInfo, int>();
            foreach (var tag in results)
            {
                dictionary[tag] = tag.UsageCount;
            }

            return dictionary;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetTopTagsAsync: 查询服务器 {GuildId} 的热门标签失败", guildId);
            throw;
        }
    }

    // =====================================================
    // 焦点帖子查询
    // =====================================================

    /// <summary>
    /// 按回应数获取热门帖子 Top N
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="topN">返回前N个帖子</param>
    /// <param name="sinceTime">可选：只统计该时间之后创建的帖子</param>
    /// <param name="channelIds">可选：频道ID列表</param>
    /// <returns>帖子信息列表</returns>
    public async Task<List<PostDisplayModel>> GetTopPostsByReactionAsync(
        ulong guildId,
        int topN,
        DateTimeOffset? sinceTime = null,
        List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT
                thread_id AS ThreadId,
                title AS Title,
                jump_url AS JumpUrl,
                attachment_url AS AttachmentUrl,
                reaction_count AS ReactionCount,
                reply_count AS ReplyCount,
                channel_name AS ChannelName
            FROM posts_main
            WHERE guild_id = @GuildId
              AND is_deleted = FALSE
              AND (@SinceTime IS NULL OR created_at >= @SinceTime)
              AND (@ChannelIds::bigint[] IS NULL OR channel_id = ANY(@ChannelIds))
            ORDER BY reaction_count DESC, reply_count DESC
            LIMIT @TopN;
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            TopN = topN,
            SinceTime = sinceTime,
            ChannelIds = channelIds?.Select(id => id.ToDbId()).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            var results = await connection.QueryAsync<PostDisplayModel>(SqlCommand, parameters);
            return results.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetTopPostsByReactionAsync: 查询服务器 {GuildId} 按回应数排名的帖子失败", guildId);
            throw;
        }
    }

    /// <summary>
    /// 按回复数获取热门帖子 Top N
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="topN">返回前N个帖子</param>
    /// <param name="sinceTime">可选：只统计该时间之后创建的帖子</param>
    /// <param name="channelIds">可选：频道ID列表</param>
    /// <returns>帖子信息列表</returns>
    public async Task<List<PostDisplayModel>> GetTopPostsByReplyAsync(
        ulong guildId,
        int topN,
        DateTimeOffset? sinceTime = null,
        List<ulong>? channelIds = null)
    {
        const string SqlCommand = """
            SELECT
                thread_id AS ThreadId,
                title AS Title,
                jump_url AS JumpUrl,
                attachment_url AS AttachmentUrl,
                reaction_count AS ReactionCount,
                reply_count AS ReplyCount,
                channel_name AS ChannelName
            FROM posts_main
            WHERE guild_id = @GuildId
              AND is_deleted = FALSE
              AND (@SinceTime IS NULL OR created_at >= @SinceTime)
              AND (@ChannelIds::bigint[] IS NULL OR channel_id = ANY(@ChannelIds))
            ORDER BY reply_count DESC, reaction_count DESC
            LIMIT @TopN;
            """;

        var parameters = new
        {
            GuildId = guildId.ToDbId(),
            TopN = topN,
            SinceTime = sinceTime,
            ChannelIds = channelIds?.Select(id => id.ToDbId()).ToArray()
        };

        try
        {
            using var connection = GetConnection();
            var results = await connection.QueryAsync<PostDisplayModelDb>(SqlCommand, parameters);
            return results.Select(data => data.ToDomain()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetTopPostsByReplyAsync: 查询服务器 {GuildId} 按回复数排名的帖子失败", guildId);
            throw;
        }
    }
}
