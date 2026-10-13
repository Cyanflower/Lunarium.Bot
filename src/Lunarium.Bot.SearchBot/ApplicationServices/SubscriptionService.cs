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
    public async Task<ServiceResult<SubscriptionData>> CheckSubscriptionExistAsync(ulong userId, ulong authorId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CheckSubscriptionExistAsync(userId, authorId);
            });

            if (result is not null)
            {
                return new()
                {
                    Status = ServiceStatus.Exist,
                    Payload = result,
                    Message = "您已关注过该作者了"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "您尚未关注该作者"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询关注记录失败: 请求人ID: {userId}, 目标用户ID: {authorId}, 错误ID: {errorId}", userId, authorId, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult> AddSubscriptionAsync(ulong userId, ulong authorId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    return await _databaseService.AddSubscriptionAsync(transaction, userId, authorId);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (result == InsertResult.AlreadyExists)
            {
                return new()
                {
                    Status = ServiceStatus.Conflict,
                    Message = "您已关注过该作者了"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.Success,
                    Message = $"成功关注作者 <@{authorId}>"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 添加关注失败: 请求人ID: {userId}, 目标用户ID: {authorId}, 错误ID: {errorId}", userId, authorId, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult> RemoveSubscriptionAsync(ulong userId, ulong authorId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    return await _databaseService.RemoveSubscriptionAsync(transaction, userId, authorId);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (result == DeleteResult.NoExists)
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Message = "您尚未关注该作者"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.Success,
                    Message = $"成功取消关注作者 <@{authorId}>"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 移除关注失败: 请求人ID: {userId}, 目标用户ID: {authorId}, 错误ID: {errorId}", userId, authorId, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }
    
    public async Task<ServiceResult<List<SubscriptionData>>> QueryUserSubscriptionsAsync(ulong userId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    return await _databaseService.QueryUserSubscriptionsAsync(userId);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (result.Count == 0 || result is null)
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "您尚未关注任何作者"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = result,
                    Message = $"成功"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询关注列表失败: 请求人ID: {userId}, 错误ID: {errorId}", userId, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }
}