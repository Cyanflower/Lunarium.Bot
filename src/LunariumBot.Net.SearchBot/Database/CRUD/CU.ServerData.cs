// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===

    // SQL模板: 更新或插入服务器每日帖子创建数
    // 如果记录已存在, 则 created_posts_count + 1
    // 如果不存在, 则插入新行, created_posts_count 设为 1
    private const string serverDataUpsertCreatedPostSql = """
        INSERT INTO server_data (
            date, server_id, server_name, channel_id, channel_name, created_posts_count, reply_count
        )
        VALUES (
            @Date, @ServerId, @ServerName, @ChannelId, @ChannelName, 1, 0
        )
        ON CONFLICT(date, server_id, channel_id) DO UPDATE SET
            created_posts_count = server_data.created_posts_count + 1;
        """;

    // SQL模板: 更新或插入服务器每日帖子回复数
    // 如果记录已存在, 则 reply_count + 1
    // 如果不存在, 则插入新行, reply_count 设为 1
    private const string serverDataUpsertReplySql = """
        INSERT INTO server_data (
            date, server_id, server_name, channel_id, channel_name, created_posts_count, reply_count
        )
        VALUES (
            @Date, @ServerId, @ServerName, @ChannelId, @ChannelName, 0, 1
        )
        ON CONFLICT(date, server_id, channel_id) DO UPDATE SET
            reply_count = server_data.reply_count + 1;
        """;
        
    /// <summary>
    /// 为指定日期、服务器和频道的“已创建帖子数”加一。
    /// 如果当天该频道的统计记录不存在，则会自动创建。
    /// </summary>
    /// <param name="date">日期字符串 (格式: YYYY-MM-DD)</param>
    /// <param name="serverId">服务器ID</param>
    /// <param name="serverName">服务器名称</param>
    /// <param name="channelId">频道ID</param>
    /// <param name="channelName">频道名称</param>
    public async Task IncrementCreatedPostsCountAsync(string date, ulong serverId, string serverName, ulong channelId, string channelName)
    {
        try
        {
            var parameters = new 
            {
                Date = date,
                ServerId = serverId,
                ServerName = serverName,
                ChannelId = channelId,
                ChannelName = channelName
            };
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync(serverDataUpsertCreatedPostSql, parameters);

            _logger.Debug($"日期 {date} 在频道 {channelId} 的帖子创建数 +1");
        }
        catch (Exception ex)
        {
            _logger.Error($"更新每日帖子创建数统计时发生错误。Date: {date}, Channel: {channelId}",ex);
        }
    }

    /// <summary>
    /// 为指定日期、服务器和频道的“回复数”加一。
    /// 如果当天该频道的统计记录不存在，则会自动创建。
    /// </summary>
    /// <param name="date">日期字符串 (格式: YYYY-MM-DD)</param>
    /// <param name="serverId">服务器ID</param>
    /// <param name="serverName">服务器名称</param>
    /// <param name="channelId">频道ID</param>
    /// <param name="channelName">频道名称</param>
    public async Task IncrementReplyCountAsync(string date, ulong serverId, string serverName, ulong channelId, string channelName)
    {
        try
        {
            var parameters = new
            {
                Date = date,
                ServerId = serverId,
                ServerName = serverName,
                ChannelId = channelId,
                ChannelName = channelName
            };
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.ExecuteAsync(serverDataUpsertReplySql, parameters);
            
            _logger.Debug("日期 {date} 在频道 {channelId} 的回复数 +1");
        }
        catch (Exception ex)
        {
            _logger.Error("更新每日回复数统计时发生错误。Date: {date}, Channel: {channelId}",ex);
        }
    }
}