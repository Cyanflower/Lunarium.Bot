using Npgsql;

using Dapper;

using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    public async Task InsertThreadAsync(NpgsqlTransaction transaction, ThreadData threadData)
    {
        const string SqlCommand = """
            -- 步骤 1: 插入新的帖子主数据
            -- 这里就是常规的 INSERT, 帖子的 ID 是主键, 不应该冲突。
            -- title 和 first_message_content 字段会被设计的触发器 T.2 自动处理成 tsvector, 插入原始内容就好。
            INSERT INTO posts_main (
                thread_id, first_message_id, author_id, channel_id, channel_name, 
                guild_id, guild_name, created_at, member_count, reaction_count, 
                reply_count, last_active_at, jump_url, attachment_url, update_at, 
                update_jump_url, update_count, tags, is_valid, is_locked, is_deleted, 
                title, first_message_content, title_tokenized, content_tokenized
            )
            VALUES (
                @ThreadId, @FirstMessageId, @AuthorId, @ChannelId, @ChannelName, 
                @GuildId, @GuildName, @CreatedAt, @MemberCount, @ReactionCount, 
                @ReplyCount, @LastActiveAt, @JumpUrl, @AttachmentUrl, @UpdateAt, 
                @UpdateJumpUrl, @UpdateCount, @Tags, @IsValid, @IsLocked, @IsDeleted, 
                @Title, @FirstMessageContent, @TitleTokenized, @ContentTokenized
            );
            """;

        var parameters = new
        {
            threadData.ThreadId,
            threadData.FirstMessageId,
            threadData.AuthorId,
            threadData.ChannelId,
            threadData.ChannelName,
            threadData.GuildId,
            threadData.GuildName,
            threadData.CreatedAt,
            threadData.MemberCount,
            threadData.ReactionCount,
            threadData.ReplyCount,
            threadData.LastActiveAt,
            threadData.JumpUrl,
            threadData.AttachmentUrl,
            threadData.UpdateAt,
            threadData.UpdateJumpUrl,
            threadData.UpdateCount,
            threadData.Tags,
            threadData.IsValid,
            threadData.IsLocked,
            threadData.IsDeleted,
            threadData.Title,
            threadData.FirstMessageContent,
            threadData.TitleTokenized,
            threadData.ContentTokenized,
        };
        try
        {
            // 执行
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertThreadAsync: 插入帖子 {ThreadName}({ThreadId}) 时数据库操作失败！", threadData.Title, threadData.ThreadId);
            throw;
        }
    }

    public async Task InsertTagsJunction(NpgsqlTransaction transaction, ThreadData threadData)
    {
        const string SqlCommand = """
            -- 批量插入帖子与应用标签的关系
            -- 使用 unnest, 将这个新帖子与它所拥有的标签ID一一对应并插入关系表。
            INSERT INTO post_tags_junction (thread_id, tag_id, channel_id, created_at)
            SELECT 
                @ThreadId,
                tag_id,
                @ChannelId,
                @CreatedAt
            FROM UNNEST(@AppliedTagIds) AS tag_id
            ON CONFLICT (thread_id, tag_id) DO NOTHING;
            """;
        var parameters = new
        {
            threadData.ThreadId,
            threadData.ChannelId,
            threadData.CreatedAt,
            AppliedTagIds = threadData.TagsId.ToArray(),
        };
        try
        {
            // 执行
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertTagsJunction: 插入帖子 {ThreadName}({ThreadId}) 的 Tags 关系时数据库操作失败！", threadData.Title, threadData.ThreadId);
            throw;
        }
    }
    
    public async Task UpdateThreadAsync(NpgsqlTransaction transaction, ThreadData threadData)
    {
        const string SQLCommand = """
            -- 更新帖子主数据
            -- 只更新特定字段：title, title_tokenized, is_valid, is_locked
            UPDATE posts_main 
            SET 
                tags = Tags,
                title = @Title,
                title_tokenized = @TitleTokenized,
                is_valid = @IsValid,
                is_locked = @IsLocked
            WHERE 
                thread_id = @ThreadId;
            """;

        var parameters = new
        {
            threadData.ThreadId,
            threadData.Tags,
            threadData.Title,
            threadData.TitleTokenized,
            threadData.IsValid,
            threadData.IsLocked
        };

        try
        {
            // 执行
            await transaction.Connection.ExecuteAsync(SQLCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ UpsertThreadAsync: 更新帖子 {ThreadName}({ThreadId})  时数据库操作失败！", threadData.Title, threadData.ThreadId);
            throw;
        }
    }

    public async Task ReplaceTagsJunction(NpgsqlTransaction transaction, ThreadData threadData)
    {
        const string SqlCommand = """
            -- 删除旧标签关系
            DELETE FROM post_tags_junction
            WHERE thread_id = @ThreadId;

            -- 批量插入新的帖子与应用标签的关系
            -- 使用 unnest, 将这个新帖子与它所拥有的标签ID一一对应并插入关系表。
            INSERT INTO post_tags_junction (thread_id, tag_id, channel_id, created_at)
            SELECT 
                @ThreadId,
                tag_id,
                @ChannelId,
                @CreatedAt
            FROM UNNEST(@AppliedTagIds) AS tag_id
            ON CONFLICT (thread_id, tag_id) DO NOTHING;
            """;
        var parameters = new
        {
            threadData.ThreadId,
            threadData.ChannelId,
            threadData.CreatedAt,
            AppliedTagIds = threadData.TagsId.ToArray(),
        };
        try
        {
            // 执行
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ ForumThreadToDBAsync: 插入帖子 {ThreadName}({ThreadId}) 的 Tags 关系时数据库操作失败！", threadData.Title, threadData.ThreadId);
            throw;
        }
    }

    public async Task UpdateThreadReactionAsync(NpgsqlTransaction transaction, ulong threadId, NumericUpdateType reactionUpdateType)
    {
        const string UpdateReactionCountSqlCommand = """
            UPDATE posts_main
            SET 
                reaction_count = reaction_count + @Amount
            WHERE 
                thread_id = @ThreadId;
            """;
        try
        {
            int amount = reactionUpdateType switch
            {
                NumericUpdateType.Increase => 1,
                NumericUpdateType.Decrease => -1,
                _ => throw new ArgumentOutOfRangeException(nameof(reactionUpdateType), $"未知枚举类型: {reactionUpdateType}")
            };

            var parameters = new
            {
                ThreadId = threadId,
                Amount = amount
            };

            await transaction.Connection.ExecuteAsync(UpdateReactionCountSqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ UpdateThreadReactionAsync: 更新帖子 {ThreadId} 反应记数时发生数据库错误。", threadId);
            throw;
        }
    }
    
    public async Task UpsertPostMemberAsync(NpgsqlTransaction transaction, PostMemberData postMemberData)
    {
        const string SqlCommand = """
            INSERT INTO post_members (
                user_id, user_name, thread_id, thread_name, 
                is_thread_owner, first_join_at, last_join_at,
                message_count, is_leave,
                last_message_id, last_message_at
                -- `last_leave_at` 字段在插入时不提供，默认为 NULL
            )
            VALUES (
                @UserId, @UserName, @ThreadId, @ThreadName,
                @IsThreadOwner, @FirstJoinAt, @LastJoinAt,
                1, false, -- 首次加入，消息数为1，is_leave为false
                @LastMessageId, @LastMessageAt
            )
            -- 冲突的目标是 (user_id, thread_id) 这个联合唯一键
            ON CONFLICT (user_id, thread_id) DO UPDATE 
            SET
                -- 无论如何都更新这些字段
                user_name = EXCLUDED.user_name,           -- 同步最新的用户名
                thread_name = EXCLUDED.thread_name,       -- 同步最新的帖子名
                last_message_id = EXCLUDED.last_message_id, -- 更新为最新的消息
                last_message_at = EXCLUDED.last_message_at, -- 更新为最新的消息时间

                -- 使用 CASE 语句来处理不同情况
                
                -- 如果旧记录是“已离开”状态，那么这次就是“重新加入”
                -- 需要将 is_leave 设回 false，并更新 last_join_at
                is_leave = false,
                last_join_at = CASE 
                                WHEN post_members.is_leave = true -- 当之前是离开状态时
                                THEN EXCLUDED.last_join_at       -- 更新“最后加入时间”
                                ELSE post_members.last_join_at   -- 否则，保持原来的“最后加入时间”不变
                            END;
            """;

        var parameters = new
        {
            postMemberData.UserId,
            postMemberData.UserName,
            postMemberData.ThreadId,
            postMemberData.ThreadName,
            postMemberData.IsThreadOwner,
            postMemberData.FirstJoinAt,
            postMemberData.LastJoinAt,
            // postMemberData.LeaveAt,
            // postMemberData.IsLeave,
            // postMemberData.MessageCount,
            postMemberData.LastMessageId,
            postMemberData.LastMessageAt
        };
        try
        {
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertTagsJunction: 为帖子 {ThreadId} 插入或更新用户 {UserId} 的数据时数据库操作失败！", postMemberData.ThreadId, postMemberData.UserId);
            throw;
        }
    }

    public async Task RemovePostMemberAsync(NpgsqlTransaction transaction, ulong userId, ulong threadId)
    {
        const string SqlCommand = """
            UPDATE post_members 
            SET
                is_leave = true,
                last_leave_at = NOW()
            WHERE 
                user_id = @UserId AND thread_id = @ThreadId;
            """;

        var parameters = new
        {
            UserId = userId,
            ThreadId = threadId
        };

        try
        {
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertTagsJunction: 从帖子 {ThreadId} 软删除用户 {UserId} 时数据库操作失败！", threadId, userId);
            throw;
        }
    }

    public async Task UpdateePostMemberMsgCountAsync(NpgsqlTransaction transaction, ulong userId, ulong threadId, NumericUpdateType reactionUpdateType, int count = 1)
    {
        const string SqlCommand = """
            UPDATE post_members 
            SET
                message_count = message_count + @Amount
            WHERE 
                user_id = @UserId AND thread_id = @ThreadId;
            """;

        int amount = reactionUpdateType switch
        {
            NumericUpdateType.Increase => count,
            NumericUpdateType.Decrease => -count,
            _ => throw new ArgumentOutOfRangeException(nameof(reactionUpdateType), $"未知枚举类型: {reactionUpdateType}")
        };

        var parameters = new
        {
            UserId = userId,
            ThreadId = threadId,
            Amount = amount
        };

        try
        {
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertTagsJunction: 为帖子 {ThreadId} 更新用户 {UserId} 发言记数时数据库操作失败！", threadId, userId);
            throw;
        }
    }

    /// <summary>
    /// 软删除一个帖子。
    /// 这将通过更新 is_deleted 标志来触发数据库中的 handle_post_soft_delete 触发器，
    /// 该触发器会处理所有相关的级联软删除和清理操作。
    /// </summary>
    /// <param name="threadId">要删除的帖子的ID</param>
    /// <returns>受影响的行数 (成功则为1, 否则为0)。</returns>
    public async Task<int> SoftDeleteThreadAsync(NpgsqlTransaction transaction, ulong threadId)
    {
        const string SQLCommand = """
            UPDATE posts_main
            SET 
                is_deleted = TRUE,
                delete_at = NOW() -- 记录删除时间
            WHERE 
                thread_id = @ThreadId AND is_deleted = FALSE;
            """;

        try
        {
            var parameters = new { ThreadId = threadId };

            int affectedRows = await transaction.Connection.ExecuteAsync(SQLCommand, parameters, transaction);

            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "软删除帖子 {ThreadId} 时发生数据库错误。", threadId);
            throw;
        }
    }
}