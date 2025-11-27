using System.Net.Sockets;

using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

using LunariumBot.Net.Logger;
using LunariumBot.Net.Extensions;

using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.ApplicationServices;

public partial class ApplicationService
{

    public async Task<ServiceResult<List<FavoriteTypeInfo>>> GetFavoriteTypeServiceAsync(ulong userId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.GetFavoriteTypeAsync(userId);
            });
            if (result.Count == 0)
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "您目前还没有任何收藏夹..."
                };
            }
            return new()
            {
                Status = ServiceStatus.Success,
                Payload = result,
                Message = "查询成功"
            }; ;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询收藏夹列表列表失败: 请求人ID: {userId}, 错误ID: {errorId}", userId, errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult<FavoriteInfo>> CheckFavoriteExistServiceAsync(ulong userId, ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CheckFavoriteExistAsync(userId, threadId);
            });

            if (result is not null)
            {
                return new()
                {
                    Status = ServiceStatus.Exist,
                    Payload = result,
                    Message = "您已收藏过该帖子了"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "您尚未收藏该帖子"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询收藏记录失败: 请求人ID: {userId}, 目标帖子ID: {threadId}, 错误ID: {errorId}", userId, threadId, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }
    
    public async Task<ServiceResult> RemoveFavoriteServiceAsync(ulong userId, ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.RemoveFavoriteAsync(userId, threadId);
            });

            if (result == DeleteResult.Success)
            {
                return new() { Status = ServiceStatus.Success, Message = "已成功取消收藏" };
            }
            else
            {
                return new() { Status = ServiceStatus.NotFound, Message = "您并未收藏该帖子" };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 移除收藏失败: 请求人ID: {userId}, 帖子ID: {threadId}, 错误ID: {errorId}", userId, threadId, errorId);
            return new() { Status = ServiceStatus.InternalError, Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```" };
        }
    }

    public async Task<ServiceResult<List<FavoriteInfo>>> QueryUserFavoritesServiceAsync(ulong userId, long? favoriteAs)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryUserFavoritesAsync(userId, favoriteAs);
            });

            if (result.Count == 0)
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = result,
                    Message = "该分类下暂无收藏"
                };
            }

            return new()
            {
                Status = ServiceStatus.Success,
                Payload = result,
                Message = $"成功查询到 {result.Count} 条收藏"
            };
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询收藏列表失败: 请求人ID: {userId}, 分类ID: {favoriteAs}, 错误ID: {errorId}", userId, favoriteAs?.ToString() ?? "ALL", errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult<FavoriteTypeInfo>> CreateFavoriteTypeServiceAsync(ulong userId, string typeName)
    {
        try
        {
            // 2. 执行数据库操作
            var createdInfo = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CreateFavoriteTypeAsync(userId, typeName);
            });

            // 3. 结果判断
            if (createdInfo is not null)
            {
                // 成功创建，返回完整的对象（包含生成的ID）
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = createdInfo,
                    Message = $"成功创建收藏夹: {typeName}"
                };
            }
            else
            {
                // createdInfo 为 null，说明触发了 ON CONFLICT
                return new()
                {
                    Status = ServiceStatus.Conflict,
                    Payload = null,
                    Message = $"您已经拥有名为 \"**{typeName}**\" 的收藏夹了"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 创建收藏夹失败: 请求人ID: {userId}, 名称: {typeName}, 错误ID: {errorId}", userId, typeName, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult<FavoriteInfo>> CreateFavoriteServiceAsync(FavoriteInfoDb favoriteInfoDb)
    {
        try
        {
            var resultDb = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.AddUserFavoriteAsync(favoriteInfoDb);
            });

            if (resultDb is not null)
            {
                // 成功: 将数据库返回的权威数据转换为业务模型返回
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = resultDb,
                    Message = "收藏成功"
                };
            }
            else
            {
                // resultDb 为 null，说明触发了 ON CONFLICT
                return new()
                {
                    Status = ServiceStatus.Conflict,
                    Payload = null,
                    Message = "该帖子已在您的收藏列表中"
                };
            }
        }
        catch (InvalidOperationException) // 捕获 DB 层的收藏夹不存在异常
        {
             return new()
            {
                Status = ServiceStatus.NotFound,
                Message = "操作失败: 指定的收藏夹不存在或已被删除"
            };
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 创建收藏失败: 请求人ID: {userId}, 帖子ID: {threadId}, 收藏夹ID: {favoriteAs}, 错误ID: {errorId}",
                favoriteInfoDb.UserId, 
                favoriteInfoDb.ThreadId, 
                favoriteInfoDb.FavoriteAs, 
                errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }
}