// using System.Data;
// using Dapper;

// using LunariumBot.Net.DataCenter.Model;

// namespace LunariumBot.Net.DataCenter.Database;

// public partial class DatabaseService
// {
//     public async Task ForumThreadToDBAsync(ThreadData threadData, List<TagInfo> tagInfos)
//     {
//         const string SQLCommand = """
//             -- 步骤 1: 插入或更新频道信息 (UPSERT)
//             -- 如果 channel_id 已存在, 就更新它的名称信息, 确保信息最新。
//             INSERT INTO channels (channel_id, channel_name, guild_id, guild_name)
//             VALUES (@ChannelId, @ChannelName, @GuildId, @GuildName)
//             ON CONFLICT (channel_id) DO UPDATE 
//             SET 
//                 channel_name = EXCLUDED.channel_name, 
//                 guild_name = EXCLUDED.guild_name;

//             -- 步骤 2: 批量插入或更新该频道下的所有标签 (Batch UPSERT)
//             -- 使用 unnest 函数将传入的数组参数展开成行, 一次性处理所有标签。
//             INSERT INTO tags (tag_id, channel_id, tag_name, channel_name, guild_name)
//             SELECT 
//                 t.tag_id,
//                 @ChannelId, -- 所有这些标签都属于同一个频道
//                 t.tag_name,
//                 @ChannelName,
//                 @GuildName
//             FROM unnest(@AllTagIds, @AllTagNames) AS t(tag_id, tag_name)
//             ON CONFLICT (tag_id) DO UPDATE
//             SET
//                 tag_name = EXCLUDED.tag_name,
//                 channel_name = EXCLUDED.channel_name,
//                 guild_name = EXCLUDED.guild_name;

//             -- 步骤 3: 校准标签 - 删除该频道下已不存在的旧标签
//             -- tagInfos 是该频道的最新标签全集, 所以不在这个列表里的就是过时的。
//             -- 由于数据库表内已有 ON DELETE CASCADE, 删除 tag 时, post_tags_junction 里的关联会自动清理
//             DELETE FROM tags
//             WHERE channel_id = @ChannelId AND tag_id <> ALL(@AllTagIds);

//             -- 步骤 4: 插入新的帖子主数据
//             -- 这里就是常规的 INSERT, 帖子的 ID 是主键, 不应该冲突。
//             -- title 和 first_message_content 字段会被设计的触发器 T.2 自动处理成 tsvector, 插入原始内容就好。
//             INSERT INTO posts_main (
//                 thread_id, first_message_id, author_id, channel_id, channel_name, 
//                 guild_id, guild_name, created_at, member_count, reaction_count, 
//                 reply_count, last_active_at, jump_url, attachment_url, update_at, 
//                 update_jump_url, update_count, tags, is_valid, is_locked, is_deleted, 
//                 title, first_message_content, title_tokenized, content_tokenized
//             )
//             VALUES (
//                 @ThreadId, @FirstMessageId, @AuthorId, @ChannelId, @ChannelName, 
//                 @GuildId, @GuildName, @CreatedAt, @MemberCount, @ReactionCount, 
//                 @ReplyCount, @LastActiveAt, @JumpUrl, @AttachmentUrl, @UpdateAt, 
//                 @UpdateJumpUrl, @UpdateCount, @Tags, @IsValid, @IsLocked, @IsDeleted, 
//                 @Title, @FirstMessageContent, @TitleTokenized, @ContentTokenized
//             );

//             -- 步骤 5: 批量插入帖子与应用标签的关系
//             -- 同样使用 unnest, 将这个新帖子与它所拥有的标签ID一一对应并插入关系表。
//             INSERT INTO post_tags_junction (thread_id, tag_id, channel_id)
//             SELECT 
//                 @ThreadId,          -- 当前帖子的 ID
//                 tag_id,             -- 从 UNNEST 得到的每个标签ID
//                 @ChannelId          -- 冗余存储 channel_id
//             FROM UNNEST(@AppliedTagIds) AS tag_id
//             ON CONFLICT (thread_id, tag_id) DO NOTHING;

