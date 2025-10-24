using Npgsql;

using Dapper;

using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    public async Task SyncAllThreadsAsync(NpgsqlTransaction transaction, List<ThreadData> allThreads)
    {
        if (!allThreads.Any()) return;

        const string sqlCommand = """
            -- 步骤 1: 批量插入或更新帖子主数据 (posts_main)
            -- 使用 unnest 将所有帖子数据展开成行进行处理
            INSERT INTO posts_main (
                thread_id, first_message_id, author_id, channel_id, channel_name, 
                guild_id, guild_name, created_at, reaction_count, reply_count, 
                last_active_at, jump_url, attachment_url, 
                tags, is_valid, is_locked, in_forum,
                title, first_message_content, title_tokenized, content_tokenized
            )
            SELECT
                t.thread_id, t.first_message_id, t.author_id, t.channel_id, t.channel_name,
                t.guild_id, t.guild_name, t.created_at, t.reaction_count, t.reply_count,
                t.last_active_at, t.jump_url, t.attachment_url,
                t.tags, t.is_valid, t.is_locked, t.in_forum,
                t.title, t.first_message_content, t.title_tokenized, t.content_tokenized
            FROM unnest(
                @ThreadIds, @FirstMessageIds, @AuthorIds, @ChannelIds, @ChannelNames,
                @GuildIds, @GuildNames, @CreatedAts, @ReactionCounts, @ReplyCounts,
                @LastActiveAts, @JumpUrls, @AttachmentUrls,
                @Tags, @IsValids, @IsLockeds, @InForums,
                @Titles, @FirstMessageContents, @TitleTokenizeds, @ContentTokenizeds
            ) AS t(
                thread_id, first_message_id, author_id, channel_id, channel_name,
                guild_id, guild_name, created_at, reaction_count, reply_count,
                last_active_at, jump_url, attachment_url,
                tags, is_valid, is_locked, in_forum,
                title, first_message_content, title_tokenized, content_tokenized
            )
            ON CONFLICT (thread_id) DO UPDATE
            SET
                -- 只更新在扫描中可能发生变化的核心字段
                channel_name = EXCLUDED.channel_name,
                guild_name = EXCLUDED.guild_name,
                reaction_count = EXCLUDED.reaction_count,
                reply_count = EXCLUDED.reply_count,
                last_active_at = EXCLUDED.last_active_at,
                attachment_url = EXCLUDED.attachment_url,
                tags = EXCLUDED.tags,
                is_valid = EXCLUDED.is_valid,
                is_locked = EXCLUDED.is_locked,
                title = EXCLUDED.title,
                first_message_content = EXCLUDED.first_message_content,
                title_tokenized = EXCLUDED.title_tokenized,
                content_tokenized = EXCLUDED.content_tokenized;
                
            -- 步骤 2: 同步帖子与标签的关联关系 (post_tags_junction)
            -- 采用“先删后插”策略，确保关系总是最新的
            DELETE FROM post_tags_junction
            WHERE thread_id = ANY(@ThreadIds);

            INSERT INTO post_tags_junction (thread_id, tag_id, channel_id, created_at)
            SELECT
                t.thread_id,
                t.tag_id,
                t.channel_id,
                t.created_at
            FROM unnest(@JunctionThreadIds, @JunctionTagIds, @JunctionChannelIds, @JunctionCreatedAts) 
                AS t(thread_id, tag_id, channel_id, created_at)
            ON CONFLICT (thread_id, tag_id) DO NOTHING;

            -- 步骤 3: 确保每个帖子的作者都在成员列表 (post_members) 中
            -- 如果作者已存在（例如，通过实时事件），则不做任何事
            INSERT INTO post_members (
                user_id, thread_id, is_thread_owner, first_join_at, 
                last_join_at, message_count, last_message_id, last_message_at
            )
            SELECT
                t.author_id,
                t.thread_id,
                TRUE, -- 扫描时，我们只处理作者，所以 is_thread_owner 是 true
                t.created_at,
                t.created_at,
                1, -- 初始消息数为 1 (首楼消息)
                t.first_message_id,
                t.created_at
            FROM unnest(@ThreadIds, @AuthorIds, @CreatedAts, @FirstMessageIds)
                AS t(thread_id, author_id, created_at, first_message_id)
            ON CONFLICT (user_id, thread_id) DO NOTHING;
            """;

        const string deleteOrphanThreadsSql = """
            UPDATE posts_main 
            SET
                is_deleted = true,
                delete_at = NOW()
            WHERE
                -- 只处理扫描到的频道范围内的帖子
                channel_id = ANY(@ChannelIds) 
                -- 排除当前扫描到的帖子
                AND thread_id <> ALL(@ThreadIds)
                -- 避免重复软删除
                AND is_deleted = false;
            """;

        try
        {
            // 准备帖子标签关联数据 (Junction)
            // 需要将嵌套的 TagsId 列表扁平化
            var junctionThreadIds = new List<ulong>();
            var junctionTagIds = new List<ulong>();
            var junctionChannelIds = new List<ulong>();
            var junctionCreatedAts = new List<DateTimeOffset>();
            
            foreach (var thread in allThreads.Where(t => t.TagsId.Any()))
            {
                foreach (var tagId in thread.TagsId)
                {
                    junctionThreadIds.Add(thread.ThreadId);
                    junctionTagIds.Add(tagId);
                    junctionChannelIds.Add(thread.ChannelId);
                    junctionCreatedAts.Add(thread.CreatedAt);
                }
            }
    
            var parameters = new
            {
                // For posts_main and post_members
                ThreadIds = allThreads.Select(t => t.ThreadId).ToArray(),
                FirstMessageIds = allThreads.Select(t => t.FirstMessageId).ToArray(),
                AuthorIds = allThreads.Select(t => t.AuthorId).ToArray(),
                ChannelIds = allThreads.Select(t => t.ChannelId).ToArray(),
                ChannelNames = allThreads.Select(t => t.ChannelName).ToArray(),
                GuildIds = allThreads.Select(t => t.GuildId).ToArray(),
                GuildNames = allThreads.Select(t => t.GuildName).ToArray(),
                CreatedAts = allThreads.Select(t => t.CreatedAt).ToArray(),
                ReactionCounts = allThreads.Select(t => t.ReactionCount).ToArray(),
                ReplyCounts = allThreads.Select(t => t.ReplyCount).ToArray(),
                LastActiveAts = allThreads.Select(t => t.LastActiveAt).ToArray(),
                JumpUrls = allThreads.Select(t => t.JumpUrl).ToArray(),
                AttachmentUrls = allThreads.Select(t => t.AttachmentUrl).ToArray(),
                Tags = allThreads.Select(t => t.Tags).ToArray(),
                IsValids = allThreads.Select(t => t.IsValid).ToArray(),
                IsLockeds = allThreads.Select(t => t.IsLocked).ToArray(),
                InForums = allThreads.Select(t => t.InForum).ToArray(),
                Titles = allThreads.Select(t => t.Title).ToArray(),
                FirstMessageContents = allThreads.Select(t => t.FirstMessageContent).ToArray(),
                TitleTokenizeds = allThreads.Select(t => t.TitleTokenized).ToArray(),
                ContentTokenizeds = allThreads.Select(t => t.ContentTokenized).ToArray(),
    
                // For post_tags_junction
                JunctionThreadIds = junctionThreadIds.ToArray(),
                JunctionTagIds = junctionTagIds.ToArray(),
                JunctionChannelIds = junctionChannelIds.ToArray(),
                JunctionCreatedAts = junctionCreatedAts.ToArray()
            };
            await transaction.Connection.ExecuteAsync(sqlCommand, parameters, transaction);

            var uniqueChannelIds = allThreads.Select(t => t.ChannelId).Distinct().ToArray();

            await transaction.Connection.ExecuteAsync(deleteOrphanThreadsSql, new { ThreadIds = parameters.ThreadIds, ChannelIds = uniqueChannelIds }, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SyncAllThreadsAsync: 批量同步帖子数据到数据库的写入更新失败");
            throw;
        }
    }
}