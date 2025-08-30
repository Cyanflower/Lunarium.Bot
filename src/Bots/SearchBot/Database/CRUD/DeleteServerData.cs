using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===

    // SQL模板: 将指定日期的回复数减一
    private const string serverDataDecrementReplySql = """
        UPDATE server_data
        SET reply_count = reply_count - 1
        WHERE date = @Date AND server_id = @ServerId AND channel_id = @ChannelId;
        """;
        
    // SQL模板: 将指定日期的创建帖子数和回复数同时减少
    private const string serverDataDecrementPostAndRepliesSql = """
        UPDATE server_data
        SET 
            created_posts_count = created_posts_count - @PostsRemovedCount,
            reply_count = reply_count - @ReplyRemovedCount
        WHERE date = @Date AND server_id = @ServerId AND channel_id = @ChannelId;
        """;

    /// <summary>
    /// 尝试为指定日期、服务器和频道的“回复数”减一。
    /// 如果统计记录不存在，操作将静默失败。
    /// </summary>
    /// <param name="date">日期字符串 (格式: YYYY-MM-DD)</param>
    /// <param name="serverId">服务器ID</param>
    /// <param name="channelId">频道ID</param>
    public async Task DecrementReplyCountAsync(string date, ulong serverId, ulong channelId)
    {
        try
        {
            var parameters = new 
            {
                Date = date,
                ServerId = serverId,
                ChannelId = channelId
            };
            
            using var connection = new SqliteConnection(_connectionString);
            int affectedRows = await connection.ExecuteAsync(serverDataDecrementReplySql, parameters);

            if (affectedRows > 0)
            {
                _logger.LogTrace("日期 {Date} 在频道 {ChannelId} 的回复数 -1", date, channelId);
            }
            else
            {
                _logger.LogTrace("尝试为日期 {Date} 频道 {ChannelId} 的回复数 -1, 但未找到匹配的统计行。", date, channelId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新每日回复数统计(-1)时发生错误。Date: {Date}, Channel: {ChannelId}", date, channelId);
        }
    }

    /// <summary>
    /// 尝试为指定日期、服务器和频道减去一个“已创建帖子数”和指定的“回复数”。
    /// 此方法通常在帖子被删除时调用。
    /// </summary>
    /// <param name="date">日期字符串 (格式: YYYY-MM-DD)</param>
    /// <param name="serverId">服务器ID</param>
    /// <param name="channelId">频道ID</param>
    /// <param name="postsRemoved">要减去的帖子数量</param>
    /// <param name="repliesRemoved">要减去的回复数量</param>
    /// <param name="transaction">可选的数据库事务对象</param>
    public async Task DecrementPostStatsAsync(string date, ulong serverId, ulong channelId, int postsRemoved, int repliesRemoved, System.Data.Common.DbTransaction? transaction = null)
    {
        try
        {
            var parameters = new
            {
                Date = date,
                ServerId = serverId,
                ChannelId = channelId,
                PostsRemovedCount = postsRemoved,
                ReplyRemovedCount = repliesRemoved
            };

            // ✨ 判断是使用外部事务还是创建新连接
            var connection = transaction?.Connection ?? new SqliteConnection(_connectionString);
            
            int affectedRows = await connection.ExecuteAsync(serverDataDecrementPostAndRepliesSql, parameters, transaction);

            if (affectedRows > 0)
            {
                _logger.LogTrace("日期 {Date} 在频道 {ChannelId} 的帖子数 -{Posts}, 回复数 -{Replies}", date, channelId, postsRemoved, repliesRemoved);
            }
            else
            {
                _logger.LogTrace("尝试为日期 {Date} 频道 {ChannelId} 的帖子统计-N, 但未找到匹配的统计行。", date, channelId);
            }

            // 如果是自己创建的连接，需要手动关闭
            if (transaction == null)
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新每日帖子和回复数统计(批量减少)时发生错误。Date: {Date}, Channel: {ChannelId}", date, channelId);
        }
    }
}