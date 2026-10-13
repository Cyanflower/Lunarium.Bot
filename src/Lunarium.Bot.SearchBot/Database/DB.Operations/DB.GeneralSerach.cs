using Npgsql;
using Dapper;
using Lunarium.Bot.SearchBot.Models;
using JiebaNet.Segmenter.Common;
using System.Text;
using Lunarium.Bot.SearchBot.Models.Database;
using Lunarium.Bot.Extensions;


namespace Lunarium.Bot.SearchBot.Database;

public partial class DatabaseService
{
    public async Task<IReadOnlyList<PostDisplayModel>> GeneralSearch(SearchConditions searchConditions)
    {
        const int ResultLimit = 1000;
        const string SqlCommandHead = """
            SELECT
                thread_id AS ThreadId,
                first_message_id AS FirstMessageId,

                title AS Title,
                first_message_content AS Content,
                jump_url AS JumpUrl,
                updated_jump_url AS UpdateMessageJumpUrl,
                attachment_url::jsonb AS AttachmentUrl,

                author_id AS AuthorId,
                channel_id AS ChannelId,
                channel_name AS ChannelName,
                guild_id AS GuildId,
                guild_name AS GuildName,

                reaction_count AS ReactionCount,
                reply_count AS ReplyCount,

                created_at AS CreatedAt,
                last_active_at AS LastActiveAt,
                updated_at AS UpdatedAt,

                tags AS Tags,

                is_valid AS IsFallbackData
            """;

        StringBuilder selectBuilder = new(SqlCommandHead);
        StringBuilder fromBuilder = new("FROM");
        StringBuilder whereBuilder = new("""
            WHERE
                is_valid = true
                AND is_locked = false
                AND is_deleted = false
                AND in_forum = true
            """);
        StringBuilder orderBuilder = new();
        DynamicParameters parameters = new DynamicParameters();
        bool hasTsRank = false;

        // ============= 全文搜索 与 SELECT&FROM ============
        if (searchConditions.KeywordConditions is not null)
        {
            StringBuilder keywords = new();
            if (searchConditions.KeywordConditions.IncludeKeywords.Count > 0)
            {
                string searchMode = " & ";
                switch (searchConditions.KeywordConditions.SearchMode)
                {
                    case SearchMode.Exact:
                        searchMode = " & ";
                        break;
                    case SearchMode.Fuzzy:
                        searchMode = " | ";
                        break;
                }
                keywords.Append(searchConditions.KeywordConditions.IncludeKeywords.Join(searchMode));
                // 为 is_valid AS IsFallbackData 补上`,`
                selectBuilder.Append(',');
                // 补上相关性评分
                selectBuilder.AppendLine("    ts_rank(search_vector, query) AS rank");
                hasTsRank = true;
            }
            if (searchConditions.KeywordConditions.ExcludeKeywords.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(keywords.ToString()))
                {
                    // 如果没有 IncludeGroup 的内容
                    // 直接添加排除内容
                    keywords.Append(searchConditions.KeywordConditions.ExcludeKeywords.Select(k => $"!{k}").Join("&"));
                }
                else
                {
                    // 添加 and
                    keywords.Append(" & ");
                    keywords.Append(searchConditions.KeywordConditions.ExcludeKeywords.Select(k => $"!{k}").Join("&"));
                }
            }
            fromBuilder.Append(" posts_main, to_tsquery('simple', '@FTS') query");
            whereBuilder.AppendLine("    AND search_vector @@ query");
            parameters.Add("FTS", keywords.ToString());
        }
        else
        {
            fromBuilder.Append(" posts_main");
        }
        // ============= Conditions ============
        // ==== Tag ====
        if (searchConditions.TagConditions is not null)
        {
            if (searchConditions.TagConditions.IncludeTagIds.Count > 0)
            {
                switch (searchConditions.TagConditions.SearchMode)
                {
                    case SearchMode.Exact:
                        whereBuilder.AppendLine("""
                            AND (
                                SELECT COUNT(DISTINCT ptj.tag_id)
                                FROM post_tags_junction ptj
                                WHERE ptj.thread_id = posts_main.thread_id
                                AND ptj.tag_id = ANY(@IncludeTagIds)
                            ) = @IncludeTagIdsCount
                        """);
                        parameters.Add("IncludeTagIds", searchConditions.TagConditions.IncludeTagIds);
                        parameters.Add("IncludeTagIdsCount", searchConditions.TagConditions.IncludeTagIds.Count);
                        break;
                    case SearchMode.Fuzzy:
                        whereBuilder.AppendLine("""
                            AND EXISTS (
                                SELECT 1 
                                FROM post_tags_junction ptj
                                WHERE ptj.thread_id = posts_main.thread_id
                                AND ptj.tag_id = ANY(@IncludeTagIds)
                            )
                        """);
                        parameters.Add("IncludeTagIds", searchConditions.TagConditions.IncludeTagIds);
                        break;
                }
            }
            if (searchConditions.TagConditions.ExcludeTagIds.Count > 0)
            {
                whereBuilder.AppendLine("""
                    AND EXISTS (
                        SELECT 1 
                        FROM post_tags_junction ptj
                        WHERE ptj.thread_id = posts_main.thread_id
                        AND ptj.tag_id = ANY(@ExcludeTagIds)
                    )
                """);
                parameters.Add("ExcludeTagIds", searchConditions.TagConditions.ExcludeTagIds);
            }
        }
        // ==== 作者 ====
        if (searchConditions.AuthorIds.Count > 0)
        {
            whereBuilder.AppendLine("""
                    AND author_id = ANY(@AuthorIds)
                """);
            parameters.Add("AuthorIds", searchConditions.AuthorIds);
        }
        if (searchConditions.CurrentUserId.HasValue)
        {
            whereBuilder.AppendLine("""
                AND author_id NOT IN (
                    SELECT blocked_author_id 
                    FROM user_blocked_authors 
                    WHERE user_id = @CurrentUserId
                )
            """);
            parameters.Add("CurrentUserId", searchConditions.CurrentUserId.Value);
        }
        // ==== 频道分类 ====

