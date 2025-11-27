// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Models;

namespace LunariumBot.Net.DataCenter.DataProcess;

/// <summary>
/// DataProcessService 的部分类，专门负责论坛频道的处理逻辑。
/// </summary>
public partial class DataProcessService
{
    /// <summary>
    /// 创建或更新论坛频道及其标签信息到数据库。
    /// 该方法会提取频道的基本信息和所有标签，然后通过数据库服务将其持久化。
    /// 如果频道已存在，则更新其信息；如果不存在，则创建新记录。
    /// </summary>
    /// <param name="socketForumChannel">Discord 论坛频道对象。</param>
    public async Task CreateOrUpdateChannelProcessAsync(SocketForumChannel socketForumChannel)
    {
        _logger.Debug("开始处理论坛频道创建/更新: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
            socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);

        // 构建频道数据对象
        var channelData = new ChannelData()
        {
            ChannelId = socketForumChannel.Id,
            ChannelName = socketForumChannel.Name,
            GuildId = socketForumChannel.Guild.Id,
            GuildName = socketForumChannel.Guild.Name
        };

        // 收集频道下的所有标签信息
        List<TagData> tagDatas = new();
        foreach (var tag in socketForumChannel.Tags)
        {
            tagDatas.Add(new TagData()
            {
                TagId = tag.Id,
                ChannelId = socketForumChannel.Id,
                TagName = tag.Name,
                ChannelName = socketForumChannel.Name,
                GuildName = socketForumChannel.Guild.Name,
                UsageCount = 0
            });
        }

        _logger.Debug("论坛频道 {ChannelName} (ID: {ChannelId}) 包含 {TagCount} 个标签",
            socketForumChannel.Name, socketForumChannel.Id, tagDatas.Count);

        try
        {
            // 使用数据库重试策略执行数据库操作
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction) await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.UpsertForumChannelAsync(transaction, channelData, tagDatas);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功处理论坛频道: [{GuildName}.{ChannelName}] (ID: {ChannelId}), 标签数: {TagCount}",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id, tagDatas.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理论坛频道创建/更新失败: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
        }
    }

    /// <summary>
    /// 从数据库中删除论坛频道及其所有关联的标签和帖子。
    /// 该操作会触发数据库的级联删除，自动清理所有相关数据。
    /// </summary>
    /// <param name="socketForumChannel">要删除的 Discord 论坛频道对象。</param>
    public async Task DeleteChannelProcessAsync(SocketForumChannel socketForumChannel)
    {
        _logger.Info("开始处理论坛频道删除: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
            socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);

        List<ulong> TagIds = socketForumChannel.Tags.Select(tag => tag.Id).ToList();

        try
        {
            int affectedRows = 0;
            // 使用数据库重试策略执行删除操作
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                // 数据库级联规则会自动处理其关联的标签和帖子
                try
                {
                    affectedRows = await _databaseService.DeleteForumChannelAsync(transaction, socketForumChannel.Id);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (affectedRows > 0)
            {
                _logger.Info("✅ 成功删除论坛频道: [{GuildName}.{ChannelName}] (ID: {ChannelId}), 已清理 {TagCount} 个标签",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id, TagIds.Count);
            }
            else
            {
                _logger.Warning("⚠️ 尝试删除频道 [{GuildName}.{ChannelName}] (ID: {ChannelId})，但该频道在数据库中未找到。",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
            }
            _logger.Info("✅ 成功删除论坛频道: [{GuildName}.{ChannelName}] (ID: {ChannelId}), 已清理 {TagCount} 个标签",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id, TagIds.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 删除论坛频道失败: [{GuildName}.{ChannelName}] (ID: {ChannelId})",
                socketForumChannel.Guild.Name, socketForumChannel.Name, socketForumChannel.Id);
        }
    }
}