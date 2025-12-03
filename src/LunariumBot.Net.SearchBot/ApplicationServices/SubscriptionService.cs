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
    public async Task<ServiceResult<SubscriptionData>> CheckSubscriptionExistServiceAsync(ulong userId, ulong authorId)
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
}