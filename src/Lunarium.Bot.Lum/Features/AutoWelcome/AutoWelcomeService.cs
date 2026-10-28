using Discord;
using Discord.WebSocket;

using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Lum.Features.Shared;
using Lunarium.Bot.Lum.Model;
using Lunarium.Logging;

namespace Lunarium.Bot.Lum.Features.AutoWelcome;

/// <summary>
/// 出入自动化：入群欢迎（可选自动分配身份组）与离群通知。
/// <para>
/// 配置来源为 <c>DeployGuildConfig[guildId].AutoWelcome</c>，
/// 该节点缺失即代表本服务器不启用本功能。
/// </para>
/// </summary>
public sealed class AutoWelcomeService
{
    /// <summary>Discord 品牌绿（欢迎）。</summary>
    private static readonly Color WelcomeColor = new(0x57F287);

    /// <summary>Discord 品牌红（离开）。</summary>
    private static readonly Color LeaveColor = new(0xED4245);

    /// <summary>头像请求尺寸（像素）。</summary>
    private const ushort AvatarSize = 256;

    private readonly ILogger _logger;
    private readonly ConfigLoader<LumConfig> _configLoader;

    public AutoWelcomeService(ILogger logger, ConfigLoader<LumConfig> configLoader)
    {
        _logger = logger.ForContext("AutoWelcomeService");
        _configLoader = configLoader;
    }

    /// <summary>入群：按配置分配身份组，并发送欢迎消息。</summary>
    public async Task HandleUserJoinedAsync(SocketGuildUser user)
    {
        try
        {
            // 机器人入群通常由管理操作触发，不欢迎也不分配身份组
            if (user.IsBot)
            {
                _logger.Info("跳过机器人入群: GuildId={GuildId}, UserId={UserId}, Username={Username}",
                    user.Guild.Id, user.Id, user.Username);
                return;
            }

            var config = GetConfig(user.Guild);
            if (config is null)
            {
                return;
            }

            await TryGrantRoleAsync(user, config);

            var channel = user.Guild.GetChannel(config.WelcomeChannelId) as IMessageChannel;
            if (channel is null)
            {
                _logger.Warning("欢迎频道不存在或不是消息频道: GuildId={GuildId}, ChannelId={ChannelId}",
                    user.Guild.Id, config.WelcomeChannelId);
                return;
            }

            await channel.SendMessageAsync(embed: BuildWelcomeEmbed(user));

            _logger.Info("已发送欢迎消息: GuildId={GuildId}, UserId={UserId}, ChannelId={ChannelId}",
                user.Guild.Id, user.Id, channel.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理入群事件失败: GuildId={GuildId}, UserId={UserId}", user.Guild.Id, user.Id);
        }
    }

    /// <summary>离群：按配置发送离群通知。</summary>
    public async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        try
        {
            if (user.IsBot)
            {
                _logger.Info("跳过机器人离群: GuildId={GuildId}, UserId={UserId}, Username={Username}",
                    guild.Id, user.Id, user.Username);
                return;
            }

            var config = GetConfig(guild);
            if (config is null || config.LeaveNotificationChannelId == 0)
            {
                return;
            }

            var channel = guild.GetChannel(config.LeaveNotificationChannelId) as IMessageChannel;
            if (channel is null)
            {
                _logger.Warning("离群通知频道不存在或不是消息频道: GuildId={GuildId}, ChannelId={ChannelId}",
                    guild.Id, config.LeaveNotificationChannelId);
                return;
            }

            // 成员已从缓存移除，显示名称取不到；这里只用账号级信息
            await channel.SendMessageAsync(embed: BuildLeaveEmbed(user));

            _logger.Info("已发送离群通知: GuildId={GuildId}, UserId={UserId}, ChannelId={ChannelId}",
                guild.Id, user.Id, channel.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理离群事件失败: GuildId={GuildId}, UserId={UserId}", guild.Id, user.Id);
        }
    }

    private AutoWelcomeConfig? GetConfig(SocketGuild guild)
        => _configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config)
            ? config.AutoWelcome
            : null;

    /// <summary>分配入群身份组；不满足条件时只记录日志，不影响欢迎消息。</summary>
    private async Task TryGrantRoleAsync(SocketGuildUser user, AutoWelcomeConfig config)
    {
        var roleId = config.AutoDistributeRoleId;
        if (roleId == 0)
        {
            return;
        }

        var guild = user.Guild;
        var role = guild.GetRole(roleId);

        if (!RoleGuard.CanAssign(guild, role, out var reason))
        {
            _logger.Warning("跳过入群身份组分配: GuildId={GuildId}, RoleId={RoleId}, Reason={Reason}",
                guild.Id, roleId, reason);
            return;
        }

        try
        {
            await user.AddRoleAsync(role!);

            _logger.Info("已分配入群身份组: GuildId={GuildId}, UserId={UserId}, RoleId={RoleId}",
                guild.Id, user.Id, roleId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "分配入群身份组失败: GuildId={GuildId}, UserId={UserId}, RoleId={RoleId}",
                guild.Id, user.Id, roleId);
        }
    }

    private static Embed BuildWelcomeEmbed(SocketGuildUser user)
    {
        var phrase = WelcomePhrases.PickRandom();

        var description = string.IsNullOrWhiteSpace(phrase)
            ? $"{user.Mention} 欢迎来到 **{user.Guild.Name}**！"
            : $"{user.Mention} 欢迎来到 **{user.Guild.Name}**！\n{phrase}";

        return new EmbedBuilder()
            .WithColor(WelcomeColor)
            .WithTitle("Welcome | 欢迎")
            .WithDescription(description)
            .WithThumbnailUrl(GetAvatarUrl(user))
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    private static Embed BuildLeaveEmbed(SocketUser user)
        => new EmbedBuilder()
            .WithColor(LeaveColor)
            .WithTitle("Leave | 离开")
            .WithDescription($"{user.Mention} 离开了服务器... | Left the server...")
            .WithThumbnailUrl(GetAvatarUrl(user))
            .AddField("用户名", user.Username, inline: true)
            .AddField("数字ID", user.Id.ToString(), inline: true)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

    /// <summary>优先取用户自身头像；未设置头像时退回默认头像，避免缩略图为空。</summary>
    private static string GetAvatarUrl(IUser user)
        => user.GetAvatarUrl(ImageFormat.Auto, AvatarSize) ?? user.GetDefaultAvatarUrl();
}
