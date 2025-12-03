// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Npgsql;
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
    public async Task SyncAllThreadAsync(List<ChannelData> allForumsData, List<TagData> allTagsData, List<ThreadData> allThreadsData)
    {
        try
        {
            _logger.Info("📊 开始同步处理: 频道 {ChannelCount}, 标签 {TagCount}, 帖子 {ThreadCount}",
                allForumsData.Count, allTagsData.Count, allThreadsData.Count);
                
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await _databaseService.SyncAllChannelsAndTagsAsync(transaction, allForumsData, allTagsData);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            const int batchSize = 1000;
            int totalBatches = (int)Math.Ceiling((double)allThreadsData.Count / batchSize);

            for (int i = 0; i < totalBatches; i++)
            {
                var batchData = allThreadsData
                    .Skip(i * batchSize)
                    .Take(batchSize)
                    .ToList();
                _logger.Info("... 正在处理第 {CurrentBatch}/{TotalBatches} 批帖子，数量: {BatchCount}", i + 1, totalBatches, batchData.Count);
                await _dbRetryPolicy.ExecuteAsync(async () =>
                {
                    await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();

                    try
                    {
                        await _databaseService.SyncAllThreadsAsync(transaction, batchData);
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });
            }

            _logger.Info("🗑️ 正在清理孤儿帖子...");
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.DeleteOrphanThreadsAsync(
                        transaction,
                        allThreadsData.Select(t => t.ThreadId).ToList(),
                        allThreadsData.Select(t => t.ChannelId).Distinct().ToList());
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

    public async Task SyncMembersAndRolesAsync(List<RoleData> roleDatas, List<UserData> memberDatas)
    {
        try
        {
            _logger.Info("📊 开始同步处理: 身份组: {roleDatasCount}, 用户: {memberDatasCount}",
                roleDatas.Count, memberDatas.Count);
                
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await _databaseService.SyncAllRolesAsync(transaction, roleDatas);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await _databaseService.DeleteOutdatedRolesAsync(transaction, roleDatas);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await _databaseService.SyncAllMembersAsync(transaction, memberDatas);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await _databaseService.SyncAllMemberRolesAsync(transaction, memberDatas);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }
}