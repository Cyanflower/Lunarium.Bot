// NuGet 包依赖
using Discord.Interactions;
using Discord;

using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.Archive.Model;
using LunariumBot.Net.Archive.Utils;
using LunariumBot.Net.Archive.Archiver;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.Archive.AppInteractionService;

[Group("admin", "管理员专属配置命令")]
public sealed class AdminCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ConfigLoader _configLoader;
    private readonly AuthUtils _authUtils;
    private readonly ArchiverService _archiverService;
    private readonly ILogger _logger;

    public AdminCommands(ILogger logger, ConfigLoader configLoader, AuthUtils authUtils, ArchiverService archiverService)
    {
        _configLoader = configLoader;
        _authUtils = authUtils;
        _archiverService = archiverService;
        _logger = logger.ForContext("AdminCommands");
    }

    public override async Task BeforeExecuteAsync(ICommandInfo command)
    {
        // override 父类方法, 在响应前统一执行DeferAsync
        await DeferAsync(ephemeral: true);
    }

    [SlashCommand("add-guild", "添加一个新的服务器配置")]
    public async Task AddGuildConfig(
        [Summary("guild-id", "要添加的服务器ID")] string guildIdStr,
        [Summary("limit", "该服务器的活跃线程上限 (900-999)")] int limit)
    {
        // 权限检查
        if (!await _authUtils.IsAdmin("admin add-guild", Context)) return;

        if (!ulong.TryParse(guildIdStr, out var guildId))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("服务器ID格式不正确，请输入纯数字。"));
            return;
        }

        // 验证机器人是否在该服务器中
        var targetGuild = Context.Client.GetGuild(guildId);
        if (targetGuild == null)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed($"无法添加服务器 `{guildId}`。", $"原因：机器人不是该服务器的成员，请先将机器人邀请到目标服务器中。"));
            return;
        }

        if (limit < 900 || limit >= 1000)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed($"当前值不合法: {limit}。", $"原因：允许范围为 900 - 999"));
            return;
        }

        bool success = _configLoader.UpdateConfig(config =>
        {
            if (config.DeployGuildConfig.ContainsKey(guildId))
            {
                // 使用异常来中断UpdateConfig的流程
                throw new InvalidOperationException($"服务器 {guildId} 的配置已存在。");
            }
            config.DeployGuildConfig[guildId] = limit;
        });

        if (success)
        {
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"已成功添加服务器 `{guildId}` 的配置，活跃线程上限为 `{limit}`。"));
        }
        else
        {
            // UpdateConfig内部已经记录了详细错误，这里只给用户一个通用提示
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("添加服务器配置失败，可能是因为提供的参数不合法或服务器已存在。请检查控制台日志获取详细信息。"));
        }
    }

    [SlashCommand("set-limit", "修改当前服务器的活跃线程上限")]
    public async Task SetGuildLimit([Summary("limit", "新的活跃线程上限 (900-999)")] int limit)
    {
        // 权限检查
        if (!await _authUtils.IsAdmin("admin set-limit", Context)) return;

        var guildId = Context.Guild.Id;

        if (limit < 900 || limit >= 1000)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed($"当前值不合法: {limit}。", $"原因：允许范围为 900 - 999"));
            return;
        }

        bool success = _configLoader.UpdateConfig(config =>
        {
            if (!config.DeployGuildConfig.ContainsKey(guildId))
            {
                throw new InvalidOperationException($"当前服务器 `{guildId}` 不在配置列表中。请先使用 `add-guild` 添加。");
            }
            config.DeployGuildConfig[guildId] = limit;
        });

        if (success)
        {
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"当前服务器的活跃线程上限已成功修改为 `{limit}`。"));
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("修改配置失败，可能是因为提供的上限值不合法。请检查控制台日志。"));
        }
    }

    [SlashCommand("set-interval", "修改归档任务的执行间隔（分钟）")]
    public async Task SetInterval([Summary("minutes", "新的执行间隔（分钟，必须大于0）")] int minutes)
    {
        // 权限检查
        if (!await _authUtils.IsAdmin("admin set-interval", Context)) return;

        bool success = _configLoader.UpdateConfig(config =>
        {
            config.IntervalMin = minutes;
        });

        if (success)
        {
            // 配置更新成功后，手动触发定时器间隔的更新
            _archiverService.UpdateTimerInterval();
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"归档任务的执行间隔已成功修改为 `{minutes}` 分钟，定时器已更新。"));
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("修改间隔失败，可能是因为提供的值不合法。请检查控制台日志。"));
        }
    }


    [SlashCommand("reload-config", "从 appsettings.jsonc 文件重新加载配置")]
    public async Task ReloadConfig()
    {
        // 权限检查
        if (!await _authUtils.IsAdmin("admin reload-config", Context)) return;

        // ReloadConfig 内部处理了成功与失败的情况
        _configLoader.ReloadConfig();
        // 无论如何都尝试更新一次定时器，以防万一文件中的间隔被改变
        _archiverService.UpdateTimerInterval();

        await FollowupAsync(embed: EmbedUtils.SuccessEmbed("已尝试从文件重新加载配置并更新定时器。如果文件内容有误，将保留旧配置。请检查控制台日志确认结果。"));
    }

    [SlashCommand("unpin-post", "取消本帖在论坛中的置顶")]
    public async Task UnpinPost()
    {
        // 权限检查
        if (!await _authUtils.IsAdmin("admin reload-config", Context)) return;

        try
        {
            if (Context.Channel is not IThreadChannel threadChannel)
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败, 当前为非帖子频道"), ephemeral: true);
                return;
            }

            // 移除 Pinned 标志位
            await threadChannel.ModifyAsync(prop => { prop.Flags = threadChannel.Flags & ~ChannelFlags.Pinned; });

            // 从 ArchiverService 的缓存中移除该帖子
            var (cacheSuccess, cacheReason) = _archiverService.TryRemovePinnedThread(threadChannel);

            var finalMessage = $"✅ 已向 Discord 发送取消置顶请求。\n" +
                               $"🧠 缓存同步结果: **{cacheReason}**";

            await FollowupAsync(embed: EmbedUtils.SuccessEmbed(finalMessage), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UnpinPost Exception");
            await FollowupAsync(embed: EmbedUtils.FailedEmbed($"操作失败, 原因: \n{ex.ToString().Slice(1500)}"), ephemeral: true);
        }
    }
}