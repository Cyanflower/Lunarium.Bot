using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Text.Json;

using Discord;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.ThreadFetcher;
using SearchBot.Utils;
using SearchBot.Model;
using SearchBot.Scheduler;
using LunariumExtensionMethod;

namespace SearchBot.AppInteractionService;

public partial class AppCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<AppCommands> _logger;
    private readonly Scanner _scanner;
    private readonly DatabaseService _databaseService;
    private readonly Authentication _authentication;
    private readonly ConfigLoader _configloader;
    private readonly PaginatorManager _paginatorManager;
    private readonly UserCooldown _userCooldown;
    private readonly HashSet<string> _scanCategories;
    private readonly HashSet<ulong> _allScanChannelIds;
    private readonly SearchCriteriaBuilder _criteriaBuilder;

    public AppCommands(
        ILogger<AppCommands> logger,
        Scanner scanner,
        DatabaseService databaseService,
        Authentication authentication,
        ConfigLoader configloader,
        PaginatorManager paginatorManager,
        UserCooldown userCooldown,
        SearchCriteriaBuilder criteriaBuilder
    )
    {
        _logger = logger;
        _scanner = scanner;
        _databaseService = databaseService;
        _authentication = authentication;
        _configloader = configloader;
        _paginatorManager = paginatorManager;
        _userCooldown = userCooldown;
        _criteriaBuilder = criteriaBuilder;
        _scanCategories = _configloader.ScanCategories;
        _allScanChannelIds = _configloader.AllScanChannelIds;
    }

    #region 鉴权工具
    private async Task<bool> IsAdmin(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!_authentication.InGuild(context.Guild.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否具有任一管理员身份 ---
        // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
        var guildUser = context.User as IGuildUser;
        if (guildUser == null || !_authentication.IsAdmin(guildUser.RoleIds))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能由管理员使用"), ephemeral: true);
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
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否在论坛频道的帖子中 ---
        // Discord.WebSocket.SocketUser 转为 Discord.IGuildUser
        if (Context.Interaction.Channel is not IThreadChannel threadChannel)
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非帖子中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        // --- 是否在扫描的分区内 ---
        if (threadChannel.CategoryId is ulong categoryId)
        {
            if (!_allScanChannelIds.Contains(categoryId))
            {
                var description = "该命令只能在以下分区内使用:\n" + string.Join("\n", _scanCategories.Select(s => $"> - {s}"));
                await FollowupAsync(embed: EmbedUtils.AuthEmbed(description), ephemeral: true);
                _logger.LogInformation("鉴权失败: 用户 {Username} 在分区外中执行了[/{command}]命令", context.User.Username, command);
                return false;
            }
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("出现网络错误, 请重试"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在执行了[/{command}]命令, 未能成功获取父频道信息", context.User.Username, command);
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
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        _logger.LogDebug("Context.Interaction.Channel: {Context.Interaction.Channel}", Context.Interaction.Channel.Name);
        _logger.LogDebug("Context.Channel: {Context.Interaction.Channel}", Context.Channel.Name);
        // --- 是否在论坛频道的帖子中 ---
        if (Context.Interaction.Channel is not IThreadChannel threadChannel)
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非帖子中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        if (threadChannel.CategoryId != null)
        {
            var parentChannel = await threadChannel.Guild.GetChannelAsync(threadChannel.CategoryId.Value, CacheMode.AllowDownload);
            if (parentChannel is not IForumChannel)
            {
                await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在帖子中使用"), ephemeral: true);
                _logger.LogWarning("鉴权失败: 用户 {Username} 在文字频道的子区中执行了[/{command}]命令", context.User.Username, command);
                return false;
            }
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("出现网络错误, 请重试"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在执行了[/{command}]命令, 未能成功获取父频道信息", context.User.Username, command);
            return false;
        }

        _logger.LogInformation("鉴权通过: 用户 {Username} 执行了[/{command}]命令", context.User.Username, command);
        return true;
    }
    private async Task<bool> InGuild(string command, IInteractionContext context)
    {
        // === 鉴权与参数验证 ===
        // --- 是否在配置的服务器内 ---
        if (!_authentication.InGuild(context.Guild.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("该命令只能在授权服务器中使用"), ephemeral: true);
            _logger.LogWarning("鉴权失败: 用户 {Username} 在非授权服务器中执行了[/{command}]命令", context.User.Username, command);
            return false;
        }
        _logger.LogInformation("鉴权通过: 用户 {Username} 执行了[/{command}]命令", context.User.Username, command);
        return true;
    }
    #endregion

    #region 维护命令
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
            var FollowupMessage = await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"Pong! 🏓\nWebSocket延迟: {websocketLatency}ms"), ephemeral: true);
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"{FollowupMessage.Id}"), ephemeral: true);
        }
    }

    [SlashCommand("全扫描", "执行全扫描")]
    public async Task FullSyncScanAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await IsAdmin("全扫描", Context)) return;
        // === 命令逻辑 ===
        try
        {
            // 执行扫描
            var scanResult = _scanner.FullSync();
            var description =
                "扫描结果:\n" +
                $"总帖子数量: {scanResult.ScanAllResult}\n" +
                "----------------\n" +
                "并发处理帖子数据结果:\n" +
                $"> 成功: {scanResult.ThreadProcessResult.SuccessCount}\n" +
                $"> 失败: {scanResult.ThreadProcessResult.FailedCount}\n" +
                $"> 跳过: {scanResult.ThreadProcessResult.SkippedCount}\n" +
                $"> 总计: {scanResult.ThreadProcessResult.ThreadsCount}\n" +
                $"> 总耗时: {scanResult.ThreadProcessResult.TotalElapsed:F2}秒\n" +
                $"> 平均速度: {scanResult.ThreadProcessResult.OverallAvgSpeed:F2} 任务/秒 | {scanResult.ThreadProcessResult.OverallAvgSpeed * 2:F2} API 请求/秒\n" +
                "----------------\n" +
                $"数据库写入结果:\n" +
                $"> 帖子主表写入行数: {scanResult.InsertPostResult.MainAffectedRows}\n" +
                $"> FTS表写入行数: {scanResult.InsertPostResult.FtsAffectedRows}\n" +
                $"> 帖子标签关系表写入行数: {scanResult.InsertPostResult.TagJunctionAffectedRows}\n" +
                $"数据库失效帖子数量: {scanResult.ScanObsoleteResult}\n" +
                $"数据库成功移除行数: {scanResult.ScanRemoveResult}";
            // 回复
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed(description, "扫描更新成功"), ephemeral: false);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("扫描失败", ex.ToString()));
            _logger.LogError("扫描失败, 原因:{ex}", ex);
        }
    }

    [SlashCommand("创建管理面板", "在本频道内创建一个用于快捷搜索与查看收藏和订阅的面板")]
    public async Task CreateManagerEmbed()
    {
        await DeferAsync(ephemeral: true);
        // 鉴权: 如果不是admin, 则返回
        if (!await IsAdmin("创建管理面板", Context)) return;

        var embed = new EmbedBuilder()
            .WithDescription("")
            .Build();
        var component = new ComponentBuilder()
            .WithButton("直接搜索: 无过滤条件的浏览发帖时间从新到旧的200个帖子", customId: "direct_search", style: ButtonStyle.Primary, row: 0)
            .WithButton("查看全部收藏的帖子", customId: "view_favorite", style: ButtonStyle.Primary, row: 1)
            .WithButton("查看全部订阅的帖子", customId: "view_subthread", style: ButtonStyle.Primary, row: 2)
            .WithButton("查看更新: 查看订阅帖子的更新推送消息", customId: "view_pushlist", style: ButtonStyle.Primary, row: 3)
            .WithButton("快捷搜索: 打开快速搜索菜单", customId:"quick_search", style: ButtonStyle.Primary, row: 4)
            .Build();

        await FollowupAsync(embed: embed, components: component);
    }

    #endregion

    #region 订阅功能命令
    [SlashCommand("订阅", "订阅该帖子内的作品更新(PNG附件/JSON附件)")]
    public async Task SubscribeAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InScanChannelThread("订阅", Context)) return;

        // === 命令逻辑 ===
        if (Context.Channel is IThreadChannel threadChannel)
        {
            // 检查是否已经订阅
            var isSubscribed = await _databaseService.QueryUserSubscriptionsExistAsync(Context.User.Id, threadChannel.Id);
            if (isSubscribed)
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "你已经订阅过这个帖子啦，不用重复订阅哦。"), ephemeral: true);
                return;
            }

            // 发送确认消息
            var embed = new EmbedBuilder()
                .WithTitle("🔄 确认订阅")
                .WithDescription($"你确定要订阅帖子 **{threadChannel.Name}** 的更新吗？")
                .WithFooter("订阅后，当帖子内有新的作品发布时，我会私信通知你哦！")
                .WithColor(Color.Blue)
                .Build();

            var components = new ComponentBuilder()
                // CustomId 格式: add_ST_{userId}
                .WithButton("确认订阅", $"add_ST_{Context.User.Id}", ButtonStyle.Success)
                .WithButton("放弃", "general_cancel", ButtonStyle.Danger)
                .Build();

            await FollowupAsync(embed: embed, components: components, ephemeral: true);
        }
    }

    [SlashCommand("取消订阅", "取消订阅该帖子内的作品更新(PNG附件/JSON附件)")]
    public async Task UnsubscribeAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InScanChannelThread("取消订阅", Context)) return;

        // === 命令逻辑 ===
        if (Context.Channel is IThreadChannel threadChannel)
        {
            // 检查是否真的订阅了
            var isSubscribed = await _databaseService.QueryUserSubscriptionsExistAsync(Context.User.Id, threadChannel.Id);
            if (!isSubscribed)
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", "你还没有订阅这个帖子哦。"), ephemeral: true);
                return;
            }

            // 发送确认消息
            var embed = new EmbedBuilder()
                .WithTitle("🔄 确认取消订阅")
                .WithDescription($"你真的要取消对帖子 **{threadChannel.Name}** 的订阅吗？")
                .WithColor(Color.Orange)
                .Build();

            var components = new ComponentBuilder()
                // CustomId 格式: remove_ST_{userId}
                .WithButton("确认取消", $"remove_ST_{Context.User.Id}", ButtonStyle.Danger)
                .WithButton("放弃", "general_cancel", ButtonStyle.Secondary)
                .Build();

            await FollowupAsync(embed: embed, components: components, ephemeral: true);
        }
    }

    [SlashCommand("取消订阅-链接", "通过帖子链接取消订阅")]
    public async Task UnsubscribeByLinkAsync(
        [Summary("链接", "要取消订阅的帖子链接")]
        string link
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("取消订阅-链接", Context)) return;

        // 1. 解析链接，提取 Guild ID 和 Thread ID
        var match = Regex.Match(link, @"/channels/(\d+)/(\d+)");
        if (!match.Success)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "提供的链接格式不正确哦，请检查一下。"), ephemeral: true);
            return;
        }

        // 2. 验证链接有效性
        var guildIdStr = match.Groups[1].Value;
        var threadIdStr = match.Groups[2].Value;

        if (!ulong.TryParse(threadIdStr, out var threadId) || !ulong.TryParse(guildIdStr, out var guildId))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "无法从链接中解析出有效的ID。"), ephemeral: true);
            return;
        }

        if (guildId != Context.Guild.Id)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "这个链接似乎是来自另一个服务器的，我没办法操作哦。"), ephemeral: true);
            return;
        }

        // 3. 检查帖子是否真的在用户的订阅中
        var isSubscribed = await _databaseService.QueryUserSubscriptionsExistAsync(Context.User.Id, threadId);
        if (!isSubscribed)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", "您并没有订阅过这个帖子呀。"), ephemeral: true);
            return;
        }

        // 尝试获取帖子名称用于显示
        string threadName = $"帖子(ID: {threadId})";
        var channel = await Context.Client.GetChannelAsync(threadId) as IThreadChannel;
        if (channel != null)
        {
            threadName = channel.Name;
        }

        // 5. 发送确认消息
        var embed = new EmbedBuilder()
            .WithTitle("🔄 确认取消订阅")
            .WithDescription($"您真的要取消对帖子 **{threadName}** 的订阅吗？")
            .WithColor(Color.Orange)
            .Build();

        var components = new ComponentBuilder()
            // ✨ 使用新的 CustomId 格式: remove_ST_id_{userId}_{threadId} ✨
            .WithButton("确认取消", $"remove_ST_id_{Context.User.Id}_{threadId}", ButtonStyle.Danger)
            .WithButton("放弃", "general_cancel", ButtonStyle.Secondary)
            .Build();

        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }

    [SlashCommand("查看订阅", "查看您已订阅的帖子列表")]
    public async Task ViewSubscriptionsAsync(
        [Summary("排序", "按订阅时间排序, 默认为从新到旧")]
        [Choice("从新到旧", "new")]
        [Choice("从旧到新", "old")]
        string sort = "new"
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("查看订阅", Context)) return;

        // === 1. 查询订阅列表 ===
        var queryResult = await _databaseService.QueryUserSubscriptionsAsync(Context.User.Id);
        if (queryResult == null || !queryResult.Any())
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle("空空如也")
                .WithDescription("您的订阅列表里还没有任何帖子哦。")
                .WithColor(Color.Blue)
                .Build();
            await FollowupAsync(embed: emptyEmbed, ephemeral: true);
            return;
        }

        // === 2. 排序 ===
        List<PostDisplayModel> sortedResult;
        if (sort == "new")
        {
            sortedResult = queryResult.OrderByDescending(post => post.AddedAt).ToList();
        }
        else
        {
            sortedResult = queryResult.OrderBy(post => post.AddedAt).ToList();
        }

        // === 3. 创建分页并发送 (逻辑与查看收藏一致) ===
        var postPages = sortedResult.Chunk(6).Select(chunk => chunk.ToList()).ToList();
        var embeds = DisplayEmbedBuilder.ToSubscriptionsEmbedList(postPages, _databaseService.BaselineVersion);

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var uuid = $"{Context.User.Id}_{currentTime}_subscriptions";
        var currentNum = 1;
        var totalPages = embeds.Count;
        _paginatorManager.Register(uuid, embeds);

        bool isLastPage = totalPages == 1;
        var button = new ComponentBuilder()
            .WithButton("首页", $"P_first_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton("上一页", $"P_prev_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton($"{currentNum}/{totalPages}", $"P_jump_{totalPages}_{uuid}", style: ButtonStyle.Secondary)
            .WithButton("下一页", $"P_next_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .WithButton("尾页", $"P_last_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .Build();

        await FollowupAsync(embeds: embeds[0].ToArray(), components: button, ephemeral: true);
    }

    [SlashCommand("查看更新", "查看已订阅帖子中已更新作品的推送列表")]
    public async Task ViewUpdatesAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("查看更新", Context)) return;

        // === 1. 查询推送列表 ===
        var queryResult = await _databaseService.QueryUserPushListAsync(Context.User.Id);
        if (queryResult == null || !queryResult.Any())
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle("风平浪静")
                .WithDescription("您的更新推送列表是空的，所有订阅的帖子都没有新的作品哦。")
                .WithColor(Color.Green)
                .Build();
            await FollowupAsync(embed: emptyEmbed, ephemeral: true);
            return;
        }

        // === 2. 排序 (固定按更新时间从新到旧) ===
        var sortedResult = queryResult.OrderByDescending(post => post.UpdatedAt).ToList();

        // === 3. 创建分页并发送 ===
        var postPages = sortedResult.Chunk(6).Select(chunk => chunk.ToList()).ToList();
        var embeds = DisplayEmbedBuilder.ToSubPushListEmbedList(postPages, _databaseService.BaselineVersion);

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var uuid = $"{Context.User.Id}_{currentTime}_updates";
        var currentNum = 1;
        var totalPages = embeds.Count;
        _paginatorManager.Register(uuid, embeds);

        bool isLastPage = totalPages == 1;
        var button = new ComponentBuilder()
            .WithButton("首页", $"P_first_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton("上一页", $"P_prev_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton($"{currentNum}/{totalPages}", $"P_jump_{totalPages}_{uuid}", style: ButtonStyle.Secondary)
            .WithButton("下一页", $"P_next_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .WithButton("尾页", $"P_last_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .Build();

        await FollowupAsync(embeds: embeds[0].ToArray(), components: button, ephemeral: true);
    }

    [SlashCommand("清空更新", "清空您的作品更新推送列表")]
    public async Task ClearUpdatesAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("清空更新", Context)) return;

        var embed = new EmbedBuilder()
            .WithTitle("⚠️ 确认操作")
            .WithDescription("您确定要清空所有作品更新推送列表的历史消息吗？此操作不可撤销。")
            .WithColor(Color.Orange)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("确认清空", $"clear_push_list_{Context.User.Id}", ButtonStyle.Danger)
            .WithButton("放弃", "general_cancel", ButtonStyle.Secondary)
            .Build();

        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }
    #endregion

    #region 收藏功能
    [SlashCommand("收藏", "收藏该帖子")]
    public async Task FavoriteAsync
    (
        [Summary("类别", "将帖子收藏至对应类别, 默认为不添加类别分类")]
        [Autocomplete(typeof(FavoriteClassAutoCompletion))]
        string? favorite = null
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuildThread("收藏", Context)) return; // 鉴权失败则直接返回

        // === 命令逻辑 ===
        // 转换对象
        if (Context.Channel is IThreadChannel threadChannel)
        {
            // 如果指定了收藏类别
            if (favorite != null)
            {
                // 查询用户已有收藏类别
                HashSet<string> favoriteType = await _databaseService.GetUserFavoriteTypeAsync(Context.User.Id);
                // 如果是已有的收藏类别
                if (favoriteType.Contains(favorite))
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🔄 确认操作")
                        .WithDescription($"您确定要将帖子 **{threadChannel.Name}** 添加到已存在的收藏夹 **{favorite}** 吗？")
                        .WithColor(Color.Blue)
                        .Build();

                    var components = new ComponentBuilder()
                        // customId 格式: add_F_{userId}_{favoriteName}
                        .WithButton("确认添加", $"add_F_{Context.User.Id}_{favorite}", ButtonStyle.Success)
                        .WithButton("放弃", "general_cancel", ButtonStyle.Danger)
                        .Build();

                    await FollowupAsync(embed: embed, components: components, ephemeral: true);
                }
                // 如果指定了新的收藏类别
                else
                {
                    // 收藏类别是否到了上限
                    if (favoriteType.Count >= 25)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("❌ 收藏失败")
                            .WithDescription("无法创建新的收藏类别，因为您的收藏夹数量已达到25个上限。")
                            .WithFooter("请考虑将帖子收藏至已有的收藏夹，或整理您的收藏夹。")
                            .WithColor(Color.Red)
                            .Build();
                        await FollowupAsync(embed: embed, ephemeral: true);
                    }
                    // 如果没有, 自动创建新类别
                    else
                    {
                        var buttonComponent = new ComponentBuilder()
                            .WithButton($"🔄 创建并收藏", $"create_F_{Context.User.Id}_{favorite}", ButtonStyle.Primary)
                            .WithButton("放弃", "general_cancel", ButtonStyle.Danger)
                            .Build();

                        var desc = favoriteType.Any() ? string.Join("\n", favoriteType.Select(s => $"> - {s}")) : "> - (暂无)";
                        var confirmEmbed = new EmbedBuilder()
                            .WithTitle($"🔄 新建收藏夹确认")
                            .WithDescription($"收藏夹 **{favorite}** 不存在，是否要创建它并将当前帖子收藏进去？\n\n您当前的收藏夹 ({favoriteType.Count}/25):\n" + desc)
                            .WithFooter("每个用户最多创建25个收藏夹。")
                            .WithColor(Color.Orange)
                            .Build();
                        await FollowupAsync(embed: confirmEmbed, components: buttonComponent, ephemeral: true);
                    }
                }
            }
            // 直接添加到未分类 (favorite is null)
            else
            {
                // 这里的逻辑是直接添加，可以给一个简单的确认
                var embed = new EmbedBuilder()
                   .WithTitle("🔄 确认操作")
                   .WithDescription($"您确定要将帖子 **{threadChannel.Name}** 添加到 **未分类** 收藏中吗？")
                   .WithColor(Color.Blue)
                   .Build();

                var components = new ComponentBuilder()
                    // 注意这里的 customId，用一个特殊的、不太可能重复的字符串（比如"DEFAULT"）来代表未分类
                    // 或者直接传一个空字符串，但需要确保按钮处理器能正确处理
                    // Interaction Service 无法直接传递 null 或空字符串作为通配符参数，所以用一个占位符
                    .WithButton("确认收藏", $"add_F_{Context.User.Id}_null", ButtonStyle.Success)
                    .WithButton("放弃", "general_cancel", ButtonStyle.Danger)
                    .Build();

                await FollowupAsync(embed: embed, components: components, ephemeral: true);
            }
        }
    }

    [SlashCommand("取消收藏", "取消收藏该帖子")]
    public async Task UnfavoriteAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuildThread("取消收藏", Context)) return;

        // === 命令逻辑 ===
        if (Context.Channel is IThreadChannel threadChannel)
        {
            // 检查帖子是否真的在收藏中
            var queryResult = await _databaseService.QueryUserFavoriteExistAsync(Context.User.Id, threadChannel.Id);
            if (queryResult)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🔄 确认取消收藏")
                    .WithDescription($"您真的要从收藏中移除帖子 **{threadChannel.Name}** 吗？")
                    .WithColor(Color.Orange)
                    .Build();

                var components = new ComponentBuilder()
                    // 我们可以创建一个新的按钮处理器来处理取消收藏的逻辑
                    // customId 格式: remove_F_{userId}  (我们只需要用户ID，帖子ID可以从交互的channel获取)
                    .WithButton("确认移除", $"remove_F_{Context.User.Id}", ButtonStyle.Danger)
                    .WithButton("放弃", "general_cancel", ButtonStyle.Secondary)
                    .Build();

                await FollowupAsync(embed: embed, components: components, ephemeral: true);
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", "这个帖子不在您的收藏列表中哦。"), ephemeral: true);
            }
        }
        else
        {
            // 这个分支理论上不会被触发，因为 InGuildThread 已经检查过了
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("错误", "无法在非帖子频道执行此操作。"), ephemeral: true);
        }
    }

    [SlashCommand("取消收藏-链接", "通过帖子链接取消收藏")]
    public async Task UnfavoriteByLinkAsync(
        [Summary("链接", "要取消收藏的帖子链接")]
        string link
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("取消收藏-链接", Context)) return;

        // 1. 解析链接，提取 Guild ID 和 Thread ID
        // 正则表达式匹配类似 https://discord.com/channels/12345/67890 的链接
        var match = Regex.Match(link, @"/channels/(\d+)/(\d+)");
        if (!match.Success)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "提供的链接格式不正确哦，请检查一下。"), ephemeral: true);
            return;
        }

        // 验证链接有效性
        var guildIdStr = match.Groups[1].Value;
        var threadIdStr = match.Groups[2].Value;

        if (!ulong.TryParse(threadIdStr, out var threadId) || !ulong.TryParse(guildIdStr, out var guildId))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "无法从链接中解析出有效的ID。"), ephemeral: true);
            return;
        }

        if (guildId != Context.Guild.Id)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "这个链接似乎是来自另一个服务器的，我没办法操作哦。"), ephemeral: true);
            return;
        }

        // 检查帖子是否真的在用户的收藏中
        var isFavorited = await _databaseService.QueryUserFavoriteExistAsync(Context.User.Id, threadId);
        if (!isFavorited)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", "您并没有收藏过这个帖子呀。"), ephemeral: true);
            return;
        }

        // 尝试获取帖子名称用于显示
        string threadName = $"帖子(ID: {threadId})";
        var channel = await Context.Client.GetChannelAsync(threadId) as IThreadChannel;
        if (channel != null)
        {
            threadName = channel.Name;
        }

        // 发送确认消息
        var embed = new EmbedBuilder()
            .WithTitle("🔄 确认取消收藏")
            .WithDescription($"您真的要从收藏中移除帖子 **{threadName}** 吗？")
            .WithColor(Color.Orange)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("确认移除", $"remove_F_id_{Context.User.Id}_{threadId}", ButtonStyle.Danger)
            .WithButton("放弃", "general_cancel", ButtonStyle.Secondary)
            .Build();

        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }

    [SlashCommand("查看收藏", "查看自己的收藏列表")]
    public async Task ViewFavoriteAsync
    (
        [Summary("类别", "将帖子收藏至对应类别, 默认为不添加类别分类")]
        [Autocomplete(typeof(FavoriteClassAutoCompletion))]
        string? favorite = null,

        [SummaryAttribute("排序", "按添加收藏的时间排序, 默认为收藏时间从新到旧")]
        [Choice("从新到旧", "new")]
        [Choice("从旧到新", "old")]
        string? sort = "new"
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("查看收藏", Context)) return;

        // === 查询收藏 ===
        var queryResult = await _databaseService.QueryUserFavoriteAsync(Context.User.Id, favorite);
        if (queryResult == null || !queryResult.Any()) // 检查是否为空或无元素
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle("空空如也")
                .WithDescription(favorite == null ? "您的收藏夹里还没有任何帖子哦。" : $"在 **{favorite}** 收藏夹里没有找到任何帖子。")
                .WithColor(Color.Blue)
                .Build();
            await FollowupAsync(embed: emptyEmbed, ephemeral: true);
            _logger.LogInformation("用户 {UserId} 查询收藏夹 '{favorite}' 结果为空", Context.User.Id, favorite ?? "未分类");
            return;
        }

        // 根据排序方式对查询结果进行排序
        List<PostDisplayModel> sortedResult;
        if (sort == "new")
        {
            sortedResult = queryResult.OrderByDescending(post => post.AddedAt).ToList();
        }
        else
        {
            sortedResult = queryResult.OrderBy(post => post.AddedAt).ToList();
        }

        // 创建分页结果
        var postPages = sortedResult.Chunk(6).Select(chunk => chunk.ToList()).ToList();
        if (!postPages.Any())
        {
            // 理论上不会到这里，因为前面已经检查过了，但作为安全措施
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("错误", "无法生成收藏页面"), ephemeral: true);
            return;
        }
        var embeds = DisplayEmbedBuilder.ToFavoriteEmbedList(postPages, _databaseService.BaselineVersion);

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var uuid = $"{Context.User.Id}_{currentTime}_favorite"; // 确保uuid的唯一性
        var currentNum = 1;
        var totalPages = embeds.Count;
        _paginatorManager.Register(uuid, embeds);

        // === 交互消息 ===
        // 动态判断按钮是否应该被禁用
        bool isLastPage = totalPages == 1;

        var button = new ComponentBuilder()
            .WithButton("首页", $"P_first_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton("上一页", $"P_prev_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton($"{currentNum}/{totalPages}", $"P_jump_{totalPages}_{uuid}", style: ButtonStyle.Secondary)
            .WithButton("下一页", $"P_next_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .WithButton("尾页", $"P_last_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .Build();

        var firstPageEmbeds = embeds[0];
        await FollowupAsync(embeds: firstPageEmbeds.ToArray(), components: button, ephemeral: true);
    }

    [SlashCommand("删除收藏夹", "删除一个收藏夹并处理其中的帖子")]
    public async Task DeleteFavoriteCategoryAsync(
        [Summary("收藏夹", "要删除的收藏夹名称")]
        [Autocomplete(typeof(FavoriteClassAutoCompletion))]
        string favorite,

        [Summary("处理方式", "如何处理该收藏夹内的帖子")]
        [Choice("全部移除收藏 (不可逆)", "remove")]
        [Choice("保留并移至'未分类'", "keep")]
        string action
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("删除收藏夹", Context)) return;

        var favoriteTypes = await _databaseService.GetUserFavoriteTypeAsync(Context.User.Id);
        if (!favoriteTypes.Contains(favorite))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", $"你没有一个叫 **{favorite}** 的收藏夹哦。"), ephemeral: true);
            return;
        }

        var actionDescription = action == "remove" 
            ? "该收藏夹里的所有帖子都将被**取消收藏**" 
            : "该收藏夹里的所有帖子都将被**移至未分类**";

        var embed = new EmbedBuilder()
            .WithTitle("⚠️ 确认删除操作")
            .WithDescription($"你确定要删除收藏夹 **{favorite}** 吗？\n\n**重要提示**: {actionDescription}。\n此操作无法撤销！")
            .WithColor(Color.Red)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("确认删除", $"delete_F_cat_{Context.User.Id}_{action}_{favorite}", ButtonStyle.Danger)
            .WithButton("我再想想", "general_cancel", ButtonStyle.Secondary)
            .Build();

        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }

    [SlashCommand("重命名收藏夹", "更改一个收藏夹的名称")]
    public async Task RenameFavoriteCategoryAsync(
        [Summary("旧名称", "选择要重命名的收藏夹")]
        [Autocomplete(typeof(FavoriteClassAutoCompletion))]
        string oldName,
        [Summary("新名称", "为收藏夹起一个新名字")]
        string newName
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("重命名收藏夹", Context)) return;
        
        // --- 参数校验 ---
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 50)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "新名称不能为空，且长度不能超过50个字符哦。"), ephemeral: true);
            return;
        }

        if (oldName == newName)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", "新旧名称不能一样呀！"), ephemeral: true);
            return;
        }

        var favoriteTypes = await _databaseService.GetUserFavoriteTypeAsync(Context.User.Id);
        if (!favoriteTypes.Contains(oldName))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", $"你没有一个叫 **{oldName}** 的收藏夹哦。"), ephemeral: true);
            return;
        }
        if (favoriteTypes.Contains(newName))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", $"你已经有一个叫 **{newName}** 的收藏夹啦，换个名字吧！"), ephemeral: true);
            return;
        }

        // --- 发送确认 ---
        var embed = new EmbedBuilder()
            .WithTitle("🔄 确认重命名")
            .WithDescription($"你确定要将收藏夹 **{oldName}** 的名字改成 **{newName}** 吗？")
            .WithColor(Color.Blue)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("确认更改", $"rename_F_cat_{Context.User.Id}_{oldName}_{newName}", ButtonStyle.Success)
            .WithButton("放弃", "general_cancel", ButtonStyle.Secondary)
            .Build();

        await FollowupAsync(embed: embed, components: components, ephemeral: true);
    }
    #endregion

    #region 搜索功能
    [SlashCommand("搜索", "根据多种条件搜索论坛帖子")]
    public async Task SearchAsync
    (
        [Summary("论坛分区", "指定在哪个论坛分区内搜索")]
        [Autocomplete(typeof(ForumCategoriesAutoCompletion))] // 复用已有的自动补全！
        string? forumChannel = null,

        [Summary("帖子作者", "只搜索这位作者发布的帖子")]
        IUser? author = null,

        [Summary("关键词1", "在标题或内容中搜索, 多个词用逗号/空格隔开, 单个关键词参数内的多个词为'与(AND)', 不同关键词参数间为'或(OR)'")]
        string? keyWord1 = null,

        [Summary("关键词2", "在标题或内容中搜索, 多个词用逗号/空格隔开, 单个关键词参数内的多个词为'与(AND)', 不同关键词参数间为'或(OR)'")]
        string? keyWord2 = null,

        [Summary("关键词3", "在标题或内容中搜索, 多个词用逗号/空格隔开, 单个关键词参数内的多个词为'与(AND)', 不同关键词参数间为'或(OR)'")]
        string? keyWord3 = null,

        [Summary("排除关键词1", "排除标题或内容中包含任意一个词的帖子, 多个用逗号/空格隔开 单个关键词参数内的多个词为'与(AND)', 不同关键词参数间为'或(OR)'")]
        string? exKeyWord1 = null,

        [Summary("排除关键词2", "排除标题或内容中包含任意一个词的帖子, 多个用逗号/空格隔开 单个关键词参数内的多个词为'与(AND)', 不同关键词参数间为'或(OR)'")]
        string? exKeyWord2 = null,

        [Summary("排除关键词3", "排除标题或内容中包含任意一个词的帖子, 多个用逗号/空格隔开 单个关键词参数内的多个词为'与(AND)', 不同关键词参数间为'或(OR)'")]
        string? exKeyWord3 = null,

        [Summary("包含标签1-AND包含组-", "帖子必须同时包含包含AND组标签(AND)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? tags1and = null,

        [Summary("包含标签2-AND包含组-", "帖子必须同时包含包含AND组标签(AND)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? tags2and = null,

        [Summary("包含标签3-AND包含组-", "帖子必须同时包含包含AND组标签(AND)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? tags3and = null,

        [Summary("包含标签1-OR包含组-", "帖子必须包含OR组标签之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? tags1or = null,

        [Summary("包含标签2-OR包含组-", "帖子必须包含OR组标签之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? tags2or = null,

        [Summary("包含标签3-OR包含组-", "帖子必须包含OR组标签之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? tags3or = null,

        [Summary("排除标签1-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? exTags1 = null,

        [Summary("排除标签2-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? exTags2 = null,

        [Summary("排除标签3-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? exTags3 = null,

        [Summary("排除标签4-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")]
        [Autocomplete(typeof(AllTagsAutoCompletion))]
        string? exTags4 = null,

        [Summary("排除作者1", "在结果中排除这位作者的帖子(OR)")]
        IUser? exAuthor1 = null,

        [Summary("排除作者2", "在结果中排除这位作者的帖子(OR)")]
        IUser? exAuthor2 = null,

        [Summary("排除作者3", "在结果中排除这位作者的帖子(OR)")]
        IUser? exAuthor3 = null,

        [Summary("排序方式", "结果的排序方式")]
        [Choice("发帖时间降序(从新到旧)", "posted_date_desc")]
        [Choice("发帖时间升序(从旧到新)", "posted_date_asc")]
        [Choice("最后活跃降序(从新到旧)", "last_active_desc")]
        [Choice("最后活跃升序(从旧到新)", "last_active_asc")]
        [Choice("最高反应降序(从高到低)", "reactions_desc")]
        [Choice("最高反应升序(从低到高)", "reactions_asc")]
        [Choice("总回复数降序(从高到低)", "replies_desc")]
        [Choice("总回复数升序(从低到高)", "replies_asc")]
        string order = "posted_date_desc",

        [Summary("起始日期", "搜索在此日期之后创建的帖子 (格式: YYYY-MM-DD)")]
        string? startTime = null,

        [Summary("结束日期", "搜索在此日期之前创建的帖子 (格式: YYYY-MM-DD)")]
        string? endTime = null,

        [Summary("结果上限", "最多返回多少条结果")]
        [MinValue(10)]
        [MaxValue(1000)]
        int resultLimit = 200
    )
    {
        // === 鉴权 ===
        await DeferAsync(ephemeral: true);
        if (!await InGuild("搜索", Context)) return;
        if (_userCooldown.IsOnCooldown(Context.User.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("命令尚在一分钟冷却之内, 请稍候再试"));
            return;
        }

        // === 1. 构建查询条件 ===
        _logger.LogInformation("用户 {User} 正在执行搜索...", Context.User.Username);
        var criteria = await _criteriaBuilder.BuildAsync(
            forumChannel, author,
            keyWord1, keyWord2, keyWord3,
            tags1and, tags2and, tags3and,
            tags1or, tags2or, tags3or,
            exTags1, exTags2, exTags3, exTags4,
            exKeyWord1, exKeyWord2, exKeyWord3,
            exAuthor1, exAuthor2, exAuthor3,
            order, startTime, endTime, resultLimit
        );
        _logger.LogDebug("用户 {User} 的搜索模型构建完毕...", Context.User.Username);

        // === 2. 执行数据库查询 ===
        await SearchUtils.ExecuteAndDisplaySearchAsync(_logger, _databaseService, _paginatorManager, criteria, Context.Interaction);
    }
    #endregion

    #region 快捷搜索功能
    [SlashCommand("添加快捷搜索", "将当前的搜索条件保存为一个快捷命令")]
    public async Task AddQuickSearchAsync(
        [Summary("命令名称", "为这个快捷搜索起一个独一无二的名字")] string commandName,
        [Summary("论坛分区", "指定在哪个论坛分区内搜索")][Autocomplete(typeof(ForumCategoriesAutoCompletion))] string? forumChannel = null,
        [Summary("帖子作者", "只搜索这位作者发布的帖子")] IUser? author = null,
        [Summary("关键词1", "在标题或内容中搜索")] string? keyWord1 = null,
        [Summary("关键词2", "在标题或内容中搜索")] string? keyWord2 = null,
        [Summary("关键词3", "在标题或内容中搜索")] string? keyWord3 = null,
        [Summary("排除关键词1", "排除标题或内容中包含任意一个词的帖子")] string? exKeyWord1 = null,
        [Summary("排除关键词2", "排除标题或内容中包含任意一个词的帖子")] string? exKeyWord2 = null,
        [Summary("排除关键词3", "排除标题或内容中包含任意一个词的帖子")] string? exKeyWord3 = null,
        [Summary("包含标签1-AND包含组-", "帖子必须同时包含包含AND组标签(AND)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? tags1and = null,
        [Summary("包含标签2-AND包含组-", "帖子必须同时包含包含AND组标签(AND)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? tags2and = null,
        [Summary("包含标签3-AND包含组-", "帖子必须同时包含包含AND组标签(AND)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? tags3and = null,
        [Summary("包含标签1-OR包含组-", "帖子必须包含OR组标签之一(OR)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? tags1or = null,
        [Summary("包含标签2-OR包含组-", "帖子必须包含OR组标签之一(OR)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? tags2or = null,
        [Summary("包含标签3-OR包含组-", "帖子必须包含OR组标签之一(OR)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? tags3or = null,
        [Summary("排除标签1-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? exTags1 = null,
        [Summary("排除标签2-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? exTags2 = null,
        [Summary("排除标签3-OR排除组-", "帖子不能包含这个OR排除组之一(OR)")][Autocomplete(typeof(AllTagsAutoCompletion))] string? exTags3 = null,
        [Summary("排除作者1", "在结果中排除这位作者的帖子(OR)")] IUser? exAuthor1 = null,
        [Summary("排除作者2", "在结果中排除这位作者的帖子(OR)")] IUser? exAuthor2 = null,
        [Summary("排除作者3", "在结果中排除这位作者的帖子(OR)")] IUser? exAuthor3 = null,
        [Summary("排序方式", "结果的排序方式")]
        [Choice("发帖时间降序(从新到旧)", "posted_date_desc")]
        [Choice("发帖时间升序(从旧到新)", "posted_date_asc")]
        [Choice("最后活跃降序(从新到旧)", "last_active_desc")]
        [Choice("最后活跃升序(从旧到新)", "last_active_asc")]
        [Choice("最高反应降序(从高到低)", "reactions_desc")]
        [Choice("最高反应升序(从低到高)", "reactions_asc")]
        [Choice("总回复数降序(从高到低)", "replies_desc")]
        [Choice("总回复数升序(从低到高)", "replies_asc")]
        string order = "posted_date_desc",
        [Summary("起始日期", "搜索在此日期之后创建的帖子 (格式: YYYY-MM-DD)")] string? startTime = null,
        [Summary("结束日期", "搜索在此日期之前创建的帖子 (格式: YYYY-MM-DD)")] string? endTime = null,
        [Summary("结果上限", "最多返回多少条结果")][MinValue(10)][MaxValue(1000)] int resultLimit = 200
    )
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("添加快捷搜索", Context)) return;

        var quickSearchCommands = await _databaseService.GetUserQuickSearchesAsync(Context.User.Id);
        if (quickSearchCommands.Count >= 25)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("添加失败", "您的快捷搜索命令已达到25个上限，请先删除一些不用的命令。"), ephemeral: true);
            return;
        }

        string? exTags4 = null;

        var criteria = await _criteriaBuilder.BuildAsync(
            forumChannel, author, keyWord1, keyWord2, keyWord3,
            tags1and, tags2and, tags3and, tags1or, tags2or, tags3or,
            exTags1, exTags2, exTags3, exTags4, exKeyWord1, exKeyWord2, exKeyWord3,
            exAuthor1, exAuthor2, exAuthor3, order, startTime, endTime, resultLimit
        );

        if (criteria.IsEmpty())
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("添加失败", "您必须至少提供一个搜索条件才能保存快捷命令哦"), ephemeral: true);
            return;
        }

        var commandToSave = new QuickSearchCommandModel
        {
            UserId = Context.User.Id,
            CommandName = commandName,
            CriteriaJson = JsonSerializer.Serialize(criteria)
        };

        await _databaseService.InsertQuickSearchAsync(commandToSave);

        var description = SearchParameterMapper.ToDisplayLabel(criteria);
        await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"快捷命令 **{commandName}** 已保存！\n\n**参数预览:**\n> {description}"), ephemeral: true);
    }

    [SlashCommand("快捷搜索", "执行一个已保存的快捷搜索")]
    public async Task QuickSearchAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("快捷搜索", Context)) return;

        var commands = await _databaseService.GetUserQuickSearchesAsync(Context.User.Id);
        if (!commands.Any())
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("没有找到快捷命令", "您还没有添加任何快捷搜索命令哦，快使用 `/添加快捷搜索` 来创建一个吧！"), ephemeral: true);
            return;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId($"qs_execute_{Context.User.Id}")
            .WithPlaceholder("选择一个要执行的快捷命令");

        foreach (var cmd in commands)
        {
            var criteria = JsonSerializer.Deserialize<SearchCriteria>(cmd.CriteriaJson);
            menu.AddOption(
                label: cmd.CommandName.Slice(100), // 标签最多100字符
                value: cmd.CommandName,
                description: SearchParameterMapper.ToDisplayLabel(criteria).Slice(100) // 描述最多100字符
            );
        }

        var embed = new EmbedBuilder()
            .WithTitle("🚀 执行快捷搜索")
            .WithDescription("请从下面的菜单中选择一个您想要执行的快捷搜索命令。")
            .WithColor(Color.Blue)
            .Build();

        await FollowupAsync(embed: embed, components: new ComponentBuilder().WithSelectMenu(menu).Build(), ephemeral: true);
    }

    [SlashCommand("删除快捷搜索", "删除一个已保存的快捷搜索")]
    public async Task DeleteQuickSearchAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await InGuild("删除快捷搜索", Context)) return;

        var commands = await _databaseService.GetUserQuickSearchesAsync(Context.User.Id);
        if (!commands.Any())
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("列表为空", "您没有任何可以删除的快捷搜索命令。"), ephemeral: true);
            return;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId($"qs_delete_select_{Context.User.Id}")
            .WithPlaceholder("选择一个要删除的快捷命令");

        foreach (var cmd in commands)
        {
            var criteria = JsonSerializer.Deserialize<SearchCriteria>(cmd.CriteriaJson);
            menu.AddOption(cmd.CommandName.Slice(100), cmd.CommandName, SearchParameterMapper.ToDisplayLabel(criteria).Slice(100));
        }

        var embed = new EmbedBuilder()
            .WithTitle("🗑️ 删除快捷搜索")
            .WithDescription("请从下面的菜单中选择一个您想要删除的快捷搜索命令。")
            .WithColor(Color.Red)
            .Build();

        await FollowupAsync(embed: embed, components: new ComponentBuilder().WithSelectMenu(menu).Build(), ephemeral: true);
    }
    #endregion

}