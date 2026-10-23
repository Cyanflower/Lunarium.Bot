using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Logging;

namespace Lunarium.Bot.Framework;

/// <summary>
/// 处理通过 @Bot 触发的文本维护命令：
/// <list type="bullet">
///   <item><c>重新注册</c>：清空全局与各部署服务器的命令后，重新注册到所有部署服务器。</item>
///   <item><c>在本服务器重新注册</c>：仅重新注册触发消息所在服务器的命令。</item>
/// </list>
/// 两条命令均仅限维护者使用。
/// </summary>
public sealed partial class CommandSynchronizer
{
    private enum TextCommand
    {
        /// <summary>清空并重新注册所有部署服务器的命令。</summary>
        ReRegisterAll,

        /// <summary>仅重新注册当前服务器的命令。</summary>
        ReRegisterHere
    }

    /// <summary>触发词到命令的映射；比较器已忽略大小写。</summary>
    private static readonly FrozenDictionary<string, TextCommand> _commandMap =
        new Dictionary<string, TextCommand>(StringComparer.OrdinalIgnoreCase)
        {
            ["重新注册"] = TextCommand.ReRegisterAll,
            ["re-registration"] = TextCommand.ReRegisterAll,
            ["reregister"] = TextCommand.ReRegisterAll,
            ["在本服务器重新注册"] = TextCommand.ReRegisterHere,
            ["reregister-here"] = TextCommand.ReRegisterHere
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger _logger;
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;

    public CommandSynchronizer(ILogger logger, DiscordSocketClient client, InteractionService interactions)
    {
        _logger = logger.ForContext("CommandSynchronizer");
        _client = client;
        _interactions = interactions;
    }

    /// <summary>匹配消息中的 @Bot 提及片段。</summary>
    [GeneratedRegex(@"<@!?\d+>\s*")]
    private static partial Regex MentionRegex();

    /// <summary>
    /// 处理一条文本消息；若不是发给 Bot 的维护命令则直接忽略。
    /// </summary>
    /// <param name="message">收到的消息。</param>
    /// <param name="maintainerId">维护者用户 ID。</param>
    /// <param name="deployGuildIds">部署服务器 ID 列表（仅全量重新注册使用）。</param>
    public async Task HandleMessageAsync(
        SocketMessage message,
        ulong maintainerId,
        IReadOnlyCollection<ulong> deployGuildIds)
    {
        if (!TryParseCommand(message, out var command))
        {
            return;
        }

        var userMessage = (SocketUserMessage)message;
        _logger.Info("用户 {Username} 触发了传统命令: {Command}", userMessage.Author.Username, command);

        switch (command)
        {
            case TextCommand.ReRegisterAll:
                await ReRegisterAllAsync(userMessage, maintainerId, deployGuildIds);
                break;

            case TextCommand.ReRegisterHere:
                await ReRegisterHereAsync(userMessage, maintainerId);
                break;
        }
    }

    /// <summary>
    ///     把当前已加载的斜杠命令注册到所有部署服务器（<c>deleteMissing: true</c>）。
    /// </summary>
    /// <remarks>
    ///     供 Bot 首次登录完成时调用。逐服务器独立处理：某个服务器失败只记录日志，不影响其它服务器。
    /// </remarks>
    /// <param name="deployGuildIds">部署服务器 ID 列表。</param>
    public async Task SyncCommandsToDeployGuildsAsync(IReadOnlyCollection<ulong> deployGuildIds)
    {
        _logger.Info("正在同步命令到部署服务器列表...");
        foreach (var command in _interactions.SlashCommands)
        {
            _logger.Info("  - {Name}: {Description}", command.Name, command.Description);
        }
        _logger.Info("命令总数: {Count}", _interactions.SlashCommands.Count);

        foreach (var guildId in deployGuildIds)
        {
            var guild = _client.GetGuild(guildId);
            if (guild is null)
            {
                _logger.Warning("⚠️ 无法找到服务器 {GuildId}，Bot可能不在该服务器中或服务器ID不正确", guildId);
                continue;
            }

            try
            {
                _logger.Info("正在同步命令到服务器: {GuildId} - {GuildName}", guildId, guild.Name);
                await _interactions.RegisterCommandsToGuildAsync(guildId, true);
                _logger.Info("服务器 {GuildName} 命令同步完成。", guild.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 服务器 {GuildId} 命令同步失败", guildId);
            }
        }

        _logger.Info("✅ 命令同步完成！");
    }

    private bool TryParseCommand(SocketMessage message, out TextCommand command)
    {
        command = default;

        // 过滤掉不关心的消息：非用户消息、机器人消息
        if (message is not SocketUserMessage userMessage || message.Author.IsBot)
        {
            return false;
        }

        // 必须 @ 了 Bot
        if (!userMessage.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id))
        {
            return false;
        }

        // 去掉 @mention 后按忽略大小写匹配触发词
        var commandText = MentionRegex().Replace(userMessage.Content, "").Trim();
        return _commandMap.TryGetValue(commandText, out command);
    }

    /// <summary>鉴权：通过返回 <see langword="true"/>；失败时已回复用户并返回 <see langword="false"/>。</summary>
    private async Task<bool> AuthorizeAsync(SocketUserMessage userMessage, params IMsgBasedAuthRule[] rules)
    {
        var failReason = await RuleEngine.ValidateAsync(userMessage, rules);
        if (failReason is null)
        {
            return true;
        }

        _logger.Warning("鉴权失败: 用户 {Username} 尝试执行维护命令失败。", userMessage.Author.Username);
        await userMessage.ReplyAsync(components: failReason);
        return false;
    }

    private async Task ReRegisterAllAsync(
        SocketUserMessage userMessage,
        ulong maintainerId,
        IReadOnlyCollection<ulong> deployGuildIds)
    {
        if (!await AuthorizeAsync(userMessage, new BaseRuleSet.RequireMaintainer(new HashSet<ulong> { maintainerId })))
        {
            return;
        }

        _logger.Info("鉴权成功: 用户 {Username} 正在执行命令。", userMessage.Author.Username);
        await userMessage.ReplyAsync("鉴权成功: 正在刷新所有斜杠命令... 请稍等片刻");

        try
        {
            var report = new StringBuilder();

            // 清除全局命令
            _logger.Info("正在清除全局命令...");
            await _client.BulkOverwriteGlobalApplicationCommandsAsync([]);
            _logger.Info("全局命令已清除。");
            report.AppendLine("- 全局命令已清除。");

            // 清除各个服务器中的命令
            foreach (var guildId in deployGuildIds)
            {
                await ClearGuildCommandsAsync(guildId, report);
            }

            // 重新同步到部署服务器
            await SyncCommandsToDeployGuildsAsync(deployGuildIds);

            await userMessage.Channel.SendMessageAsync($"✅ 操作完成！\n{report}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "执行重新注册命令时发生错误。");
            await userMessage.Channel.SendMessageAsync("执行重新注册命令时发生错误。");
        }
    }

    private async Task ClearGuildCommandsAsync(ulong guildId, StringBuilder report)
    {
        try
        {
            _logger.Info("正在清除服务器 {GuildId} 的命令...", guildId);
            var guild = _client.GetGuild(guildId);
            if (guild is null)
            {
                _logger.Warning("无法找到服务器 {GuildId}", guildId);
                report.AppendLine($"- 无法找到服务器 {guildId}");
                return;
            }

            // 使用空数组覆盖服务器命令
            await guild.BulkOverwriteApplicationCommandAsync([]);
            _logger.Info("服务器 {GuildName}({GuildId}) 的命令已清除。", guild.Name, guildId);
            report.AppendLine($"- 服务器 {guildId} 的命令已清除。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "清除服务器 {GuildId} 命令时发生错误", guildId);
            report.AppendLine($"- 清除服务器 {guildId} 命令时发生错误\n```{ex.ToString().Slice(150, "...")}```");
            // 继续处理其他服务器
        }
    }

    private async Task ReRegisterHereAsync(SocketUserMessage userMessage, ulong maintainerId)
    {
        if (!await AuthorizeAsync(
                userMessage,
                new BaseRuleSet.RequireInAnyGuild(),
                new BaseRuleSet.RequireMaintainer(new HashSet<ulong> { maintainerId })))
        {
            return;
        }

        // RequireInAnyGuild 已保证消息来自服务器频道，此处仅为防御
        if (userMessage.Channel is not IGuildChannel guildChannel)
        {
            return;
        }

        _logger.Info(
            "鉴权成功: 用户 {Username} 正在重新注册服务器 {GuildId} 的命令。",
            userMessage.Author.Username,
            guildChannel.GuildId);
        await userMessage.ReplyAsync("鉴权成功: 正在重新注册本服务器的斜杠命令... 请稍等片刻");

        try
        {
            // deleteMissing: true 会在一次 BulkOverwrite 中整体覆盖，无需先清空
            await _interactions.RegisterCommandsToGuildAsync(guildChannel.GuildId, true);
            _logger.Info(
                "服务器 {GuildName}({GuildId}) 的命令已重新注册。",
                guildChannel.Guild.Name,
                guildChannel.GuildId);
            await userMessage.Channel.SendMessageAsync("✅ 本服务器的斜杠命令已重新注册。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "重新注册服务器 {GuildId} 的命令时发生错误", guildChannel.GuildId);
            await userMessage.Channel.SendMessageAsync("❌ 重新注册本服务器的斜杠命令时发生错误。");
        }
    }
}