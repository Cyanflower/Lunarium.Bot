using Dapper;
using Microsoft.Data.Sqlite;
using SearchBot.Model;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // SQL模板: 根据名字查询所有同名tag的ID
    const string getTagIdsByNamesSql = "SELECT tag_name AS TagName, tag_id AS TagId FROM tags WHERE tag_name IN @Names;";

    const string getUserQuickSearchesSql = "SELECT user_id AS UserId, command_name AS CommandName, criteria_json AS CriteriaJson FROM user_fast_search_command WHERE user_id = @UserId ORDER BY command_name;";

    /// <summary>
    /// 根据一组标签名称，查询它们对应的所有标签ID。
    /// 这是为了解决跨频道/服务器存在同名标签的问题。
    /// </summary>
    /// <param name="names">标签名称列表</param>
    /// <returns>一个字典，键是标签名，值是所有该名称对应的ID列表</returns>
    public async Task<Dictionary<string, List<ulong>>> GetTagIdsByNamesAsync(List<string> names)
    {
        if (names == null || !names.Any())
        {
            return new Dictionary<string, List<ulong>>();
        }

        using var connection = new SqliteConnection(_connectionString);
        var results = await connection.QueryAsync<(string TagName, ulong TagId)>(getTagIdsByNamesSql, new { Names = names });

        // 将扁平的结果分组为字典
        return results
            .GroupBy(r => r.TagName)
            .ToDictionary(g => g.Key, g => g.Select(item => item.TagId).ToList());
    }

    /// <summary>
    /// 根据复杂的搜索条件动态构建SQL并查询帖子。
    /// </summary>
    /// <param name="criteria">已解析的搜索条件对象</param>
    /// <returns>符合条件的帖子列表</returns>
    public async Task<List<PostDisplayModel>> SearchPostsAsync(SearchCriteria criteria)
    {
        var sqlBuilder = new StringBuilder("""
            SELECT 
                p.thread_id as ThreadId, 
                p.first_message_id as FirstMessageId, 
                p.author_id as AuthorId, 
                p.channel_id as ChannelId, 
                p.channel_name as ChannelName, 
                p.guild_id as GuildId, 
                p.guild_name as GuildName, 
                p.created_at as CreatedAt, 
                p.member_count as MemberCount, 
                p.reaction_count as ReactionCount, 
                p.reply_count as ReplyCount, 
                p.last_active_at as LastActiveAt, 
                p.jump_url as JumpUrl, 
                p.attachment_url as AttachmentUrl, 
                p.update_at as UpdatedAt,
                p.message_jump_url as MessageJumpUrl,
                p.tags as Tags, 
                p.title as Title, 
                p.content as Content
            FROM posts_main p 
            WHERE 1=1 
            """
        );
        var parameters = new DynamicParameters();

        // --- 基本过滤 ---
        if (criteria.ForumChannelIds.Any())
        {
            sqlBuilder.Append("AND p.channel_id IN @ForumChannelIds ");
            parameters.Add("ForumChannelIds", criteria.ForumChannelIds);
        }
        if (criteria.AuthorId.HasValue)
        {
            sqlBuilder.Append("AND p.author_id = @AuthorId ");
            parameters.Add("AuthorId", criteria.AuthorId.Value);
        }
        if (criteria.ExcludeAuthorIds.Any())
        {
            sqlBuilder.Append("AND p.author_id NOT IN @ExcludeAuthorIds ");
            parameters.Add("ExcludeAuthorIds", criteria.ExcludeAuthorIds);
        }

        // --- 时间范围 ---
        if (criteria.StartTimestamp.HasValue)
        {
            sqlBuilder.Append("AND p.created_at >= @StartTimestamp ");
            parameters.Add("StartTimestamp", criteria.StartTimestamp.Value);
        }
        if (criteria.EndTimestamp.HasValue)
        {
            sqlBuilder.Append("AND p.created_at <= @EndTimestamp ");
            parameters.Add("EndTimestamp", criteria.EndTimestamp.Value);
        }

        // --- 关键词 (FTS) ---
        if (criteria.IncludeKeywords.Any())
        {
            var ftsQuery = string.Join(" OR ", criteria.IncludeKeywords.Select(group => $"({string.Join(" ", group.Select(kw => $"\"{kw}\""))})"));
            sqlBuilder.Append("AND p.thread_id IN (SELECT rowid FROM posts_fts WHERE posts_fts MATCH @FtsQuery) ");
            parameters.Add("FtsQuery", ftsQuery);
        }
        if (criteria.ExcludeKeywords.Any())
        {
            var ftsExcludeQuery = string.Join(" OR ", criteria.ExcludeKeywords.Select(group => $"({string.Join(" ", group.Select(kw => $"\"{kw}\""))})"));
            sqlBuilder.Append("AND p.thread_id NOT IN (SELECT rowid FROM posts_fts WHERE posts_fts MATCH @FtsExcludeQuery) ");
            parameters.Add("FtsExcludeQuery", ftsExcludeQuery);
        }

        // --- 标签 ---
        if (criteria.IncludeTagsAnd.Any())
        {
            foreach (var tagId in criteria.IncludeTagsAnd)
            {
                sqlBuilder.Append($"AND EXISTS (SELECT 1 FROM post_tags_junction j WHERE j.thread_id = p.thread_id AND j.tag_id = {tagId}) ");
            }
        }
        if (criteria.IncludeTagsOr.Any())
        {
            sqlBuilder.Append("AND EXISTS (SELECT 1 FROM post_tags_junction j WHERE j.thread_id = p.thread_id AND j.tag_id IN @IncludeTagsOr) ");
            parameters.Add("IncludeTagsOr", criteria.IncludeTagsOr);
        }
        if (criteria.ExcludeTags.Any())
        {
            sqlBuilder.Append("AND NOT EXISTS (SELECT 1 FROM post_tags_junction j WHERE j.thread_id = p.thread_id AND j.tag_id IN @ExcludeTags) ");
            parameters.Add("ExcludeTags", criteria.ExcludeTags);
        }

        // --- 排序 ---
        switch (criteria.OrderBy)
        {
            case "posted_date_asc": sqlBuilder.Append("ORDER BY p.created_at ASC "); break;
            case "last_active_desc": sqlBuilder.Append("ORDER BY p.last_active_at DESC "); break;
            case "last_active_asc": sqlBuilder.Append("ORDER BY p.last_active_at ASC "); break;
            case "reactions_desc": sqlBuilder.Append("ORDER BY p.reaction_count DESC "); break;
            case "reactions_asc": sqlBuilder.Append("ORDER BY p.reaction_count ASC "); break;
            case "replies_desc": sqlBuilder.Append("ORDER BY p.reply_count DESC "); break;
            case "replies_asc": sqlBuilder.Append("ORDER BY p.reply_count ASC "); break;
            default: sqlBuilder.Append("ORDER BY p.created_at DESC "); break; // 默认
        }

        // --- 限制数量 ---
        sqlBuilder.Append("LIMIT @ResultLimit");
        parameters.Add("ResultLimit", criteria.ResultLimit);

        using var connection = new SqliteConnection(_connectionString);
        var result = await connection.QueryAsync<PostDisplayModel>(sqlBuilder.ToString(), parameters);
        return result.ToList();
    }

    /// <summary>
    /// 获取指定用户的所有快捷搜索命令。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>快捷命令列表</returns>
    public async Task<List<QuickSearchCommandModel>> GetUserQuickSearchesAsync(ulong userId)
    {
        using var connection = new SqliteConnection(_connectionString);
        var result = await connection.QueryAsync<QuickSearchCommandModel>(getUserQuickSearchesSql, new { UserId = userId });
        return result.ToList();
    }
}