using Lunarium.Scheduler;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using Discord;
using Discord.Interactions;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Bot.Framework.Health;
using Lunarium.Bot.Framework.Interaction;
using System.ComponentModel;

namespace Lunarium.Bot.Framework;

public sealed class Notifier
{
    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    public Notifier(ILogger logger, DiscordSocketClient client)
    {
        _logger = logger;
        _client = client;
    }

    public async Task ReConnect(ulong maintainerId, HealthMonitor? healthMonitor = null)
    {
        try
        {
            // 获取监控日志字符串
            var latencyLogs = healthMonitor?.GetLatencyLogs(5) ?? ["HealthMonitor Not Running"];
            var connectStateLogs = healthMonitor?.GetConnectStateLogs(5) ?? ["HealthMonitor Not Running"];

            _logger.Warning("BOT 客户端重新连接, 登录 BOT : {ClientCurrentUserUsername}({ClientCurrentUserId})", _client.CurrentUser.Username, _client.CurrentUser.Id);
            _logger.Warning("检测到 BOT 客户端在运行时重新连接 Discord API 网关事件");
            var sb = new StringBuilder();
            sb.Append($"""
            ### BOT 客户端重新连接
            登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
            检测到 BOT 客户端在运行时重新连接 Discord API 网关事件
            """);
            if (healthMonitor is not null)
            {
                foreach (var log in latencyLogs)
                {
                    _logger.Warning(log);
                    sb.AppendLine(log);
                }
                foreach (var log in connectStateLogs)
                {
                    _logger.Warning(log);
                    sb.AppendLine(log);
                }
            }

            var loginEmbed = new EmbedBuilder()
                .WithTitle("重新连接通知")
                .WithAuthor(_client.CurrentUser)
                .WithDescription(sb.ToString())
                .Build();

            // 向维护者发送私信通知
            try
            {
                var maintainer = await _client.GetUserAsync(maintainerId);
                if (maintainer is not null)
                {
                    await maintainer.SendMessageAsync(embed: loginEmbed);
                    _logger.Info("已向维护者{MaintainerName}({MaintainerId})发送重连通知私信", maintainer.GlobalName, maintainerId);
                }
                else
                {
                    _logger.Warning("无法获取维护者用户({MaintainerId}), 重连通知私信未发送", maintainerId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "向维护者({MaintainerId})发送重连通知私信失败", maintainerId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ReConnect 处理失败");
        }
    }

    public async Task SendLoginNotificationAsync(ulong statusChannelId, List<ulong>? DeployGuildList = null, string? appendDescription = null)
    {
        var statusChannel = await _client.GetChannelAsync(statusChannelId);
        if (statusChannel is IMessageChannel statusMessageChannel)
        {
            var loginLogStatusSB = new StringBuilder();
            loginLogStatusSB.AppendLine($"""
                ### 服务已启动
                登录 BOT : {_client.CurrentUser.Username}({_client.CurrentUser.Id})
                """);

            if (DeployGuildList is not null)
            {
                var guildInfoList = new List<(IGuild guild, ulong id)>();
                var invalidConfig = new List<ulong>();
                foreach (var id in DeployGuildList)
                {
                    var guild = _client.GetGuild(id);

                    if (guild != null)
                    {
                        guildInfoList.Add((guild, id));
                    }
                    else
                    {
                        _logger.Warning($"警告: 机器人不在配置的服务器 {id} 中，已跳过。");
                        invalidConfig.Add(id);
                    }
                }
                loginLogStatusSB.AppendLine("部署服务器:");
                foreach (var guildInfo in guildInfoList)
                {
                    loginLogStatusSB.AppendLine($"""
                        > - {guildInfo.guild.Name}
                        > -# ID: {guildInfo.guild.Id}
                        """);
                }
                if (invalidConfig.Count > 0)
                {
                    loginLogStatusSB.AppendLine("发现以下无效配置, 当前所登录 BOT 并不处于这些服务器中");
                    foreach (var guildInfo in guildInfoList)
                    {
                        loginLogStatusSB.AppendLine($"> - ID: {guildInfo.guild.Id}: {guildInfo.id}");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(appendDescription))
            {
                loginLogStatusSB.AppendLine(appendDescription);
            }

            var embed = new EmbedBuilder()
                .WithTitle("Bot 上线通知")
                .WithDescription(loginLogStatusSB.ToString())
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await statusMessageChannel.SendMessageAsync(embed: embed);
            _logger.Info("成功发送登录消息通知");
        }
        else
        {
            _logger.Warning("ReadyAsync 出现异常: 配置的 Status 频道({channelId})不是可发消息频道类型(IMessageChannel)", statusChannelId);
        }
    }

    public async Task SendKeyEntryMessageAsync(ulong statusChannelId, CustomId customId, string? content = null)
    {
        var statusChannel = await _client.GetChannelAsync(statusChannelId);
        if (statusChannel is IMessageChannel statusMessageChannel)
        {
            var message = await statusMessageChannel.SendMessageAsync(
                components: MessagePrefab.EntryKey(customId, content));

            _logger.Info("已发送密钥输入提示: ChannelId={ChannelId}, MessageId={MessageId}, CustomId={CustomId}",
                statusChannelId, message.Id, customId);
        }
        else
        {
            _logger.Warning("无法发送密钥输入提示: 频道({ChannelId})不存在或不是可发消息频道类型(IMessageChannel)",
                statusChannelId);
        }
    }

    public async Task SendAuditMessageToMaintainer(ulong maintainerId, string message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Bot 审计通知")
            .WithDescription(message)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }
    
    public async Task SendAuditMessageToChannel(ulong channelId, string message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Bot 审计通知")
            .WithDescription(message)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }
}