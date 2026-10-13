using System.Data;
using Dapper;

using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.DataCenter.Models.Database;
using Lunarium.Bot.Extensions;
using Npgsql;

namespace Lunarium.Bot.DataCenter.Database;

public partial class DatabaseService
{
    public async Task InsertMessageAsync(NpgsqlTransaction transaction, MessageData messageData)
    {
        const string SqlCommand = """
            INSERT INTO message_main (
                message_id, author_id, created_at, guild_id, 
                channel_id, thread_id,
                message_jump_url, is_create_thread, is_update, is_deleted
            )
            VALUES (
                @MessageId, @AuthorId, @CreatedAt, @GuildId, 
                @ChannelId, @ThreadId,
                @MessageJumpUrl, @IsCreateThread, @IsUpdate, FALSE
            );
            """;

        var messageDataDb = MessageDataDb.FromDomain(messageData);

        var parameters = new
        {
            messageDataDb.MessageId,
            messageDataDb.AuthorId,
            messageDataDb.CreatedAt,
            messageDataDb.GuildId,
            messageDataDb.ChannelId,
            messageDataDb.ThreadId,
            messageDataDb.MessageJumpUrl,
            messageDataDb.IsCreateThread,
            messageDataDb.IsUpdate,
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex,"❌ InsertMessageAsync: 插入消息 {MessageId} 时数据库操作失败！", messageData.MessageId);
            throw;
        }
    }

    public async Task DeleteMessagesAsync(NpgsqlTransaction transaction, List<ulong> messageIds)
    {
        const string SqlCommand = """
            UPDATE message_main
            SET
                is_deleted = true
            WHERE
                message_id = ANY(@MessageIds)
            """;
        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            await transaction.Connection.ExecuteAsync(SqlCommand, new { MessageIds = messageIds.Select(id => id.ToDbId()).ToArray() }, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex,"❌ InsertMessageAsync: 批量删除以下消息 {MessageIds} 时数据库操作失败！", messageIds);
            throw;
        }
    }
}