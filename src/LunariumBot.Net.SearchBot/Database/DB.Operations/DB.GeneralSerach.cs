using Npgsql;
using Dapper;


namespace LunariumBot.Net.SearchBot.Database;

public class GeneralSerach
{
    const string SqlCommandHead = """
        SELECT
            thread_id AS ThreadId,
            first_message_id AS FirstMessageId,

            title AS Title,
            first_message_content AS Content,
            jump_url AS JumpUrl,
            updated_jump_url AS UpdateMessageJumpUrl,
            attachment_url AS AttachmentUrl,

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
        """;

}