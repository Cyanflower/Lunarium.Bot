// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.SearchBot.Database;

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
                _logger.Debug($"日期 {date} 在频道 {channelId} 的回复数 -1");
            }
            else
            {
                _logger.Debug($"尝试为日期 {date} 频道 {channelId} 的回复数 -1, 但未找到匹配的统计行。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error( $"更新每日回复数统计(-1)时发生错误。Date: {date}, Channel: {channelId}", ex);
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
                _logger.Info($"日期 {date} 在频道 {channelId} 的帖子数 -{postsRemoved}, 回复数 -{repliesRemoved}");
            }
            else
            {
                _logger.Info($"尝试为日期 {date} 频道 {channelId} 的帖子统计-N, 但未找到匹配的统计行。");
            }

            // 如果是自己创建的连接，需要手动关闭
            if (transaction == null)
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"更新每日帖子和回复数统计(批量减少)时发生错误。Date: {date}, Channel: {channelId}", ex);
        }
    }
}