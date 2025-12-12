using Npgsql;

using Dapper;

using LunariumBot.Net.ReleaseBot.Models;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.ReleaseBot.Database;

public partial class DatabaseService
{
    public async Task<long?> QueryCacheChannelAsync(ulong threadId)
    {
        const string SqlCommand = """
            SELECT cache_thread_id
            FROM release_main
            WHERE thread_id = @ThreadId;
            """;
        var parameters = new
        {
            ThreadId = threadId.ToDbId()
        };

        try
        {
            await using var connection = GetConnection();
            var result = await connection.ExecuteScalarAsync<long?>(SqlCommand, parameters);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ QueryCacheChannelAsync: 查询 Thread {ThreadId} 的缓存频道失败！", threadId);
            throw;
        }
    }

    public async Task<int?> InsertWorkDataAsync(NpgsqlTransaction transaction, WorkData workData)
    {
        const string SqlCommand = """
            INSERT INTO release_main (
                release_message_id, thread_id, author_id,
                release_at, guild_id, channel_id,
                message_jump_url, release_model, release_password, is_consume_token, 
                cache_message_url, cache_message_id, cache_thread_id,
                cache_channel_id, cache_guild_id, cache_attachment_urls, alerts,
                enabled_alerts, is_update, is_deleted,
                delete_at
            )
            VALUES (
                @ReleaseMessageId, @ThreadId, @AuthorId,
                @ReleaseAt, @GuildId, @ChannelId,
                @MessageJumpUrl, @ReleaseModel, @ReleasePassword, @IsConsumeToken, 
                @CacheMessageUrl, @CacheMessageId, @CacheThreadId,
                @CacheChannelId, @CacheGuildId, @CacheAttachmentUrls, @Alerts,
                @EnableAlerts, @IsUpdate, FALSE,
                NULL
            );
            """;

        var workDataDb = WorkDataDb.FromDomain(workData);
        var parameters = new
        {
            ReleaseMessageId = workDataDb.ReleaseMessageId,
            ThreadId = workDataDb.ThreadId,
            AuthorId = workDataDb.AuthorId,
            ReleaseAt = workDataDb.ReleaseAt,
            GuildId = workDataDb.GuildId,
            ChannelId = workDataDb.ChannelId,
            MessageJumpUrl = workDataDb.MessageJumpUrl,
            ReleaseModel = workDataDb.ReleaseModel,
            ReleasePassword = workDataDb.ReleasePassword,
            IsConsumeToken = workDataDb.IsConsumeToken,
            CacheMessageUrl = workDataDb.CacheMessageUrl,
            CacheMessageId = workDataDb.CacheMessageId,
            CacheThreadId = workDataDb.CacheThreadId,
            CacheChannelId = workDataDb.CacheChannelId,
            CacheGuildId = workDataDb.CacheGuildId,
            CacheAttachmentUrls = workDataDb.CacheAttachmentUrls,
            Alerts = workDataDb.Alerts,
            EnableAlerts = workDataDb.EnableAlerts,
            IsUpdate = workDataDb.IsUpdate,
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            var result = await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertWorkDataAsync: 写入作品信息 {@workData} 失败！", workData);
            throw;
        }
    }

    public async Task<int?> InsertDownloadLogAsync(NpgsqlTransaction transaction, DownloadLog downloadLog)
    {
        const string SqlCommand = """
            INSERT INTO work_download_logs (
                user_id,
                thread_id,
                release_message_id
            )
            VALUES (
                @UserId,
                @ThreadId,
                @ReleaseMessageId
            );
            """;

        var downloadLogDb = DownloadLogDb.FromDomain(downloadLog);
        var parameters = new
        {
            UserId = downloadLogDb.UserId,
            ThreadId = downloadLogDb.ThreadId,
            ReleaseMessageId = downloadLogDb.ReleaseMessageId,
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            var result = await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ InsertDownloadLogAsync: 写入作品信息 {@downloadLog} 失败！", downloadLog);
            throw;
        }
    }

