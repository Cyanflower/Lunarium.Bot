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

namespace Lunarium.Bot.Framework;

public sealed class CommandSynchronizer
{
    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly HashSet<string> _reRegisterCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "重新注册",
        "re-registration",
        "reregister"
    };

    public CommandSynchronizer(ILogger logger, DiscordSocketClient client)
    {
        _logger = logger;
        _client = client;
    }

    public async Task SyncCommandsAsync(SocketMessage message, InteractionService interactions, ulong maintainerId, List<ulong> DeployGuildList)
    {
        // 过滤掉不关心的消息
        // - 消息不是用户发的 (例如系统消息)
        // - 消息是机器人自己或其他机器人发的
        if (message is not SocketUserMessage userMessage || message.Author.IsBot)
        {
            return;
        }

        // 检查消息是否 @ 了Bot
        if (!userMessage.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
        {
            return;
        }

        // 解析出命令文本
        // 用正则表达式把 @mention 的部分去掉，然后去掉前后的空格
        var commandText = Regex.Replace(userMessage.Content, @"<@!?\d+>\s*", "").Trim();

        // 忽略大小写的匹配
        if (!_reRegisterCommands.Any(cmd => string.Equals(cmd, commandText, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // 记录操作
        _logger.Info("用户 {userMessage.Author.Username} 触发了传统命令: {commandText}", userMessage.Author.Username, commandText);

        // 鉴权
        if (userMessage.Author.Id != maintainerId)
        {
            _logger.Warning("鉴权失败: 用户 {userMessage.Author.Username} 尝试执行管理员命令失败。", userMessage.Author.Username);
            await userMessage.ReplyAsync("权限不足: 该命令为系统维护命令");
            return;
        }

        _logger.Info("鉴权成功: 用户 {userMessage.Author.Username} 正在执行命令。", userMessage.Author.Username);

        await userMessage.ReplyAsync("鉴权成功: 正在刷新所有斜杠命令... 请稍等片刻");

        try
        {
            var sendMessage = new StringBuilder();
            // 清除全局命令
            _logger.Info("正在清除全局命令...");
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[0]);
            _logger.Info("全局命令已清除。");
            sendMessage.Append("- 全局命令已清除。");

            // 清除各个服务器中的命令
            foreach (var guildId in DeployGuildList)
            {
                try
                {
                    _logger.Info("正在清除服务器 {guildId} 的命令...", userMessage.Author.Username);
                    var guild = _client.GetGuild(guildId);
                    if (guild != null)
                    {
                        // 使用空数组覆盖服务器命令
                        await guild.BulkOverwriteApplicationCommandAsync(new ApplicationCommandProperties[0]);
                        _logger.Info("服务器 {GuildId} 的命令已清除。", userMessage.Author.Username);
                        sendMessage.Append($"- 服务器 {guildId} 的命令已清除。");
                    }
                    else
                    {
                        _logger.Warning("无法找到服务器 {GuildId}", guildId);
                        sendMessage.Append($"- 无法找到服务器 {guildId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "清除服务器 {GuildId} 命令时发生错误", guildId);
                    sendMessage.Append($"- 清除服务器 {guildId} 命令时发生错误\n```{ex.ToString().Slice(150, "...")}```");
                    // 继续处理其他服务器
                }
            }

            // 重新同步
            try
            {
                _logger.Info("正在同步命令到部署服务器列表...");
                _logger.Info("同步命令列表:");
                foreach (var command in interactions.SlashCommands)
                {
                    _logger.Info("  - {command.Name}: {command.Description}",command.Name, command.Description);
                }
                _logger.Info("命令总数: {_interactions.SlashCommands.Count}", interactions.SlashCommands.Count);

                // 同步命令到部署服务器列表
                foreach (var guildId in DeployGuildList)
                {
                    var guild = _client.GetGuild(guildId);
                    if (guild != null)
                    {
                        _logger.Info("正在同步命令到服务器: {GuildId} - {GuildName}", guildId, guild.Name);
                        await interactions.RegisterCommandsToGuildAsync(guildId, true);
                        _logger.Info("服务器 {guild.Name} 命令同步完成。", guild.Name);
                    }
                    else
                    {
                        _logger.Warning("⚠️ 无法找到服务器 {GuildId}，Bot可能不在该服务器中或服务器ID不正确", guildId);
                    }
                }
                _logger.Info("✅ 命令同步完成！");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 同步命令时发生错误");
            }

            await userMessage.Channel.SendMessageAsync($"✅ 操作完成！\n{sendMessage.ToString()}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "执行重新注册命令时发生错误。");
            await userMessage.Channel.SendMessageAsync("执行重新注册命令时发生错误。");
        }
    }
}