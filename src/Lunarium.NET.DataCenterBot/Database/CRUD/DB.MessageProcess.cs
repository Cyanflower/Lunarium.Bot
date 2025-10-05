using System.Data;
using Dapper;

using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    public async Task ForumThreadMessageToDBAsync(MessageData messageData, int replayCount)
    {
        const string SQLCommand = """
            -- 步骤 1: 插入新消息记录到 message_main 表
            INSERT INTO message_main (
                message_id, author_id, author_name, created_at, guild_id, guild_name, 
                channel_id, channel_name, thread_id, thread_title, 
                message_jump_url, is_create_thread, is_update, is_deleted
            )
            VALUES (
                @MessageId, @AuthorId, @AuthorName, @CreatedAt, @GuildId, @GuildName, 
                @ChannelId, @ChannelName, @ThreadId, @ThreadTitle, 
                @MessageJumpUrl, @IsCreateThread, @IsUpdate, FALSE
            );
            
            -- 步骤 2: 更新主贴信息 (posts_main)
            -- 对于回复记数, Thread 数据会带有来自Discord API的帖子回复准确记数, 用其数据覆盖简单精准
            UPDATE posts_main
            SET 
                reply_count = @ReplayCount,
                last_active_at = @CreatedAt,               -- 更新最后活跃时间
                
                -- 如果是楼主的“更新”操作, 则更新以下字段, 否则保持原样
                update_at = CASE WHEN @IsUpdate THEN @CreatedAt ELSE update_at END,
                update_jump_url = CASE WHEN @IsUpdate THEN @MessageJumpUrl ELSE update_jump_url END,
                update_count = update_count + CASE WHEN @IsUpdate THEN 1 ELSE 0 END
            WHERE thread_id = @ThreadId;

            -- 步骤 3: 插入或更新帖子成员信息 (post_members)
            INSERT INTO post_members (user_id, user_name, thread_id, thread_name, join_at, message_count, last_message_id, last_message_at)
            VALUES (@AuthorId, @AuthorName, @ThreadId, @ThreadTitle, @CreatedAt, 1, @MessageId, @CreatedAt)
            -- 当 (user_id, thread_id) 冲突时 (意味着该用户已在该贴发过言)
            ON CONFLICT (user_id, thread_id) DO UPDATE 
            SET
                user_name = EXCLUDED.user_name, -- 更新一下用户名，同步用户改名
                thread_name = EXCLUDED.thread_name, -- 更新一下帖子名，同步帖子改名
                message_count = post_members.message_count + 1, -- 消息数+1
                last_message_id = EXCLUDED.last_message_id,     -- 更新为最新的消息ID
                last_message_at = EXCLUDED.last_message_at;     -- 更新为最新的消息时间
            
            -- 步骤 4: 插入或更新用户全局统计数据 (user_data)
            INSERT INTO user_data (user_id, user_message_count)
            VALUES (@AuthorId, 1)
            -- 当 (user_id) 冲突时 (意味着该用户已有统计记录)
            ON CONFLICT (user_id) DO UPDATE
            SET
                user_message_count = user_data.user_message_count + 1, -- 总消息数+1
                user_credit_lv1 = user_credit_lv1 + 1,
                user_update_count = CASE WHEN @IsUpdate THEN user_update_count +1 ELSE user_update_count END;
            """;
        try
        {
            await using Npgsql.NpgsqlConnection? connection = GetConnection();
            await connection.OpenAsync();
            await using System.Data.Common.DbTransaction? transaction = await connection.BeginTransactionAsync();
            var parameters = new
            {
                // From messageData
                messageData.MessageId,
                messageData.AuthorId,
                messageData.AuthorName,
                messageData.CreatedAt,
                messageData.GuildId,
                messageData.GuildName,
                messageData.ChannelId,
                messageData.ChannelName,
                messageData.ThreadId,
                messageData.ThreadTitle,
                messageData.MessageJumpUrl,
                messageData.IsCreateThread,
                messageData.IsUpdate,

                ReplayCount = replayCount
            };
            try
            {
                // 执行
                await connection.ExecuteAsync(SQLCommand, parameters, transaction);
                // 提交事务
                await transaction.CommitAsync();
                _logger.Info("成功处理消息 {MessageId} 并更新了数据库记录。", messageData.MessageId);
            }
            catch (Exception ex)
            {
                // 回滚
                await transaction.RollbackAsync();
                _logger.Error(ex, "处理消息 {MessageId} 时数据库操作失败！已回滚事务", messageData.MessageId);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public async Task TextThreadMessageToDBAsync(MessageData messageData)
    {
        const string SQLCommand = """
            -- 步骤 1: 插入新消息记录到 message_main 表
            INSERT INTO message_main (
                message_id, author_id, created_at, guild_id, guild_name, 
                channel_id, channel_name, thread_id, thread_title, 
                message_jump_url, is_create_thread, is_update, is_deleted
            )
            VALUES (
                @MessageId, @AuthorId, @CreatedAt, @GuildId, @GuildName, 
                @ChannelId, @ChannelName, @ThreadId, @ThreadTitle, 
                @MessageJumpUrl, @IsCreateThread, @IsUpdate, FALSE
            );

            -- 步骤 2: 插入或更新帖子成员信息 (post_members)
            INSERT INTO post_members (user_id, user_name, thread_id, thread_name, join_at, message_count, last_message_id, last_message_at)
            VALUES (@AuthorId, @AuthorName, @ThreadId, @ThreadTitle, @CreatedAt, 1, @MessageId, @CreatedAt)
            -- 当 (user_id, thread_id) 冲突时 (意味着该用户已在该贴发过言)
            ON CONFLICT (user_id, thread_id) DO UPDATE 
            SET
                user_name = EXCLUDED.user_name,                 -- 更新用户名
                thread_name = EXCLUDED.thread_name,             -- 更新帖子名
                message_count = post_members.message_count + 1, -- 消息数+1
                last_message_id = EXCLUDED.last_message_id,     -- 更新为最新的消息ID
                last_message_at = EXCLUDED.last_message_at;     -- 更新为最新的消息时间
            
            -- 步骤 3: 插入或更新用户全局统计数据 (user_data)
            INSERT INTO user_data (user_id, user_message_count)
            VALUES (@AuthorId, 1)
            -- 当 (user_id) 冲突时 (意味着该用户已有统计记录)
            ON CONFLICT (user_id) DO UPDATE
            SET
                user_message_count = user_data.user_message_count + 1, -- 总消息数+1
                user_credit_lv1 = user_credit_lv1 + 1;
            """;
        try
        {
            await using Npgsql.NpgsqlConnection? connection = GetConnection();
            await connection.OpenAsync();
            await using System.Data.Common.DbTransaction? transaction = await connection.BeginTransactionAsync();
            var parameters = new
            {
                // From messageData
                messageData.MessageId,
                messageData.AuthorId,
                messageData.CreatedAt,
                messageData.GuildId,
                messageData.GuildName,
                messageData.ChannelId,
                messageData.ChannelName,
                messageData.ThreadId,
                messageData.ThreadTitle,
                messageData.MessageJumpUrl,
                messageData.IsCreateThread,
                messageData.IsUpdate,
            };
            try
            {
                // 执行
                await connection.ExecuteAsync(SQLCommand, parameters, transaction);
                // 提交事务
                await transaction.CommitAsync();
                _logger.Info("成功处理消息 {MessageId} 并更新了数据库记录。", messageData.MessageId);
            }
            catch (Exception ex)
            {
                // 回滚
                await transaction.RollbackAsync();
                _logger.Error(ex, "处理消息 {MessageId} 时数据库操作失败！已回滚事务", messageData.MessageId);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public async Task TextChannelMessageToDB(MessageData messageData)
    {
        const string SQLCommand = """
            -- 步骤 1: 插入新消息记录到 message_main 表
            INSERT INTO message_main (
                message_id, author_id, created_at, guild_id, guild_name, 
                channel_id, channel_name, thread_id, thread_title, 
                message_jump_url, is_create_thread, is_update, is_deleted
            )
            VALUES (
                @MessageId, @AuthorId, @CreatedAt, @GuildId, @GuildName, 
                @ChannelId, @ChannelName, @ThreadId, @ThreadTitle, 
                @MessageJumpUrl, @IsCreateThread, @IsUpdate, FALSE
            );

            -- 步骤 2: 插入或更新用户全局统计数据 (user_data)
            INSERT INTO user_data (user_id, user_message_count)
            VALUES (@AuthorId, 1)
            -- 当 (user_id) 冲突时 (意味着该用户已有统计记录)
            ON CONFLICT (user_id) DO UPDATE
            SET
                user_message_count = user_data.user_message_count + 1, -- 总消息数+1
                user_credit_lv1 = user_credit_lv1 + 1;
            """;
        try
        {
            await using Npgsql.NpgsqlConnection? connection = GetConnection();
            await connection.OpenAsync();
            await using System.Data.Common.DbTransaction? transaction = await connection.BeginTransactionAsync();
            var parameters = new
            {
                // From messageData
                messageData.MessageId,
                messageData.AuthorId,
                messageData.CreatedAt,
                messageData.GuildId,
                messageData.GuildName,
                messageData.ChannelId,
                messageData.ChannelName,
                messageData.ThreadId,
                messageData.ThreadTitle,
                messageData.MessageJumpUrl,
                messageData.IsCreateThread,
                messageData.IsUpdate,
            };
            try
            {
                // 执行
                await connection.ExecuteAsync(SQLCommand, parameters, transaction);
                // 提交事务
                await transaction.CommitAsync();
                _logger.Info("成功处理消息 {MessageId} 并更新了数据库记录。", messageData.MessageId);
            }
            catch (Exception ex)
            {
                // 回滚
                await transaction.RollbackAsync();
                _logger.Error(ex, "处理消息 {MessageId} 时数据库操作失败！已回滚事务", messageData.MessageId);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public async Task MessagesDeleteToDB(List<ulong> messageIds)
    {
        if (messageIds.Count == 0)
        {
            _logger.Debug("MessagesDeleteToDB: 传入的消息ID列表为空，跳过数据库操作。");
            return;
        }

        const string SQLCommand = """
            -- 步骤 1: 软删除消息
            UPDATE message_main
            SET is_deleted = TRUE
            WHERE message_id = ANY(@MessageIds) AND is_deleted = FALSE;

            -- 步骤 2: 更新 post_members（减少消息计数）
            UPDATE post_members pm
            SET message_count = pm.message_count - subq.deleted_count
            FROM (
                SELECT 
                    author_id,
                    thread_id,
                    COUNT(*) AS deleted_count
                FROM message_main
                WHERE message_id = ANY(@MessageIds) 
                AND is_deleted = TRUE 
                AND thread_id IS NOT NULL
                GROUP BY author_id, thread_id
            ) subq
            WHERE pm.user_id = subq.author_id AND pm.thread_id = subq.thread_id;

            -- 步骤 3: 标记帖子为无效
            UPDATE posts_main pm
            SET is_valid = FALSE
            WHERE pm.thread_id = ANY(@MessageIds)
            AND EXISTS (
                SELECT 1 FROM message_main mm 
                WHERE mm.message_id = pm.thread_id AND mm.is_deleted = TRUE
            );

            -- 步骤 4: 更新 user_data
            UPDATE user_data ud
            SET
                user_message_count = ud.user_message_count - subq.total_count,
                user_credit_lv1 = ud.user_credit_lv1 - subq.total_count,
                user_update_count = ud.user_update_count - subq.update_count
            FROM (
                SELECT 
                    author_id,
                    COUNT(*) AS total_count,
                    COUNT(*) FILTER (WHERE is_update = TRUE) AS update_count
                FROM message_main
                WHERE message_id = ANY(@MessageIds) AND is_deleted = TRUE
                GROUP BY author_id
            ) subq
            WHERE ud.user_id = subq.author_id;
            """;

        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            var parameters = new { MessageIds = messageIds.ToArray() };

            try
            {
                var deletedCount = await connection.ExecuteAsync(SQLCommand, parameters, transaction);
                await transaction.CommitAsync();
                _logger.Info("成功软删除 {DeletedCount} 条消息（共尝试删除 {TotalCount} 条），并同步更新了用户和成员的统计数据。", 
                            deletedCount, messageIds.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error(ex, "批量删除消息时数据库操作失败！事务已回滚。");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "在 MessagesDeleteToDB 方法中发生异常。");
            throw;
        }
    }
}