        if (searchConditions.ChannelIds.Count > 0)
        {
            whereBuilder.AppendLine("""
                    AND channel_id = ANY(@ChannelIds)
                """);
            parameters.Add("ChannelIds", searchConditions.ChannelIds);
        }
        // ============= ORDER ============
        switch (searchConditions.PostSortOption)
        {
            case PostSortOption.ReactionAsc:
                orderBuilder.AppendLine("""
                ORDER BY reaction_count ASC
                """);
                break;
            case PostSortOption.ReactionDesc:
                orderBuilder.AppendLine("""
                ORDER BY reaction_count DESC
                """);
                break;
            case PostSortOption.ReplyCountAsc:
                orderBuilder.AppendLine("""
                ORDER BY reply_count ASC
                """);
                break;
            case PostSortOption.ReplyCountDesc:
                orderBuilder.AppendLine("""
                ORDER BY reply_count DESC
                """);
                break;
            case PostSortOption.CreatedTimeAsc:
                orderBuilder.AppendLine("""
                ORDER BY created_at ASC
                LIMIT 1000;
                """);
                break;
            case PostSortOption.CreatedTimeDesc:
                orderBuilder.AppendLine("""
                ORDER BY created_at DESC
                """);
                break;
            case PostSortOption.UpdateTimeAsc:
                orderBuilder.AppendLine("""
                ORDER BY COALESCE(updated_at, created_at) ASC
                """);
                break;
            case PostSortOption.UpdateTimeDesc:
                orderBuilder.AppendLine("""
                ORDER BY COALESCE(updated_at, created_at) DESC
                """);
                break;
            case PostSortOption.RelevanceDesc:
                if (hasTsRank)
                {
                    orderBuilder.AppendLine("""
                    ORDER BY rank DESC
                    """);
                }
                else
                {
                    orderBuilder.AppendLine("""
                    ORDER BY created_at DESC
                    """);
                }
                break;
        }

        var sqlCommand = $"""
            {selectBuilder}
            {fromBuilder}
            {whereBuilder}
            {orderBuilder}
            LIMIT {ResultLimit};
            """;
        List<PostDisplayModel> postDisplayModels = new();
        try
        {
            using var connection = GetConnection();
            postDisplayModels = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                var results = await connection.QueryAsync<PostDisplayModelDb>(sqlCommand, parameters);
                return results.Select(result => result.ToDomain()).ToList();
            });
            return postDisplayModels;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GeneralSearch Exception");
            throw;
        }
    }
    
    public async Task<IReadOnlyList<PostDisplayModel>> SearchForCache(List<ulong> threadIds)
    {
        const string SqlCommand = """
            SELECT
                thread_id AS ThreadId,
                first_message_id AS FirstMessageId,

                title AS Title,
                first_message_content AS Content,
                jump_url AS JumpUrl,
                updated_jump_url AS UpdateMessageJumpUrl,
                attachment_url::jsonb AS AttachmentUrl,

                author_id AS AuthorId,
                channel_id AS ChannelId,
                channel_name AS ChannelName,
                guild_id AS GuildId,
                guild_name AS GuildName,

                reaction_count AS ReactionCount,
                reply_count AS ReplyCount,

                created_at AS CreatedAt,
                last_active_at AS LastActiveAt,
                updated_at AS UpdatedAt,

                tags AS Tags,

                is_valid AS IsFallbackData
            FROM posts_main
            WHERE thread_id = ANY(@ThreadIds);
            """;
        var parameters = new
        {
            ThreadIds = threadIds.Select(id => id.ToDbId()).ToArray()
        };
        try
        {
            using var connection = GetConnection();
            var postDisplayModels = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                var results = await connection.QueryAsync<PostDisplayModelDb>(SqlCommand, parameters);
                return results;
            });
            return postDisplayModels.Select(data => data.ToDomain()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "SearchForCache Exception");
            throw;
        }
    }
}