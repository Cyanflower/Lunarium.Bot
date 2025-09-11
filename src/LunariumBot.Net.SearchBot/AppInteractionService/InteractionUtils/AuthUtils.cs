// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public class AuthUtils : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ConfigLoader _configLoader;
    private readonly ILogger _logger;
    private readonly HashSet<string> _scanCategories;
    private readonly HashSet<ulong> _allScanChannelIds;
    public AuthUtils(ILogger logger, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("CommandAuth");
        _configLoader = configLoader;
        _scanCategories = _configLoader.Config.GuildsConfig.ScanChannelIDS.Keys.ToHashSet();
        _allScanChannelIds = _configLoader.Config.GuildsConfig.ScanChannelIDS.Values
            .SelectMany(ids => ids)
            .ToHashSet();
    }

    public bool IsAdmin(IEnumerable<ulong> userRoleIds)
    {
        return userRoleIds.Any(_configLoader.Config.AuthConfig.AdminRoleIds.Contains);
    }

    public bool InGuild(ulong guildId)
    {
        return _configLoader.Config.GuildsConfig.DeployGuildIds.Contains(guildId);
    }

    public async Task<bool> IsAdmin(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!InGuild(context.Guild.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在非授权服务器中执行了[/{command}]命令");
            return false;
        }
        // --- 是否具有任一管理员身份 ---
        // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
        var guildUser = context.User as IGuildUser;
        if (guildUser == null || !IsAdmin(guildUser.RoleIds))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能由管理员使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 非管理员角色 用户 {context.User.Username} 执行了[/{command}]命令");
            return false;
        }
        _logger.Info($"鉴权通过: 用户 {context.User.Username} 执行了[/{command}]命令");
        return true;
    }

    public async Task<bool> InScanChannelThread(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!InGuild(context.Guild.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在非授权服务器中执行了[/{command}]命令");
            return false;
        }
        // --- 是否在论坛频道的帖子中 ---
        // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
        if (context.Channel is not IThreadChannel threadChannel)
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在非帖子中执行了[/{command}]命令");
            return false;
        }
        // --- 是否在扫描的分区内 ---
        if (threadChannel.CategoryId is ulong categoryId)
        {
            if (!_allScanChannelIds.Contains(categoryId))
            {
                var description = "该命令只能在以下分区内使用:\n" + string.Join("\n", _scanCategories.Select(s => $"> - {s}"));
                await FollowupAsync(embed: EmbedUtils.AuthEmbed(description), ephemeral: true);
                _logger.Info($"鉴权失败: 用户 {context.User.Username} 在分区外中执行了[/{command}]命令");
                return false;
            }
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("出现网络错误, 请重试"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在执行了[/{command}]命令, 未能成功获取父频道信息");
            return false;
        }
        _logger.Info($"鉴权通过: 用户 {context.User.Username} 订阅了帖子 {threadChannel.Name}({threadChannel.Id})");

        return true;
    }

    public async Task<bool> InGuildThread(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!InGuild(context.Guild.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在非授权服务器中执行了[/{command}]命令");
            return false;
        }
        _logger.Debug($"context.Interaction.Channel: {context.Channel}", context.Channel.Name);
        _logger.Debug($"context.Channel: {context.Channel}", context.Channel.Name);
        // --- 是否在论坛频道的帖子中 ---
        if (context.Channel is not IThreadChannel threadChannel)
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在非帖子中执行了[/{command}]命令");
            return false;
        }
        if (threadChannel.CategoryId != null)
        {
            var parentChannel = await threadChannel.Guild.GetChannelAsync(threadChannel.CategoryId.Value, CacheMode.AllowDownload);
            if (parentChannel is not IForumChannel)
            {
                await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
                _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在文字频道的子区中执行了[/{command}]命令");
                return false;
            }
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("出现网络错误, 请重试"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在执行了[/{command}]命令, 未能成功获取父频道信息");
            return false;
        }

        _logger.Info($"鉴权通过: 用户 {context.User.Username} 执行了[/{command}]命令");
        return true;
    }
    public async Task<bool> InGuild(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!InGuild(context.Guild.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.Warning($"鉴权失败: 用户 {context.User.Username} 在非授权服务器中执行了[/{command}]命令");
            return false;
        }
        _logger.Info($"鉴权通过: 用户 {context.User.Username} 执行了[/{command}]命令");
        return true;
    }
}