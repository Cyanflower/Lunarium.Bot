using System.Threading.RateLimiting;
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.Extensions;

public static class RateLimiterExtensions
{
    /// <summary>
    /// 尝试获取限速令牌，并在获取失败(队列已满)时记录错误日志，但仍返回 Lease 对象以供 Dispose。
    /// </summary>
    /// <param name="rateLimiter">限速器实例</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="permitCount">需要的令牌数量，默认为 1</param>
    /// <returns>RateLimitLease，需使用 using 进行释放</returns>
    public static async ValueTask<RateLimitLease> AcquireWithLoggingAsync(
        this RateLimiter rateLimiter, 
        ILogger logger, 
        int permitCount = 1)
    {
        // 异步等待获取令牌
        var lease = await rateLimiter.AcquireAsync(permitCount);

        // 检查结果
        if (!lease.IsAcquired)
        {
            // 统一记录日志
            // 这种情况通常发生在 QueueLimit 已满时
            logger.Error($"[RateLimiter] 获取令牌失败 (IsAcquired=false)。本地限速队列已满，将跳过本地限速，直接请求 API。");
        }

        // 4. 返回 lease，交由调用方使用 using 释放
        return lease;
    }
}