
using LunariumBot.Net.Extensions;
using LunariumBot.Net.ReleaseBot.Models;
using LunariumBot.Net.ReleaseBot.Utils;

namespace LunariumBot.Net.ReleaseBot.ApplicationServices;


public partial class ApplicationService
{
    public async Task<ulong?> QueryCacheChannelAsync(ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryCacheChannelAsync(threadId);
            });
            if (result.HasValue)
            {
                return result.Value.ToDiscordId();
            }
            return null;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ QueryCacheChannelAsync: 查询 Thread {ThreadId} 的缓存频道失败", errorId, threadId);
            return null;
        }
    }

    public async Task<long?> InsertWorkDataAsync(WorkData workData)
    {
        try
        {
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();

            long? resultId = null; 
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    resultId = await _databaseService.InsertWorkDataAsync(transaction, workData);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

            });
            return resultId;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ InsertWorkDataAsync: 写入作品信息 {@workData} 失败", errorId, workData);
            return null;
        }
    }

    public async Task<int?> InsertDownloadLogAsync(DownloadLog downloadLog)
    {
        try
        {
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();
            await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

            int? result = null;
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    result = await _databaseService.InsertDownloadLogAsync(transaction, downloadLog);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
            return result;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ InsertDownloadLogAsync: 写入作品信息 {@workData} 失败", errorId, downloadLog);
            return null;
        }
    }

    /// 根据发布消息ID获取完整的 WorkData 对象
    public async Task<WorkData?> GetWorkDataAsync(ulong releaseMessageId)
    {
        try
        {
            var dbResult = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryWorkDataByMessageIdAsync(releaseMessageId);
            });

            return dbResult?.ToDomain();
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ GetWorkDataAsync: 获取 Message {ReleaseMessageId} 数据失败", errorId, releaseMessageId);
            return null;
        }
    }

    // 获取指定帖子内最新的作品数据
    public async Task<WorkData?> GetLatestWorkDataByThreadIdAsync(ulong threadId)
    {
        try
        {
            var dbResult = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryLatestWorkDataByThreadIdAsync(threadId);
            });

            return dbResult?.ToDomain();
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ GetLatestWorkDataByThreadIdAsync: 获取 Thread {ThreadId} 数据失败", errorId, threadId);
            return null;
        }
    }

    // 获取指定帖子内指定 ID 的作品数据
    public async Task<WorkData?> GetWorkDataByWorkIdAsync(ulong threadId, long workId)
    {
        try
        {
            var dbResult = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryWorkDataByWorkIdAsync(threadId, workId);
            });

            return dbResult?.ToDomain();
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ GetLatestWorkDataByThreadIdAsync: 获取 Thread {ThreadId} 数据失败", errorId, threadId);
            return null;
        }
    }

    /// 根据发布消息ID获取发布密码
    public async Task<string?> GetReleasePasswordAsync(ulong releaseMessageId)
    {
        try
        {
            // 查询操作
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryReleasePasswordAsync(releaseMessageId);
            });
            return result;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ GetReleasePasswordAsync: 获取 Message {ReleaseMessageId} 密码失败", errorId, releaseMessageId);
            return null;
        }
    }

    // 软删除指定发布消息的数据
    public async Task<bool> SoftDeleteWorkDataAsync(ulong releaseMessageId)
    {
        try
        {
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();

            int rowsAffected = 0;
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    rowsAffected = await _databaseService.SoftDeleteWorkDataAsync(transaction, releaseMessageId);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ SoftDeleteWorkDataAsync: 软删除 Message {ReleaseMessageId} 失败", errorId, releaseMessageId);
            return false;
        }
    }

    // 根据 ThreadId 硬删除相关数据
    public async Task<bool> DeleteWorkDataByThreadIdAsync(ulong threadId)
    {
        try
        {
            // 写操作：服务层负责连接和事务生命周期
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();

            var rowsAffected = 0;
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    rowsAffected = await _databaseService.HardDeleteWorkDataByThreadIdAsync(transaction, threadId);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
                
            });

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ DeleteWorkDataByThreadIdAsync: 硬删除 Thread {ThreadId} 失败", errorId, threadId);
            return false;
        }
    }

    // 检查指定用户在指定帖子中是否已发送过消息
    public async Task<bool> HasMessageRecordAsync(ulong authorId, ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CheckMessageExistsAsync(authorId, threadId);
            });
            return result;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ HasMessageRecordAsync: 检查 Author {AuthorId} 在 Thread {ThreadId} 的记录失败", errorId, authorId, threadId);
            // 发生异常时保守返回 false，视具体业务逻辑可调整
            return false;
        }
    }

    // 检查指定用户在指定帖子中是否进行过反应 (Reaction)
    public async Task<bool> HasReactionRecordAsync(ulong userId, ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CheckReactionExistsAsync(userId, threadId);
            });
            return result;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ HasReactionRecordAsync: 检查 User {UserId} 在 Thread {ThreadId} 的反应记录失败", errorId, userId, threadId);
            return false;
        }
    }

    /// 检查指定用户是否已经下载过该发布消息 (通过查询日志记录)。
    public async Task<bool> HasDownloadRecordAsync(ulong userId, ulong releaseMessageId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CheckDownloadLogExistsAsync(userId, releaseMessageId);
            });
            return result;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ HasDownloadRecordAsync: 检查 User {UserId} 对 Message {ReleaseMessageId} 的下载记录失败", errorId, userId, releaseMessageId);
            // 发生数据库异常时保守返回 false，即假定未下载，允许重试或记录新日志（视业务需求而定）
            return false;
        }
    }

    public async Task<bool> UpdateWorkCacheAsync(WorkData updateModel)
    {
        try
        {
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();

            int rowsAffected = 0;
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    rowsAffected = await _databaseService.UpdateWorkCacheAsync(transaction, updateModel);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
            if (rowsAffected > 0)
            {
                _logger.Debug("✅ UpdateWorkCacheAsync: 成功更新 Message {ReleaseMessageId} 的缓存信息。", updateModel.ReleaseMessageId);
                return true;
            }
            else
            {
                _logger.Warning("⚠️ UpdateWorkCacheAsync: 未找到 Message {ReleaseMessageId}，更新未生效。", updateModel.ReleaseMessageId);
                return false;
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ UpdateWorkCacheAsync: 更新 Message {ReleaseMessageId} 缓存失败",
                errorId, updateModel.ReleaseMessageId);
            return false;
        }
    }
    
    public async Task<bool> UpdatePostUpdateInfoAsync(ulong threadId, DateTimeOffset updatedAt, string updatedJumpUrl)
    {
        try
        {
            int rowsAffected = 0;
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();
            
            // 使用 Polly 重试策略执行数据库操作
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    rowsAffected = await _databaseService.UpdatePostUpdateInfoAsync(transaction, threadId, updatedAt, updatedJumpUrl);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (rowsAffected > 0)
            {
                // _logger.Debug("✅ 成功更新 Thread {ThreadId} 的最后更新时间为 {UpdatedAt}", threadId, updatedAt);
                return true;
            }
            else
            {
                _logger.Warning("⚠️ UpdatePostUpdateInfoAsync: 未找到 Thread {ThreadId}，更新未生效。", threadId);
                return false;
            }
        }
        catch (Exception ex)
        {
            // 生成错误追踪ID
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "[{errorId}] ❌ UpdatePostUpdateInfoAsync: 更新 Thread {ThreadId} 信息失败", errorId, threadId);
            return false;
        }
    }
}