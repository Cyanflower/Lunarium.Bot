using System;
using System.Text;

using Discord;
using Discord.Net;
using Discord.Interactions;
using Discord.Rest;
using Polly;
using Polly.Retry;

using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.DataCenter.AppInteractionService;

public class ScanProgressReporter
{
    private readonly SocketInteractionContext _context;
    private readonly ILogger _logger;
    private readonly StringBuilder _description;
    private readonly EmbedBuilder _embedBuilder;
    private readonly AsyncRetryPolicy asyncRetryPolicy;
    private RestUserMessage? _message;
    private bool _isInitialized = false;
    public ScanProgressReporter(SocketInteractionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _description = new StringBuilder();
        _embedBuilder = new EmbedBuilder();
        asyncRetryPolicy = HttpRetryPolicyConfig();
    }

    public async Task ReportStartAsync(string title, Color color)
    {
        try
        {
            _embedBuilder.WithTitle(title).WithColor(color);
            await asyncRetryPolicy.ExecuteAsync(async () =>
            {
                _message = await _context.Channel.SendMessageAsync(embed: _embedBuilder.Build());
                _isInitialized = true;
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex,"ReportStartAsync Ex:");
        }
    }
    
    public void AppendLine(string description)
    {
        _description.AppendLine(description);
    }

    public async Task ReportProgressAsync(string? description = null, Color? color = null)
    {
        if (_message is not null && _isInitialized)
        {
            try
            {
                if (description is not null) _description.AppendLine(description);

                _embedBuilder.WithDescription(_description.ToString());

                if (color is not null) _embedBuilder.WithColor(color.Value);

                await asyncRetryPolicy.ExecuteAsync(async () =>
                {
                    await _message.ModifyAsync(property => { property.Embeds = new[] { _embedBuilder.Build() }; });
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ReportProgressAsync Ex:");
            }
        }
        else
        {
            _logger.Warning("ScanProgressReporter 尚未通过 ReportStartAsync 完成初始化");
        }
    }
    
    public async Task ReportCompleteAsync(string? description = null, string? title = null, Color? color = null)
    {
        if (_message is not null && _isInitialized)
        {
            try
            {
                if (description is not null) _description.AppendLine(description);

                _embedBuilder.WithTitle(title);
                
                _embedBuilder.WithDescription(_description.ToString());

                if (color is not null) _embedBuilder.WithColor(color.Value);

                await asyncRetryPolicy.ExecuteAsync(async () =>
                {
                    await _message.ModifyAsync(property => { property.Embeds = new[] { _embedBuilder.Build() }; });
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ReportProgressAsync Ex:");
            }
        }
        else
        {
            _logger.Warning("ScanProgressReporter 尚未通过 ReportStartAsync 完成初始化");
        }
    }

    private AsyncRetryPolicy HttpRetryPolicyConfig()
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

                    _logger.Warning("⚠️ HTTP 请求失败 ({ErrorType})，将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }
}
