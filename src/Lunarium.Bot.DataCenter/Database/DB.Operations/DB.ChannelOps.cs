using Npgsql;
using Dapper;

using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.Framework;
using Lunarium.Bot.DataCenter.Models.Database;

namespace Lunarium.Bot.DataCenter.Database;

public partial class DatabaseService
{
    /// <summary>
    /// 创建或更新一个频道及其所有关联的标签信息。
    /// 该操作在一个事务中执行，以保证数据的一致性。
    /// </summary>
    /// <param name="transaction">NpgsqlTransaction 事务。</param>
    /// <param name="channelData">要创建或更新的频道数据。</param>
    /// <param name="tagDatas">该频道下最新的完整标签列表。</param>
    public async Task UpsertForumChannelAsync(NpgsqlTransaction transaction, ChannelData channelData, List<TagData> tagDatas)
    {
        const string SqlCommand = """
            -- 步骤 1: 插入或更新频道信息 (UPSERT)
            -- 如果 channel_id 已存在, 就更新它的名称和服务器信息, 确保数据最新。
            INSERT INTO channels (channel_id, channel_name, guild_id, guild_name)
            VALUES (@ChannelId, @ChannelName, @GuildId, @GuildName)
            ON CONFLICT (channel_id) DO UPDATE 
            SET 
                channel_name = EXCLUDED.channel_name, 
                guild_name = EXCLUDED.guild_name;

            -- 步骤 2: 批量插入或更新该频道下的所有标签 (Batch UPSERT)
            -- 使用 unnest 函数将传入的数组参数展开成行, 一次性处理所有标签。
            INSERT INTO tags (tag_id, channel_id, tag_name, channel_name, guild_name)
            SELECT 
                t.tag_id,
                @ChannelId, -- 所有这些标签都属于同一个频道
                t.tag_name,
                @ChannelName,
                @GuildName
            FROM unnest(@AllTagIds, @AllTagNames) AS t(tag_id, tag_name)
            ON CONFLICT (tag_id) DO UPDATE
            SET
                tag_name = EXCLUDED.tag_name,
                channel_name = EXCLUDED.channel_name,
                guild_name = EXCLUDED.guild_name;

            -- 步骤 3: 校准标签 - 删除该频道下已不存在的旧标签
            -- tagDatas 是该频道的最新标签全集, 所以数据库里有、但新列表里没有的，就是过时的。
            -- 使用 <> ALL() 语法可以高效地找出需要删除的标签。
            -- 注意：由于在 v1.initial_schema.sql 中为 tags 表设置了 ON DELETE CASCADE
            -- 删除这里的 tag 会自动清理 post_tags_junction 表中的关联记录，非常方便！
            DELETE FROM tags
            WHERE channel_id = @ChannelId AND tag_id <> ALL(@AllTagIds);
            """;

        var channelDataDb = ChannelDataDb.FromDomain(channelData);
        var tagDataDbs = tagDatas.Select(TagDataDb.FromDomain).ToList();

        var parameters = new
        {
            channelDataDb.ChannelId,
            channelDataDb.ChannelName,
            channelDataDb.GuildId,
            channelDataDb.GuildName,
            AllTagIds = tagDataDbs.Select(t => t.TagId).ToArray(),
            AllTagNames = tagDataDbs.Select(t => t.TagName).ToArray()
        };

        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ CreateOrUpdateChannelToDB: 创建/更新频道 {ChannelName} ({ChannelId}) 时数据库操作失败", channelData.ChannelName, channelData.ChannelId);
            throw;
        }
    }

    /// <summary>
    /// 从数据库中删除一个频道及其所有关联的标签。
    /// 这个操作是原子性的，通过数据库的外键级联删除特性实现。
    /// </summary>
    /// <param name="transaction">NpgsqlTransaction 事务。</param>
    /// <param name="channelId">要删除的频道的ID。</param>
    /// <returns>数据库删除的行数</returns>
    public async Task<int> DeleteForumChannelAsync(NpgsqlTransaction transaction, ulong channelId)
    {
        const string SqlCommand = """
            DELETE FROM channels
            WHERE channel_id = @ChannelId;
            """;

        try
        {
            var parameters = new { ChannelId = channelId.ToDbId() };

            // 执行删除操作
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            int affectedRows = await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);

            return affectedRows;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ DeleteChannelToDB: 删除频道 {ChannelId} 时发生数据库错误。", channelId);
            throw;
        }
    }
}