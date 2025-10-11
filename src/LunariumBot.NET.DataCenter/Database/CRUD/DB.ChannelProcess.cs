using System.Data;
using Dapper;

using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    /// <summary>
    /// 创建或更新一个频道及其所有关联的标签信息。
    /// 该操作在一个事务中执行，以保证数据的一致性。
    /// </summary>
    /// <param name="channelData">要创建或更新的频道数据。</param>
    /// <param name="tagInfos">该频道下最新的完整标签列表。</param>
    public async Task CreateOrUpdateChannelToDB(ChannelData channelData, List<TagInfo> tagInfos)
    {
        // 准备 SQL 命令。我们将多个操作组合在一个字符串中，
        // 在一个事务里执行，可以获得最佳性能和数据一致性。
        const string sqlCommand = """
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
            -- 这样比循环插入快得多！
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
            -- tagInfos 是该频道的最新标签全集, 所以数据库里有、但新列表里没有的，就是过时的。
            -- 使用 <> ALL() 语法可以高效地找出需要删除的标签。
            -- 注意：由于你在 v1.initial_schema.sql 中为 tags 表设置了 ON DELETE CASCADE，
            -- 删除这里的 tag 会自动清理 post_tags_junction 表中的关联记录，非常方便！
            DELETE FROM tags
            WHERE channel_id = @ChannelId AND tag_id <> ALL(@AllTagIds);
            """;

        try
        {
            // 获取数据库连接
            await using var connection = GetConnection();
            await connection.OpenAsync();
            // 开启一个事务，这是保证多个操作原子性的关键！
            await using var transaction = await connection.BeginTransactionAsync();

            var parameters = new
            {
                // From channelData
                channelData.ChannelId,
                channelData.ChannelName,
                channelData.GuildId,
                channelData.GuildName,

                // From tagInfos, 需要从 List<TagInfo> 中提取出 ID 和 Name 的数组
                // Dapper 会自动将 ulong[] 和 string[] 映射为 PostgreSQL 的数组类型
                AllTagIds = tagInfos.Select(t => t.TagId).ToArray(),
                AllTagNames = tagInfos.Select(t => t.TagName).ToArray()
            };

            try
            {
                // 在事务中执行我们定义好的 SQL 命令
                await connection.ExecuteAsync(sqlCommand, parameters, transaction);
                // 如果所有操作都成功了，就提交事务，让更改永久生效
                await transaction.CommitAsync();
                _logger.Info("成功创建/更新频道 {ChannelName} ({ChannelId}) 及其标签。", channelData.ChannelName, channelData.ChannelId);
            }
            catch (Exception ex)
            {
                // 如果中间任何一步出错了，就回滚事务，撤销所有已做的更改
                await transaction.RollbackAsync();
                _logger.Error(ex, "创建/更新频道 {ChannelId} 时数据库操作失败！事务已回滚。", channelData.ChannelId);
                throw; // 重新抛出异常，让上层调用者知道操作失败了
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "在 CreateOrUpdateChannelToDB 方法中获取数据库连接或开启事务时失败。");
            throw;
        }
    }

    /// <summary>
    /// 从数据库中删除一个频道及其所有关联的标签。
    /// 这个操作是原子性的，通过数据库的外键级联删除特性实现。
    /// </summary>
    /// <param name="channelId">要删除的频道的ID。</param>
    /// <param name="tagIds">（可选）要删除的标签ID列表，用于日志记录。此方法主要依赖 channelId 进行删除。</param>
    public async Task DeleteChannelToDB(ulong channelId, List<ulong> tagIds)
    {
        // SQL 命令非常简单，因为数据库结构设计得很好！
        // 我们在 `tags` 表上设置了 `ON DELETE CASCADE` 外键约束。
        // 这意味着，当我们删除 `channels` 表中的一条记录时，
        // PostgreSQL 会自动找到并删除 `tags` 表中所有引用了这个 channel_id 的记录。
        // 同样，你在 `post_tags_junction` 上的级联删除也会被触发。
        // T.3 触发器还会软删除所有属于这个频道的帖子！
        // 这就是声明式数据完整性的强大之处！
        const string sqlCommand = """
            DELETE FROM channels
            WHERE channel_id = @ChannelId;
            """;

        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();

            var parameters = new { ChannelId = channelId };
            
            // 执行删除操作
            int affectedRows = await connection.ExecuteAsync(sqlCommand, parameters);
            
            if (affectedRows > 0)
            {
                _logger.Info("成功删除频道 {ChannelId}。数据库级联规则会自动处理其关联的标签和帖子。", channelId);
            }
            else
            {
                _logger.Warning("尝试删除频道 {ChannelId}，但该频道在数据库中未找到。", channelId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "删除频道 {ChannelId} 时发生数据库错误。", channelId);
            throw;
        }
    }
}