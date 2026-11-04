using System;
using System.Text;

using Discord;
using Discord.Interactions;
using Discord.Rest;
using Polly.Retry;

using Lunarium.Bot.Framework.RateLimiting;
using Lunarium.Bot.DataCenter.Utils;
using Lunarium.Logging;

namespace Lunarium.Bot.DataCenter.AppInteractionService;

public class ScanProgressReporter
{
    private readonly SocketInteractionContext _context;
    private readonly ILogger _logger;
    private readonly StringBuilder _description;
    private readonly EmbedBuilder _embedBuilder;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private RestUserMessage? _message;
    private bool _isInitialized = false;
    public ScanProgressReporter(
        SocketInteractionContext context,
        ILogger logger,
        RateLimitedExecutor rateLimitedExecutor,
        AsyncRetryPolicy httpRetryPolicy)
    {
        _context = context;
        _logger = logger.ForContext("ScanProgressReporter");
        _description = new StringBuilder();
        _embedBuilder = new EmbedBuilder();
        _rateLimitedExecutor = rateLimitedExecutor;
        _httpRetryPolicy = httpRetryPolicy;
    }

    public async Task ReportStartAsync(string title, Color color)
    {
        try
        {
            _embedBuilder.WithTitle(title).WithColor(color);
            // 限速在重试内部：每次重试尝试都会重新排队取令牌
            _message = await _httpRetryPolicy.ExecuteAsync(
                token => _rateLimitedExecutor.WriteAndWaitAsync(
                    async _ => await _context.Channel.SendMessageAsync(embed: _embedBuilder.Build()),
                    token),
                CancellationToken.None);
            _isInitialized = true;
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

                // 限速在重试内部：每次重试尝试都会重新排队取令牌
                var message = _message;
                await _httpRetryPolicy.ExecuteAsync(
                    token => _rateLimitedExecutor.WriteAndWaitAsync(
                        async _ => await message.ModifyAsync(property => { property.Embeds = new[] { _embedBuilder.Build() }; }),
                        token),
                    CancellationToken.None);
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

                // 限速在重试内部：每次重试尝试都会重新排队取令牌
                var message = _message;
                await _httpRetryPolicy.ExecuteAsync(
                    token => _rateLimitedExecutor.WriteAndWaitAsync(
                        async _ => await message.ModifyAsync(property => { property.Embeds = new[] { _embedBuilder.Build() }; }),
                        token),
                    CancellationToken.None);
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
}