    /// 根据 release_message_id 查询对应的完整 WorkData
    public async Task<WorkDataDb?> QueryWorkDataByMessageIdAsync(ulong releaseMessageId)
    {
        const string SqlCommand = """
            SELECT 
                release_message_id AS ReleaseMessageId, 
                thread_id AS ThreadId, 
                author_id AS AuthorId,
                release_at AS ReleaseAt, 
                guild_id AS GuildId, 
                channel_id AS ChannelId,
                message_jump_url AS MessageJumpUrl, 
                release_model AS ReleaseModel, 
                release_password AS ReleasePassword,
                is_consume_token AS IsConsumeToken,
                cache_message_url AS CacheMessageUrl, 
                cache_message_id AS CacheMessageId, 
                cache_thread_id AS CacheThreadId,
                cache_channel_id AS CacheChannelId, 
                cache_guild_id AS CacheGuildId, 
                cache_attachment_urls AS CacheAttachmentUrls, 
                alerts AS Alerts,
                enabled_alerts AS EnableAlerts, 
                is_update AS IsUpdate, 
                is_deleted AS IsDeleted,
                delete_at AS DeleteAt
            FROM release_main
            WHERE release_message_id = @ReleaseMessageId
            LIMIT 1;
            """;

        var parameters = new
        {
            ReleaseMessageId = releaseMessageId.ToDbId()
        };

        try
        {
            // 查询操作：数据层负责连接管理
            await using var connection = GetConnection();
            var result = await connection.QuerySingleOrDefaultAsync<WorkDataDb>(SqlCommand, parameters);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ QueryWorkDataByMessageIdAsync: 查询 Message {ReleaseMessageId} 的 WorkData 失败！", releaseMessageId);
            throw;
        }
    }
    
    public async Task<WorkDataDb?> QueryLatestWorkDataByThreadIdAsync(ulong threadId)
    {
        const string SqlCommand = """
            SELECT 
                release_message_id AS ReleaseMessageId, 
                thread_id AS ThreadId, 
                author_id AS AuthorId,
                release_at AS ReleaseAt, 
                guild_id AS GuildId, 
                channel_id AS ChannelId,
                message_jump_url AS MessageJumpUrl, 
                release_model AS ReleaseModel, 
                release_password AS ReleasePassword,
                is_consume_token AS IsConsumeToken,
                cache_message_url AS CacheMessageUrl, 
                cache_message_id AS CacheMessageId, 
                cache_thread_id AS CacheThreadId,
                cache_channel_id AS CacheChannelId, 
                cache_guild_id AS CacheGuildId, 
                cache_attachment_urls AS CacheAttachmentUrls, 
                alerts AS Alerts,
                enabled_alerts AS EnableAlerts, 
                is_update AS IsUpdate, 
                is_deleted AS IsDeleted,
                delete_at AS DeleteAt
            FROM release_main
            WHERE thread_id = @ThreadId 
              AND is_deleted = FALSE
            ORDER BY release_at DESC
            LIMIT 1;
            """;

        var parameters = new
        {
            ThreadId = threadId.ToDbId()
        };

        try
        {
            await using var connection = GetConnection();
            var result = await connection.QuerySingleOrDefaultAsync<WorkDataDb>(SqlCommand, parameters);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ QueryLatestWorkDataByThreadIdAsync: 查询 Thread {ThreadId} 的最新 WorkData 失败！", threadId);
            throw;
        }
    }

    /// 根据 release_message_id 软删除 (IsDeleted=true, DeleteAt=NOW())
    public async Task<int> SoftDeleteWorkDataAsync(NpgsqlTransaction transaction, ulong releaseMessageId)
    {
        const string SqlCommand = """
            UPDATE release_main
            SET is_deleted = TRUE, 
                delete_at = NOW()
            WHERE release_message_id = @ReleaseMessageId;
            """;

        var parameters = new
        {
            ReleaseMessageId = releaseMessageId.ToDbId()
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            // 操作：使用传入的事务
            var rowsAffected = await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
            return rowsAffected;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SoftDeleteWorkDataAsync: 软删除 Message {ReleaseMessageId} 失败！", releaseMessageId);
            throw;
        }
    }

    // 根据 ThreadId 硬删除所有匹配行
    public async Task<int> HardDeleteWorkDataByThreadIdAsync(NpgsqlTransaction transaction, ulong threadId)
    {
        const string SqlCommand = """
            DELETE FROM release_main
            WHERE thread_id = @ThreadId;
            """;

        var parameters = new
        {
            ThreadId = threadId.ToDbId()
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            // 操作：使用传入的事务
            var rowsAffected = await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
            return rowsAffected;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ HardDeleteWorkDataByThreadIdAsync: 硬删除 Thread {ThreadId} 的数据失败！", threadId);
            throw;
        }
    }

