// .NET 核心库
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Scheduler;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

[Group("订阅功能", "订阅功能相关命令")]
public class SubscriptionCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly PaginatorManager _paginatorManager;
    private readonly InteractionUtils _interactionUtils;
    public SubscriptionCommands(
        ILogger logger,
        AuthUtils authUtils,
        DatabaseService databaseService,
        PaginatorManager paginatorManager,
        InteractionUtils interactionUtils
    )
    {
        _logger = logger.ForContext("SubscriptionCommands");
        _authUtils = authUtils;
        _databaseService = databaseService;
        _paginatorManager = paginatorManager;
        _interactionUtils = interactionUtils;
    }

    [SlashCommand("订阅", "订阅该帖子内的作品更新(PNG附件/JSON附件)")]
    public async Task SubscribeAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InScanChannelThread("订阅", Context)) return;

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
        if (!await _authUtils.InScanChannelThread("取消订阅", Context)) return;

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
        if (!await _authUtils.InGuild("取消订阅-链接", Context)) return;

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
            // 使用新的 CustomId 格式: remove_ST_id_{userId}_{threadId}
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
        try
        {
            await DeferAsync(ephemeral: true);
            if (!await _authUtils.InGuild("查看订阅", Context)) return;
    
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
            // 调用新的 ToSubscriptionsEmbedList 方法
            var (allEmbeds, allPosts) = DisplayEmbedBuilder.ToSubscriptionsEmbedList(sortedResult, _databaseService.BaselineVersion);
    
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // 使用新的 uuid 格式
            var uuid = $"{Context.User.Id}_{currentTime}_sub"; 
            
            // 创建 InteractivePaginatorState，并指定类型为 Subscription
            var state = new InteractivePaginatorState(allPosts, allEmbeds, PaginatorType.Subscription);
            _paginatorManager.RegisterInteractive(uuid, state);
    
            var totalPages = state.GetTotalPages();
    
            // === 交互消息 ===
            // 获取第一页的数据
            var (postsOnFirstPage, embedsOnFirstPage) = state.GetPage(1);
            
            // 构建组件，传入正确的 PaginatorType
            var components = _interactionUtils.BuildInteractiveComponents(uuid, 1, totalPages, postsOnFirstPage, PaginatorType.Subscription, Context);
    
            // 发送响应
            await FollowupAsync(embeds: embedsOnFirstPage.ToArray(), components: components, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error("用户查看订阅出错", ex);
        }
    }

    [SlashCommand("查看更新", "查看已订阅帖子中已更新作品的推送列表")]
    public async Task ViewUpdatesAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("查看更新", Context)) return;

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
        if (!await _authUtils.InGuild("清空更新", Context)) return;

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
}