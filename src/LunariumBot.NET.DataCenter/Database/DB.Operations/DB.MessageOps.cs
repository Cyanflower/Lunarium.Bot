using System.Data;
using Dapper;

using LunariumBot.Net.DataCenter.Model;
using Npgsql;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    public async Task InsertForumThreadMessageAsync(NpgsqlTransaction transaction, MessageData messageData)
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

        var parameters = new
        {
            messageData.MessageId,
            messageData.AuthorId,
            messageData.CreatedAt,
            messageData.GuildId,
            messageData.ChannelId,
            messageData.ThreadId,
            messageData.MessageJumpUrl,
            messageData.IsCreateThread,
            messageData.IsUpdate,
        };

        try
        {
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public async Task TextChannelMessageToDB(MessageData messageData)
    {
        
    }

    public async Task MessagesDeleteToDB(List<ulong> messageIds)
    {
        
    }
}