    // 根据 release_message_id 查询对应的 ReleasePassword
    public async Task<string?> QueryReleasePasswordAsync(ulong releaseMessageId)
    {
        const string SqlCommand = """
            SELECT release_password
            FROM release_main
            WHERE release_message_id = @ReleaseMessageId;
            """;

        var parameters = new
        {
            ReleaseMessageId = releaseMessageId.ToDbId()
        };

        try
        {
            // 查询操作：数据层负责连接管理
            await using var connection = GetConnection();
            var result = await connection.ExecuteScalarAsync<string?>(SqlCommand, parameters);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ QueryReleasePasswordAsync: 查询 Message {ReleaseMessageId} 的密码失败！", releaseMessageId);
            throw;
        }
    }

    // 检查 message_main 表中是否存在指定 author_id 和 thread_id 的记录。
    // 不需要返回实际数据，仅检查存在性。
    public async Task<bool> CheckMessageExistsAsync(ulong authorId, ulong threadId)
    {
        const string SqlCommand = """
            SELECT 1
            FROM message_main
            WHERE author_id = @AuthorId 
              AND thread_id = @ThreadId
            LIMIT 1;
            """;

        var parameters = new
        {
            AuthorId = authorId.ToDbId(),
            ThreadId = threadId.ToDbId()
        };

        _logger.Debug("CheckMessageExistsAsync: parameters: {@parameters}", parameters);
        
        try
        {
            // 此处只需要读操作，使用 GetConnection() 获取新连接即可
            await using var connection = GetConnection();

            // ExecuteScalarAsync 获取第一行第一列，如果没有匹配行则返回 null
            var result = await connection.ExecuteScalarAsync<int?>(SqlCommand, parameters);

            _logger.Debug("CheckMessageExistsAsync: result: {result}", result);

            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ CheckMessageExistsAsync: 检查 Author {AuthorId} 在 Thread {ThreadId} 的记录失败！", authorId, threadId);
            throw;
        }
    }


    // 检查 post_reactions 表中是否存在指定 thread_id 和 user_id 的记录
    // 只要该用户对该帖子有任何 Emoji 反应，即返回 true
    public async Task<bool> CheckReactionExistsAsync(ulong userId, ulong threadId)
    {
        // 无论用户使用了哪种 Emoji，只要有一条记录匹配 thread_id 和 user_id 即可
        const string SqlCommand = """
            SELECT 1
            FROM post_reactions
            WHERE thread_id = @ThreadId
              AND user_id = @UserId
            LIMIT 1;
            """;

        var parameters = new
        {
            ThreadId = threadId.ToDbId(),
            UserId = userId.ToDbId()
        };

        try
        {
            await using var connection = GetConnection();
            var result = await connection.ExecuteScalarAsync<int?>(SqlCommand, parameters);
            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ CheckReactionExistsAsync: 检查 User {UserId} 在 Thread {ThreadId} 的反应记录失败！", userId, threadId);
            throw;
        }
    }
    
    // 检查 work_download_logs 表中是否存在指定 user_id 和 release_message_id 的记录。
    // 不返回实际数据，仅检查存在性。
    public async Task<bool> CheckDownloadLogExistsAsync(ulong userId, ulong releaseMessageId)
    {
        const string SqlCommand = """
            SELECT 1
            FROM work_download_logs
            WHERE user_id = @UserId 
              AND release_message_id = @ReleaseMessageId
            LIMIT 1;
            """;

        var parameters = new
        {
            UserId = userId.ToDbId(),
            ReleaseMessageId = releaseMessageId.ToDbId()
        };

        try
        {
            // 此处只需要读操作，使用 GetConnection() 获取新连接即可
            await using var connection = GetConnection();

            // ExecuteScalarAsync 获取第一行第一列，如果没有匹配行则返回 null
            var result = await connection.ExecuteScalarAsync<int?>(SqlCommand, parameters);

            return result.HasValue;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ CheckDownloadLogExistsAsync: 检查 User {UserId} 对 Message {ReleaseMessageId} 的下载记录失败！", userId, releaseMessageId);
            throw;
        }
    }
}