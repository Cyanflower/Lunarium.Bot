using System.Net.Sockets;

using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

using Lunarium.Bot.Logger;
using Lunarium.Bot.Extensions;

using Lunarium.Bot.SearchBot.Database;
using Lunarium.Bot.SearchBot.Models;
using Lunarium.Bot.SearchBot.Utils;

namespace Lunarium.Bot.SearchBot.ApplicationServices;

public partial class ApplicationService
{
    public async Task<ServiceResult> AddBlockedAuthorServiceAsync(ulong userId, ulong authorId)
    {
        try
        {
            if (userId == authorId) return new() { Status = ServiceStatus.InvalidInput, Message = "不能将自己加入自己的搜索屏蔽名单" };
            var serviceResult = await _dbRetryPolicy.ExecuteAsync( async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    var result = await _databaseService.AddBlockedAuthorAsync(transaction, userId, authorId);
                    await transaction.CommitAsync();
                    if (result == InsertResult.Inserted)
                    {
                        return new ServiceResult() { Status = ServiceStatus.Success, Message = $"成功将 <@{authorId}> 加入搜索屏蔽名单" };
                    }
                    else
                    {
                        return new ServiceResult() { Status = ServiceStatus.Conflict, Message = $"<@{authorId}> 在此前已经位于您的搜索屏蔽名单中了, 正在尝试为您查询详细信息..." };
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
            return serviceResult;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 屏蔽名单添加目标失败: 请求人ID: {userId}, 目标ID: {authorId}, 错误ID: {errorId}", userId, authorId, errorId);
            return new() { Status = ServiceStatus.InternalError, Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```" };
        }
    }

    public async Task<DateTimeOffset?> QueryBlockedAuthorInfoServiceAsync(ulong userId, ulong authorId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryUserBlockedAuthorInfoAsync(userId, authorId);
            });
            return result;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询屏蔽名单被屏蔽者信息失败: 请求人ID: {userId}, 目标ID: {authorId}, 错误ID: {errorId}", userId, authorId, errorId);
            return null;
        }
    }

    public async Task<ServiceResult> RemoveBlockedAuthorServiceAsync(ulong userId, ulong authorId)
    {
        try
        {
            await using var connection = _databaseService.GetConnection();
            await connection.OpenAsync();
            await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.RemoveBlockedAuthorAsync(transaction, userId, authorId);
            });
            if (result == DeleteResult.Success)
            {
                return new() { Status = ServiceStatus.Success, Message = $"成功将 <@{authorId}> 从屏蔽名单中移除" };
            }
            else
            {
                return new() { Status = ServiceStatus.NotFound, Message = $"<@{authorId}> 不在您的屏蔽名单中" };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 从屏蔽名单移除失败: 请求人ID: {userId}, 目标ID: {authorId}, 错误ID: {errorId}", userId, authorId, errorId);
            return new() { Status = ServiceStatus.InternalError, Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```" };
        }
    }
    
    public async Task<List<BlockedInfo>> QueryUserAllBlockedInfoServiceAsync(ulong userId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryUserAllBlockedAsync(userId);
            });
            return result; 
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询屏蔽名单全部列表失败: 请求人ID: {userId}, 错误ID: {errorId}", userId, errorId);
            return new();
        }
    }
}