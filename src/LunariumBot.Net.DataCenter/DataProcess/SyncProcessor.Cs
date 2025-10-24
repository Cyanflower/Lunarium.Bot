// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Npgsql;
using Discord;
using Discord.WebSocket;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.DataProcess;

/// <summary>
/// DataProcessService 的部分类，专门负责论坛频道的处理逻辑。
/// </summary>
public partial class DataProcessService
{
    public async Task SyncProcessorAsync(List<ChannelData> allForumsData, List<TagInfo> allTagsData, List<ThreadData> allThreadsData)
    {
        try
        {
            _logger.Info("📊 开始同步处理: 频道 {ChannelCount}, 标签 {TagCount}, 帖子 {ThreadCount}",
                allForumsData.Count, allTagsData.Count, allThreadsData.Count);

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.SyncAllChannelsAndTagsAsync(transaction, allForumsData, allTagsData);
                    await _databaseService.SyncAllThreadsAsync(transaction, allThreadsData);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 同步处理完成: 频道 {ChannelCount}, 标签 {TagCount}, 帖子 {ThreadCount}",
                allForumsData.Count, allTagsData.Count, allThreadsData.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SyncProcessorAsync: 同步处理失败，涉及 {ChannelCount} 个频道，{TagCount} 个标签，{ThreadCount} 个帖子",
                allForumsData.Count, allTagsData.Count, allThreadsData.Count);
            throw;
        }
    }
}