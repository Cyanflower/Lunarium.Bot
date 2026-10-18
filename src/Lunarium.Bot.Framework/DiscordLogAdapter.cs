using Lunarium.Common.Net;
using Lunarium.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

namespace Lunarium.Bot.Framework;

public sealed class DiscordLogAdapter(ILogger logger)
{
    // 累计为: [xxxxBotService.DiscordLogAdapter]
    private readonly ILogger _logger = logger.ForContext("DiscordLogAdapter");

    public Task Handle(LogMessage log)
    {
        // 将 Discord.Net 的日志转换为 Lunarium.Logging 日志
        switch (log.Severity)
        {
            case LogSeverity.Critical:
                _logger.Critical(log.Exception, "[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Error:
                _logger.Error(log.Exception, "[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Warning:
                _logger.Warning("[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Info:
                _logger.Info("[Discord.Net] {log.Message}", log.Message);
                break;
            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                _logger.Debug("[Discord.Net] {log.Message}", log.Message);
                break;
        }
        return Task.CompletedTask;
    }
}