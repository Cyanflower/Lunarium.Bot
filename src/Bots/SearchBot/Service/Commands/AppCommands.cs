using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

using SearchBot.Database;
using SearchBot.ThreadFetcher;
using SearchBot.Utils;
using LunariumExtensionMethod;
using SearchBot.Model;

namespace SearchBot.AppInteractionService;

public class AppCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<AppCommands> _logger;
    private readonly Scanner _scanner;
    private readonly DatabaseService _databaseService;
    private readonly Authentication _authentication;
    private readonly ConfigLoader _configloader;
    private readonly HashSet<string> _scanCategories;
    private readonly HashSet<ulong> _allScanChannelIds;

    public AppCommands(ILogger<AppCommands> logger, Scanner scanner, DatabaseService databaseService, Authentication authentication, ConfigLoader configloader)
    {
        _logger = logger;
        _scanner = scanner;
        _databaseService = databaseService;
        _authentication = authentication;
        _configloader = configloader;
        _scanCategories = _configloader.ScanCategories;
        _allScanChannelIds = _configloader.AllScanChannelIds;
    }

    private Embed AuthEmbed(string description)
    {
        return new EmbedBuilder()
            .WithTitle("权限不足")
            .WithDescription(description)
            .WithColor(Color.Red)
            .Build();
    }

    private Embed SuccessEmbed(string description, string title = "成功")
    {
        return new EmbedBuilder()
            .WithTitle("成功")
            .WithDescription(description)
            .WithColor(Color.Green)
            .Build();
    }

    private Embed FailedEmbed(string description, string reason = "")
    {
        return new EmbedBuilder()
            .WithTitle("失败")
            .WithDescription(description + "\n原因:\n```" + reason.Slice(2000) + "\n```")
            .WithColor(Color.Red)
            .Build();
    }

    private async Task<bool> IsAdmin(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!_authentication.InGuild(context.Guild.Id))
        {
            await RespondAsync(embed: AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否具有任一管理员身份 ---
        // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
        var guildUser = context.User as IGuildUser;
        if (guildUser == null || !_authentication.IsAdmin(guildUser.RoleIds))
        {
            await RespondAsync(embed: AuthEmbed("该命令只能由管理员使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 非管理员角色 用户 {Username} 执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        _logger.LogInformation("鉴权通过: 用户 {Username} 执行了[/{command}]命令", context.User.Username, command);
        return true;
    }

    private async Task<bool> InScanChannelThread(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!_authentication.InGuild(context.Guild.Id))
        {
            await RespondAsync(embed: AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否在论坛频道的帖子中 ---
        // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
        if (Context.Channel is not IThreadChannel threadChannel)
        {
            await RespondAsync(embed: AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非帖子中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否在扫描的分区内 ---
        if (!_allScanChannelIds.Contains(threadChannel.CategoryId ?? 0))
        {
            var description = "该命令只能在以下分区内使用:\n" + string.Join("\n",_scanCategories.Select( s => $"> - {s}"));
            await RespondAsync(embed: AuthEmbed(description), ephemeral: true);
            _logger.LogInformation("鉴权失败: 用户 {Username} 在分区外中执行了[/{command}]命令",context.User.Username, command);
            return false;
        }
        _logger.LogInformation("鉴权通过: 用户 {User} 订阅了帖子 {ThreadName}({ThreadId})", Context.User.Username, threadChannel.Name, threadChannel.Id);
        
        return true;
    }

    private async Task<bool> InGuildThread(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!_authentication.InGuild(context.Guild.Id))
        {
            await RespondAsync(embed: AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否在论坛频道的帖子中 ---
        if (Context.Channel is not IThreadChannel threadChannel)
        {
            await RespondAsync(embed: AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非帖子中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        if (await threadChannel.GetCategoryAsync(CacheMode.AllowDownload) is not IForumChannel)
        {
            await RespondAsync(embed: AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在文字频道的子区中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        _logger.LogInformation("鉴权通过: 用户 {Username} 执行了[/{command}]命令", context.User.Username, command);
        return true;
    }

    [SlashCommand("ping", "回复Pong消息")]
    public async Task PingAsync()
    { 
        await DeferAsync(ephemeral: true);
        if (await IsAdmin("ping", Context))
        {
            // === 命令逻辑 ===
            // 获取延迟
            var websocketLatency = Context.Client.Latency;
            // 回复
            await RespondAsync(embed: SuccessEmbed($"Pong! 🏓\nWebSocket延迟: {websocketLatency}ms"), ephemeral: true);
        }
    }

    [SlashCommand("全扫描", "执行全扫描")]
    public async Task FullSyncScanAsync()
    {
        await DeferAsync(ephemeral: true);
        if (await IsAdmin("全扫描", Context))
        {
            // === 命令逻辑 ===
            try
            {
                // 执行扫描
                var scanResult = _scanner.FullSync();
                var description =
                    "## 扫描:\n" +
                    $"总帖子数量: {scanResult.ScanAllResult}\n" +
                    "----------------\n" +
                    "并发处理帖子数据结果:\n" +
                    $"> 成功: {scanResult.ThreadProcessResult.SuccessCount}\n" +
                    $"> 失败: {scanResult.ThreadProcessResult.FailedCount}\n" +
                    $"> 跳过: {scanResult.ThreadProcessResult.SkippedCount}\n" +
                    $"> 总计: {scanResult.ThreadProcessResult.ThreadsCount}\n" +
                    $"> 总耗时: {scanResult.ThreadProcessResult.TotalElapsed:F2}秒\n" +
                    $"> 平均速度: {scanResult.ThreadProcessResult.OverallAvgSpeed:F2} 任务/秒\n" +
                    "----------------\n" +
                    "数据库:\n" +
                    $"数据库写入结果:\n" +
                    $"> 帖子主表写入行数: {scanResult.InsertPostResult.MainAffectedRows}\n" +
                    $"> FTS表写入行数: {scanResult.InsertPostResult.FtsAffectedRows}\n" +
                    $"> 帖子标签关系表写入行数: {scanResult.InsertPostResult.TagJunctionAffectedRows}\n" +
                    $"数据库失效帖子数量: {scanResult.ScanObsoleteResult}\n" +
                    $"数据库成功移除行数: {scanResult.ScanRemoveResult}";
                // 回复
                await FollowupAsync(embed: SuccessEmbed(description, "扫描更新成功"), ephemeral: true);
            }
            catch (Exception ex)
            {
                await FollowupAsync(embed: FailedEmbed("扫描失败", ex.ToString()));
                _logger.LogError("扫描失败, 原因:{ex}", ex);
            }
        }
    }

    [SlashCommand("订阅", "订阅该帖子内的作品更新(PNG附件/JSON附件)")]
    public async Task SubscribeAsync()
    {
        
        await DeferAsync(ephemeral: true);
        if (await InScanChannelThread("订阅", Context))
        {
            // === 命令逻辑 ===
            // 转换对象
            if (Context.Channel is IThreadChannel threadChannel)
            {
                // 检查是否在用户的订阅列表
                var queryResult = await _databaseService.QueryUserSubscriptionsExistAsync(Context.User.Id, threadChannel.Id);
                if (!queryResult)
                {
                    // 订阅
                    await _databaseService.InsertUserSubscriptionPostAsync(Context.User, threadChannel!);
                    // 回复
                    await FollowupAsync(embed: SuccessEmbed("订阅成功"), ephemeral: true);
                }
                else
                {
                    await FollowupAsync(embed: FailedEmbed("订阅失败", "你已经订阅过该帖子了"), ephemeral: true);
                }
            }
            // 理论上无法执行到这一步, 如果这里会进入 else, 那么鉴权的 if 本身就不会通过
            else
            {
                await FollowupAsync(embed: FailedEmbed("未知错误", "threadChannel.Id is null"), ephemeral: true);
            }
        }
    }

    [SlashCommand("取消订阅", "取消订阅该帖子内的作品更新(PNG附件/JSON附件)")]
    public async Task UnsubscribeAsync()
    {
        await DeferAsync(ephemeral: true);
        if (await InScanChannelThread("取消订阅", Context))
        {
            // === 命令逻辑 ===
            // 转换对象
            if (Context.Channel is IThreadChannel threadChannel)
            {
                // 检查是否在用户的订阅列表
                var queryResult = await _databaseService.QueryUserSubscriptionsExistAsync(Context.User.Id, threadChannel.Id);
                // 如果订阅存在
                if (queryResult)
                {
                    // 取消订阅
                    await _databaseService.RemoveSubscriptionAsync(Context.User.Id, threadChannel.Id);
                    await FollowupAsync(embed: SuccessEmbed("取消订阅成功"), ephemeral: true);
                }
                else
                {
                    await FollowupAsync(embed: FailedEmbed("取消订阅失败", "你并未订阅该帖子"), ephemeral: true);
                }
            }
            // 理论上无法执行到这一步, 如果这里会进入 else, 那么鉴权的 if 本身就不会通过
            else
            {
                await FollowupAsync(embed: FailedEmbed("未知错误", "threadChannel.Id is null"), ephemeral: true);
            }
        }
    }

    [SlashCommand("收藏", "收藏该帖子")]
    public async Task FavoriteAsync(
        [Summary("类别", "将帖子收藏至对应类别, 默认为不添加类别分类")] 
        [Autocomplete(typeof(FavoriteClassAutoCompletion))]
        string? favorite = null
    )
    {
        await DeferAsync(ephemeral: true);
        if (await InGuildThread("收藏", Context))
        {
            // === 命令逻辑 ===
            // 转换对象
            if (Context.Channel is IThreadChannel threadChannel)
            {
                // 如果指定了收藏类别
                if (favorite != null)
                {
                    // 查询用户已有收藏类别
                    HashSet<string>? favoriteType = await _databaseService.GetUserFavoriteTypeAsync(Context.User.Id);
                    // 如果是已有的收藏类别
                    if (favoriteType.Contains(favorite))
                    {
                        await _databaseService.InsertUserFavoritePostAsync(Context.User.Id, threadChannel, favorite);
                    }

                    // 如果指定了新的收藏类别
                    else
                    {
                        // 收藏列别是否到了上限
                        if (favoriteType.Count >= 25)
                        {
                            var embed = new EmbedBuilder()
                                .WithTitle("收藏失败")
                                .WithDescription("无法自动创建新的收藏类别, 已有的收藏类别已到达数量上限, 请考虑收藏至已有的收藏夹类别")
                                .WithFooter("收藏类别最大为25组类别, 到达上限时将无法创建新类别")
                                .WithColor(Color.Red)
                                .Build();
                            await FollowupAsync(embed: embed, ephemeral: true);
                        }
                        // 如果没有, 自动创建新类别
                        else
                        {
                            var buttonComponent = new ComponentBuilder()
                                .WithButton($"创建{favorite}收藏类别", $"create_{Context.User.Id}_{favorite}", ButtonStyle.Primary)
                                .WithButton("取消", "general_cancel", ButtonStyle.Danger)
                                .Build();
                            var desc = string.Join("\n", favoriteType.Select(s => $"> - {s}"));
                            var confirmEmbed = new EmbedBuilder()
                                .WithTitle($"新建类别: 类别 {favorite} 不存在")
                                .WithDescription($"你的收藏中目前存在的类别({favoriteType.Count}/25):\n" + desc + $"\n点击确认将自动创建名为 {favorite} 的收藏夹并将该帖子加入该类别的收藏")
                                .WithFooter("收藏类别最大为25组类别, 到达上限时将无法创建新类别")
                                .WithColor(Color.Blue)
                                .Build();
                            await FollowupAsync(embed: confirmEmbed, components: buttonComponent, ephemeral: true);
                        }
                    }
                }
                // 直接添加到未分类
                else
                {
                    await _databaseService.InsertUserFavoritePostAsync(Context.User.Id, threadChannel);
                    await FollowupAsync(embed: SuccessEmbed("收藏成功"), ephemeral: true);
                }
            }
        }
    }

    [SlashCommand("取消收藏", "取消收藏该帖子")]
    public async Task UnfavoriteAsync()
    {
        await DeferAsync(ephemeral: true);
        if (await InGuildThread("收藏", Context))
        {
            if (Context.Channel is IThreadChannel threadChannel)
            {
                var queryResult = _databaseService.QueryUserFavoriteExistAsync(Context.User.Id, threadChannel.Id);

                

                await _databaseService.RemoveFavoriteAsync(Context.User.Id, threadChannel.Id);

                await FollowupAsync(embed: SuccessEmbed("取消收藏成功"), ephemeral: true);
            }

        }
    }

    // [SlashCommand("搜索", "搜索论坛帖子")]
    // public async Task SearchAsync(
        
    // )
    // {
        
    // }
}