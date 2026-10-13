using Npgsql;
using Dapper;

using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.DataCenter.Models.Database;

namespace Lunarium.Bot.DataCenter.Database;

public partial class DatabaseService
{
    public async Task SyncAllChannelsAndTagsAsync(NpgsqlTransaction transaction, List<ChannelData> allChannels, List<TagData> allTags)
    {
        // 频道同步SQL
        const string syncChannelsSql = """
            -- 批量插入或更新频道信息
            INSERT INTO channels (channel_id, channel_name, guild_id, guild_name)
            SELECT 
                channel_id, channel_name, guild_id, guild_name
            FROM unnest(@ChannelIds, @ChannelNames, @GuildIds, @GuildNames)
                AS t(channel_id, channel_name, guild_id, guild_name)
            ON CONFLICT (channel_id) DO UPDATE
            SET
                channel_name = EXCLUDED.channel_name,
                guild_name = EXCLUDED.guild_name;
            """;
        
        // 标签同步SQL
        const string syncTagsSql = """
            -- 批量插入或更新标签信息
            INSERT INTO tags (tag_id, channel_id, tag_name, channel_name, guild_name)
            SELECT
                tag_id, channel_id, tag_name, channel_name, guild_name
            FROM unnest(@TagIds, @TagChannelIds, @TagNames, @TagChannelNames, @TagGuildNames)
                AS t(tag_id, channel_id, tag_name, channel_name, guild_name)
            ON CONFLICT (tag_id) DO UPDATE
            SET
                tag_name = EXCLUDED.tag_name,
                channel_name = EXCLUDED.channel_name,
                guild_name = EXCLUDED.guild_name;
            """;

        // 清理不存在的频道
        const string deleteOrphanChannelsSql = """
            DELETE FROM channels
            WHERE 
                guild_id = ANY(@GuildIds)  -- 只删除扫描范围内的服务器
                AND channel_id <> ALL(@ChannelIds);
            """;
        
        // 清理不存在的标签
        const string deleteOrphanTagsSql = """
            DELETE FROM tags
            WHERE 
                channel_id = ANY(@ChannelIds)  -- 只删除扫描范围内的频道标签
                AND tag_id <> ALL(@TagIds);
            """;

        try
        {
            var allChannelsDb = allChannels.Select(ChannelDataDb.FromDomain).ToList();

            var allTagsDb = allTags.Select(TagDataDb.FromDomain).ToList();
            
            // 执行频道同步
            if (allChannelsDb.Any())
            {
                var channelParams = new
                {
                    ChannelIds = allChannelsDb.Select(c => c.ChannelId).ToArray(),
                    ChannelNames = allChannelsDb.Select(c => c.ChannelName).ToArray(),
                    GuildIds = allChannelsDb.Select(c => c.GuildId).ToArray(),
                    GuildNames = allChannelsDb.Select(c => c.GuildName).ToArray(),
                };

                if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

                await transaction.Connection.ExecuteAsync(syncChannelsSql, channelParams, transaction);

                var uniqueGuildIds = allChannelsDb.Select(c => c.GuildId).Distinct().ToArray();

                await transaction.Connection.ExecuteAsync(deleteOrphanChannelsSql, 
                    new { ChannelIds = channelParams.ChannelIds, GuildIds = uniqueGuildIds }, transaction);
            }
    
            // 执行标签同步
            if (allTagsDb.Any())
            {
                var tagParams = new
                {
                    TagIds = allTagsDb.Select(t => t.TagId).ToArray(),
                    TagChannelIds = allTagsDb.Select(t => t.ChannelId).ToArray(),
                    TagNames = allTagsDb.Select(t => t.TagName).ToArray(),
                    TagChannelNames = allTagsDb.Select(t => t.ChannelName).ToArray(),
                    TagGuildNames = allTagsDb.Select(t => t.GuildName).ToArray()
                };

                if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

                await transaction.Connection.ExecuteAsync(syncTagsSql, tagParams, transaction);

                var uniqueChannelIds = allTagsDb.Select(t => t.ChannelId).Distinct().ToArray();

                await transaction.Connection.ExecuteAsync(deleteOrphanTagsSql, 
                    new { TagIds = tagParams.TagIds, ChannelIds = uniqueChannelIds }, transaction);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SyncAllChannelsAndTagsAsync: 批量同步频道和标签数据到数据库的写入更新失败");
            throw;
        }
    }
}