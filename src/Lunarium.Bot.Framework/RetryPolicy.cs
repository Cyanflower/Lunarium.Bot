using Discord.Net;
using Lunarium.Logging;
using Polly;
using Polly.Retry;

namespace Lunarium.Bot.Framework;

public static class DiscordRetryPolicy
{
    /// <summary>
    /// 配置 HTTP 请求重试策略（基于 Polly）。
    /// 该策略专门处理 Discord API 和网络相关的临时性错误。
    /// 使用指数退避策略：2秒、4秒、8秒。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    public static AsyncRetryPolicy HttpRetryPolicyConfig(ILogger logger)
    {
        var policyLogger = logger.ForContext("Retry");
        return Policy
            // 定义需要处理的异常:
            // - Discord.HttpException: 处理 5xx 服务器错误和 429 速率限制
            //   不重试: 404(资源不存在)、403(权限不足)、400(请求格式错误)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 && (int)ex.HttpCode != 403 && (int)ex.HttpCode != 400)
            // - HttpRequestException: 处理网络层级异常（SSL 握手失败、连接被重置等）
            .Or<HttpRequestException>()
            // - TaskCanceledException: 处理 HttpClient 超时
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            // - 处理 JSON 解析异常 (针对返回 HTML 错误页的情况)
            .Or<Newtonsoft.Json.JsonReaderException>()

            // 定义重试逻辑：指数退避
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2秒、4秒、8秒
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // 在日志中清晰显示触发重试的异常类型
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    policyLogger.Warning("⚠️ HTTP 请求失败 ({ErrorType}), 将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }
}