//             -- 步骤 6: 更新用户统计数据
//             -- 创建一个新帖子, 也更新作者的统计数据
//             INSERT INTO user_data (user_id, user_thread_count, user_publish_count)
//             VALUES (@AuthorId, 1, 1)
//             ON CONFLICT (user_id) DO UPDATE
//             SET
//                 user_thread_count = user_data.user_thread_count + 1,
//                 user_publish_count = user_data.user_publish_count + 1;
//             """;
//         try
//         {
//             await using Npgsql.NpgsqlConnection? connection = GetConnection();
//             await connection.OpenAsync();
//             await using System.Data.Common.DbTransaction? transaction = await connection.BeginTransactionAsync();
//             var parameters = new
//             {
//                 // From threadData
//                 threadData.ThreadId,
//                 threadData.FirstMessageId,
//                 threadData.AuthorId,
//                 threadData.ChannelId,
//                 threadData.ChannelName,
//                 threadData.GuildId,
//                 threadData.GuildName,
//                 threadData.CreatedAt,
//                 threadData.MemberCount,
//                 threadData.ReactionCount,
//                 threadData.ReplyCount,
//                 threadData.LastActiveAt,
//                 threadData.JumpUrl,
//                 threadData.AttachmentUrl,
//                 threadData.UpdateAt,
//                 threadData.UpdateJumpUrl,
//                 threadData.UpdateCount,
//                 threadData.Tags,
//                 threadData.IsValid,
//                 threadData.IsLocked,
//                 threadData.IsDeleted,
//                 threadData.Title,
//                 threadData.FirstMessageContent,
//                 threadData.TitleTokenized,
//                 threadData.ContentTokenized,

//                 // From threadData.TagsId for step 5
//                 // Dapper 会自动将 List<ulong> 映射为 PostgreSQL 的 BIGINT[] 数组
//                 AppliedTagIds = threadData.TagsId,

//                 // From tagInfos for steps 2 and 3
//                 // 需要从 List<TagInfo> 中提取出 ID 和 Name 的数组
//                 AllTagIds = tagInfos.Select(t => t.TagId).ToArray(),
//                 AllTagNames = tagInfos.Select(t => t.TagName).ToArray()
//             };
//             try
//             {
//                 // 执行
//                 await connection.ExecuteAsync(SQLCommand, parameters, transaction);
//                 // 提交事务
//                 await transaction.CommitAsync();
//                 _logger.Info("成功创建帖子 {ThreadId} 并完整更新了数据库记录。", threadData.ThreadId);
//             }
//             catch (Exception ex)
//             {
//                 // 回滚
//                 await transaction.RollbackAsync();
//                 _logger.Error(ex, "处理新帖子 {ThreadId} 时数据库操作失败！已回滚事务。", threadData.ThreadId);
//                 throw;
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "ForumThreadToDBAsync");
//             throw;
//         }
//     }
    
//     public async Task ForumThreadUpdateToDBAsync(ThreadData threadData, List<TagInfo> tagInfos)
//     {
//         const string SQLCommand = """
//             -- 步骤 1: 更新或插入频道信息 (UPSERT)
//             -- 确保频道信息是最新的
//             INSERT INTO channels (channel_id, channel_name, guild_id, guild_name)
//             VALUES (@ChannelId, @ChannelName, @GuildId, @GuildName)
//             ON CONFLICT (channel_id) DO UPDATE 
//             SET 
//                 channel_name = EXCLUDED.channel_name, 
//                 guild_name = EXCLUDED.guild_name;

//             -- 步骤 2: 批量更新或插入该频道下的所有标签 (Batch UPSERT)
//             -- 使用 unnest 函数将传入的数组参数展开成行，一次性处理所有标签
//             INSERT INTO tags (tag_id, channel_id, tag_name, channel_name, guild_name)
//             SELECT 
//                 t.tag_id,
//                 @ChannelId,
//                 t.tag_name,
//                 @ChannelName,
//                 @GuildName
//             FROM unnest(@AllTagIds, @AllTagNames) AS t(tag_id, tag_name)
//             ON CONFLICT (tag_id) DO UPDATE
//             SET
//                 tag_name = EXCLUDED.tag_name,
//                 channel_name = EXCLUDED.channel_name,
//                 guild_name = EXCLUDED.guild_name;

//             -- 步骤 3: 校准标签 - 删除该频道下已不存在的旧标签
//             -- tagInfos 是该频道的最新标签全集，所以不在这个列表里的就是过时的
//             -- 由于数据库表内已有 ON DELETE CASCADE，删除 tag 时，post_tags_junction 里的关联会自动清理
//             DELETE FROM tags
//             WHERE channel_id = @ChannelId AND tag_id <> ALL(@AllTagIds);

//             -- 步骤 4: 更新帖子主数据
//             -- 只更新特定字段：title, title_tokenized, is_valid, is_locked
//             UPDATE posts_main 
//             SET 
//                 title = @Title,
//                 title_tokenized = @TitleTokenized,
//                 is_valid = @IsValid,
//                 is_locked = @IsLocked
//             WHERE 
//                 thread_id = @ThreadId;

//             -- 步骤 5: 清理旧的标签关系
//             -- 删除该帖子所有现有的标签关系，准备插入新的
//             DELETE FROM post_tags_junction 
//             WHERE thread_id = @ThreadId;

