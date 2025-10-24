using Npgsql;
using Dapper;

using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    public async Task SyncAllChannelsAndTagsAsync(NpgsqlTransaction transaction, List<ChannelData> allChannels, List<TagInfo> allTags)
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
            // 执行频道同步
            if (allChannels.Any())
            {
                var channelParams = new
                {
                    ChannelIds = allChannels.Select(c => c.ChannelId).ToArray(),
                    ChannelNames = allChannels.Select(c => c.ChannelName).ToArray(),
                    GuildIds = allChannels.Select(c => c.GuildId).ToArray(),
                    GuildNames = allChannels.Select(c => c.GuildName).ToArray(),
                };
                await transaction.Connection.ExecuteAsync(syncChannelsSql, channelParams, transaction);
                var uniqueGuildIds = allChannels.Select(c => c.GuildId).Distinct().ToArray();
                await transaction.Connection.ExecuteAsync(deleteOrphanChannelsSql, 
                    new { ChannelIds = channelParams.ChannelIds, GuildIds = uniqueGuildIds }, transaction);
            }
    
            // 执行标签同步
            if (allTags.Any())
            {
                var tagParams = new
                {
                    TagIds = allTags.Select(t => t.TagId).ToArray(),
                    TagChannelIds = allTags.Select(t => t.ChannelId).ToArray(),
                    TagNames = allTags.Select(t => t.TagName).ToArray(),
                    TagChannelNames = allTags.Select(t => t.ChannelName).ToArray(),
                    TagGuildNames = allTags.Select(t => t.GuildName).ToArray()
                };
                await transaction.Connection.ExecuteAsync(syncTagsSql, tagParams, transaction);
                var uniqueChannelIds = allTags.Select(t => t.ChannelId).Distinct().ToArray();
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