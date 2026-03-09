using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Interactions;
using Polly;
using Polly.Retry;
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.VoteBot.Utils;

public static class Util
{
    private static DateTimeOffset SubmitExpireTime = DateTimeOffset.Parse("2026-01-08T23:59:00+08:00");
    
    private static DateTimeOffset VoteExpireTime = DateTimeOffset.Parse("2026-01-08T23:59:00+08:00");
    public static MessageComponent GetSubmitMessageComponent()
    {
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Blue)
                .WithTextDisplay("## 🎉 旅程 2026 第一季度管理组换届普选--报名处")

                .WithSeparator(SeparatorSpacingSize.Large)

                .WithTextDisplay("""
                    ### 📁 岗位公示
                    ⚖️ 风纪组
                    资源区 -- 7-8人
                    > 女性向/男性向/其他分区
                    聊天区 -- 3人
                    """)

                .WithSeparator(SeparatorSpacingSize.Small)

                .WithTextDisplay("""
                    ✒️ 事务组
                    记者 -- 2人（需试稿）
                    赛事委员 -- 2-3人
                    """)

                .WithSeparator(SeparatorSpacingSize.Large)

                .WithTextDisplay("""
                    🛡️ 报名资格
                    身份要求：仅限 创作者 / 破限组 / 社区志愿者 申请。
                    活跃要求：2025.9.1 - 1215.12.31 期间在资源/教程区有有效发布记录。
                    规则：最多可填报两个志愿，记者需提交审核作品。
                    """)

                .WithSeparator(SeparatorSpacingSize.Small)

                .WithTextDisplay("""
                    ⏳ 报名时间
                    2026年1月2日 00:00 —— 2026年01月09日 23:59 (UTC+8)
                    """)

                .WithSeparator(SeparatorSpacingSize.Large)

                .WithActionRow(row => row
                    .WithButton(new ButtonBuilder("点此报名", "Submit", ButtonStyle.Success, emote: new Emoji("📝"), isDisabled: DateTimeOffset.UtcNow > SubmitExpireTime))
                    .WithButton(new ButtonBuilder("撤销报名", "Cancel", ButtonStyle.Danger, emote: new Emoji("⚠️"), isDisabled: DateTimeOffset.UtcNow > SubmitExpireTime))
                )
            )
            .Build();
    }

    public static MessageComponent GetVoteMessageComponent()
    {
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Blue)
                .WithTextDisplay("## 🎉 旅程 2026 第一季度管理组换届普选--投票处")

                .WithSeparator(SeparatorSpacingSize.Large)

                .WithTextDisplay("""
                    🛡️ 投票资格
                    身份要求：仅限 创作者 / 破限组 / 社区志愿者 申请。
                    活跃要求：2025.9.1 后在资源/教程区有有效发布记录。
                    规则：最多可填报两个志愿，记者需提交审核作品。
                    """)

                .WithSeparator(SeparatorSpacingSize.Small)

                .WithTextDisplay("""
                    ⏳ 投票时间
                    2026年1月1日 0:00 —— 2026年XX月XX日 23:59 (UTC+8)
                    """)

                .WithSeparator(SeparatorSpacingSize.Large)

                .WithActionRow(row => row
                    .WithButton(new ButtonBuilder("点此投票", "Vote", ButtonStyle.Success, emote: new Emoji("📝"), isDisabled: DateTimeOffset.UtcNow > VoteExpireTime))
                )
            )
            .Build();
    }

    /// <summary>
    /// 配置 HTTP 请求重试策略（基于 Polly）。
    /// 该策略专门处理 Discord API 和网络相关的临时性错误。
    /// 使用指数退避策略：2秒、4秒、8秒。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    public static AsyncRetryPolicy HttpRetryPolicyConfig(ILogger? logger = null)
    {
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

                    logger?.Warning("⚠️ HTTP 请求失败 ({ErrorType}), 将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }
}
