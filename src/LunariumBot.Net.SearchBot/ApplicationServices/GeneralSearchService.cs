using System.Net.Sockets;

using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

using LunariumBot.Net.Logger;
using LunariumBot.Net.Extensions;

using LunariumBot.Net.SearchBot.Paginator;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.ApplicationServices;

public partial class ApplicationService
{
    public async Task<ServiceResult<IPaginatedView>> GeneralSerachService(ulong userId, SearchConditions searchConditions)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.GeneralSearch(searchConditions);
            });

            if (result.Count > 0)
            {
                var cacheItems = _searchCacheService.CacheItems(result);
                var view = _paginatorManager.RegisterView<PostDisplayModel>(userId, cacheItems);
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = view,
                    Message = "OK"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "未找到符合条件的结果"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 通用搜索失败: 请求人ID: {userId}, 搜索条件: {@searchConditions}, 错误ID: {errorId}", userId, searchConditions, errorId);
            return new() { Status = ServiceStatus.InternalError, Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```" };
        }
    }

    public async Task<ServiceResult> SerachForCacheService(List<ulong> threadIds)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.SearchForCache(threadIds);
            });

            if (result.Count > 0)
            {
                var cacheItems = _searchCacheService.CacheItems(result);
                var view = _paginatorManager.RegisterView<PostDisplayModel>(userId, cacheItems);
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = view,
                    Message = "OK"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "未找到符合条件的结果"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 通用搜索失败: 请求人ID: {userId}, 搜索条件: {@searchConditions}, 错误ID: {errorId}", userId, searchConditions, errorId);
            return new() { Status = ServiceStatus.InternalError, Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```" };
        }
    }
}