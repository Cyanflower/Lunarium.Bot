// .NET 核心库
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Model;
using LunariumBot.Net.SearchBot.Scheduler;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

[Group("收藏功能", "收藏功能相关命令")]
public class FavoriteCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly PaginatorManager _paginatorManager;
    private readonly InteractionUtils _interactionUtils;
    public FavoriteCommands(
        ILogger logger,
        AuthUtils authUtils,
        DatabaseService databaseService,
        PaginatorManager paginatorManager,
        InteractionUtils interactionUtils
    )
    {
        _logger = logger.ForContext("FavoriteCommands");
        _authUtils = authUtils;
        _databaseService = databaseService;
        _paginatorManager = paginatorManager;
        _interactionUtils = interactionUtils;
    }

    [SlashCommand("收藏", "收藏该帖子")]
    public async Task FavoriteAsync()
    {
        try
        {
            await DeferAsync(ephemeral: true);
            if (!await _authUtils.InGuildThread("收藏", Context)) return; // 鉴权失败则直接返回
    
            if (Context.Channel is not IThreadChannel threadChannel) return;
    
            // 检查是否已经收藏过了，避免重复操作
            var isFavorited = await _databaseService.QueryUserFavoriteExistAsync(Context.User.Id, threadChannel.Id);
            if (isFavorited)
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "你已经收藏过这个帖子啦！"), ephemeral: true);
                return;
            }
    
            // 获取用户的所有收藏夹
            var favoriteTypes = await _databaseService.GetUserFavoriteTypeAsync(Context.User.Id);
            
            // --- 构建下拉菜单 ---
            var menuBuilder = new SelectMenuBuilder()
                .WithCustomId($"favorite_select_{Context.User.Id}")
                .WithPlaceholder("选择一个收藏夹");
    
            // 1. 添加 "未分类" 选项 (使用特殊值来识别)
            menuBuilder.AddOption("收藏至 '未分类'", "__UNCLASSIFIED__", "不指定任何分类进行收藏");
    
            // 2. 如果收藏夹没满，就添加 "创建新收藏夹" 选项
            if (favoriteTypes.Count < 25)
            {
                menuBuilder.AddOption("➕ 创建新收藏夹并收藏", "__CREATE_NEW__", "创建一个新的收藏夹来存放这个帖子");
            }
    
            // 3. 添加所有已存在的收藏夹
            foreach (var type in favoriteTypes)
            {
                menuBuilder.AddOption(type, type); // label 和 value 都用收藏夹名字
            }
    
            // --- 构建 Embed ---
            var embed = new EmbedBuilder()
                .WithTitle("⭐ 添加到收藏")
                .WithDescription($"请选择要将帖子 **{threadChannel.Name}** 收藏到哪个收藏夹。\n\n你当前的收藏夹: **{favoriteTypes.Count}/25**")
                .WithColor(Color.Gold)
                .Build();
    
            var component = new ComponentBuilder()
                .WithSelectMenu(menuBuilder)
                .Build();
    
            await FollowupAsync(embed: embed, components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error("用户执行收藏命令出错: ", ex);
        }
     }

    [SlashCommand("取消收藏", "取消收藏该帖子")]
    public async Task UnfavoriteAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuildThread("取消收藏", Context)) return;

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
        if (!await _authUtils.InGuild("取消收藏-链接", Context)) return;

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
        try
        {
            await DeferAsync(ephemeral: true);
            if (!await _authUtils.InGuild("查看收藏", Context)) return;

            if (favorite == "未分类") favorite = null;
    
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
                _logger.Info($"用户 {Context.User.Id} 查询收藏夹 '{favorite ?? "未分类"}' 结果为空");
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
    
            var (allEmbeds, allPosts) = DisplayEmbedBuilder.ToFavoriteEmbedList(sortedResult, _databaseService.BaselineVersion);
    
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var uuid = $"{Context.User.Id}_{currentTime}_fav";
            var state = new InteractivePaginatorState(allPosts, allEmbeds, PaginatorType.Favorite);
            _paginatorManager.RegisterInteractive(uuid, state);
    
            var totalPages = state.GetTotalPages();
    
            // === 交互消息 ===
            // 调用新的组件构建方法来生成第一页的按钮
            var (postsOnFirstPage, embedsOnFirstPage) = state.GetPage(1);
            var components = _interactionUtils.BuildInteractiveComponents(uuid, 1, totalPages, postsOnFirstPage, PaginatorType.Favorite, Context);
    
            await FollowupAsync(embeds: embedsOnFirstPage.ToArray(), components: components, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error("用户查看收藏出错:", ex);
        }
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
        if (!await _authUtils.InGuild("删除收藏夹", Context)) return;

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
        if (!await _authUtils.InGuild("重命名收藏夹", Context)) return;
        
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
}