//             -- 步骤 6: 批量插入帖子与新标签的关系
//             -- 使用 unnest 将这个帖子与它所拥有的标签ID一一对应并插入关系表
//             INSERT INTO post_tags_junction (thread_id, tag_id, channel_id)
//             SELECT 
//                 @ThreadId,          -- 当前帖子的 ID
//                 tag_id,             -- 从 UNNEST 得到的每个标签ID
//                 @ChannelId          -- 冗余存储 channel_id
//             FROM UNNEST(@AppliedTagIds) AS tag_id
//             ON CONFLICT (thread_id, tag_id) DO NOTHING;
//             """;

//         try
//         {
//             await using Npgsql.NpgsqlConnection? connection = GetConnection();
//             await connection.OpenAsync();
//             await using System.Data.Common.DbTransaction? transaction = await connection.BeginTransactionAsync();
            
//             var parameters = new
//             {
//                 // From threadData - 只包含需要更新的字段
//                 threadData.ThreadId,
//                 threadData.ChannelId,
//                 threadData.ChannelName,
//                 threadData.GuildId,
//                 threadData.GuildName,
//                 threadData.Title,
//                 threadData.TitleTokenized,
//                 threadData.IsValid,
//                 threadData.IsLocked,

//                 // From threadData.TagsId for step 6
//                 // Dapper 会自动将 List<ulong> 映射为 PostgreSQL 的 BIGINT[] 数组
//                 AppliedTagIds = threadData.TagsId,

//                 // From tagInfos for steps 2 and 3
//                 // 需要从 List<TagInfo> 中提取出 ID 和 Name 的数组
//                 AllTagIds = tagInfos.Select(t => t.TagId).ToArray(),
//                 AllTagNames = tagInfos.Select(t => t.TagName).ToArray()
//             };

//             try
//             {
//                 // 执行
//                 await connection.ExecuteAsync(SQLCommand, parameters, transaction);
//                 // 提交事务
//                 await transaction.CommitAsync();
//                 _logger.Info("成功更新帖子 {ThreadId} 并完整更新了数据库记录。", threadData.ThreadId);
//             }
//             catch (Exception ex)
//             {
//                 // 回滚
//                 await transaction.RollbackAsync();
//                 _logger.Error(ex, "更新帖子 {ThreadId} 时数据库操作失败！已回滚事务。", threadData.ThreadId);
//                 throw;
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "ForumThreadUpdateToDBAsync");
//             throw;
//         }
//     }

//     public async Task UpdateThreadReactionToDBAsync(ulong threadId, int reactionCount)
//     {
//         const string SQLCommand = """
//             UPDATE posts_main
//             SET 
//                 reaction_count = @ReactionCount,
//             WHERE 
//                 thread_id = @ThreadId;
//             """;

//         try
//         {
//             await using Npgsql.NpgsqlConnection? connection = GetConnection();
//             await connection.OpenAsync();
//             var parameters = new
//             {
//                 ThreadId = threadId,
//                 ReactionCount = reactionCount
//             };

//             await connection.ExecuteAsync(SQLCommand, parameters);
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "更新帖子反应 {ThreadId} 时发生数据库错误。", threadId);
//             throw;
//         }
//     }

//     /// <summary>
//     /// 软删除一个帖子。
//     /// 这将通过更新 is_deleted 标志来触发数据库中的 handle_post_soft_delete 触发器，
//     /// 该触发器会处理所有相关的级联软删除和清理操作。
//     /// </summary>
//     /// <param name="threadId">要删除的帖子的ID</param>
//     /// <returns>受影响的行数 (成功则为1, 否则为0)。</returns>
//     public async Task<int> SoftDeleteThreadAsync(ulong threadId)
//     {
//         const string SQLCommand = """
//             UPDATE posts_main
//             SET 
//                 is_deleted = TRUE,
//                 delete_at = NOW() -- 记录删除时间
//             WHERE 
//                 thread_id = @ThreadId AND is_deleted = FALSE;
//             """;

//         try
//         {
//             await using Npgsql.NpgsqlConnection? connection = GetConnection();
//             await connection.OpenAsync();
//             var parameters = new { ThreadId = threadId };

//             int affectedRows = await connection.ExecuteAsync(SQLCommand, parameters);

//             if (affectedRows > 0)
//             {
//                 _logger.Info("成功软删除了帖子 {ThreadId}。", threadId);
//             }
//             else
//             {
//                 _logger.Warning("尝试软删除帖子 {ThreadId}，但该帖子未找到或已被删除。", threadId);
//             }

//             return affectedRows;
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "软删除帖子 {ThreadId} 时发生数据库错误。", threadId);
//             throw;
//         }
//     }
// }