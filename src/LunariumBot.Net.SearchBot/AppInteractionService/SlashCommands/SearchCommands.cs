// .NET 核心库
using System.Text.Json;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Extensions;
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Scheduler;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

[Group("搜索功能", "搜索功能相关命令")]
public class SearchCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly PaginatorManager _paginatorManager;
    private readonly UserCooldown _userCooldown;
    private readonly SearchCriteriaBuilder _criteriaBuilder;
    public SearchCommands(
        ILogger logger,
        AuthUtils authUtils,
        DatabaseService databaseService,
        PaginatorManager paginatorManager,
        UserCooldown userCooldown,
        SearchCriteriaBuilder criteriaBuilder
    )
    {
        _logger = logger.ForContext("SearchCommands");
        _authUtils = authUtils;
        _databaseService = databaseService;
        _paginatorManager = paginatorManager;
        _userCooldown = userCooldown;
        _criteriaBuilder = criteriaBuilder;
    }

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

        [Summary("包含标签1", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag1 = null,
        [Summary("包含标签2", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag2 = null,
        [Summary("包含标签3", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag3 = null,
        [Summary("包含标签4", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag4 = null,
        [Summary("包含模式", "如何处理上面提供的“包含标签”")]
        [Choice("包含任意一个即可 (OR)", "OR")]
        [Choice("必须包含全部 (AND)", "AND")]
        string includeMethod = "OR",

        [Summary("排除标签1", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag1 = null,
        [Summary("排除标签2", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag2 = null,
        [Summary("排除标签3", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag3 = null,
        [Summary("排除标签4", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag4 = null,
        [Summary("排除模式", "如何处理上面提供的“排除标签”")]
        [Choice("排除任意一个即可 (OR)", "OR")]
        [Choice("必须排除全部 (AND)", "AND")]
        string excludeMethod = "OR",

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
        if (!await _authUtils.InGuild("搜索", Context)) return;
        if (_userCooldown.IsOnCooldown(Context.User.Id))
        {
            await FollowupAsync(embed: EmbedUtils.AuthEmbed("命令尚在一分钟冷却之内, 请稍候再试"));
            return;
        }

        // === 1. 构建查询条件 ===
        var criteria = await _criteriaBuilder.BuildAsync(
            Context,
            forumChannel, author,
            keyWord1, keyWord2, keyWord3,
            exKeyWord1, exKeyWord2, exKeyWord3,
            includeTag1, includeTag2, includeTag3, includeTag4, includeMethod,
            excludeTag1, excludeTag2, excludeTag3, excludeTag4, excludeMethod,
            exAuthor1, exAuthor2, exAuthor3,
            order, startTime, endTime, resultLimit
        );
        _logger.Debug("用户 {Context.User.Username} 的搜索模型构建完毕...", Context.User.Username);

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
        [Summary("包含标签1", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag1 = null,
        [Summary("包含标签2", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag2 = null,
        [Summary("包含标签3", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag3 = null,
        [Summary("包含标签4", "需要包含的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? includeTag4 = null,
        [Summary("包含模式", "如何处理上面提供的“包含标签”")]
        [Choice("包含任意一个即可 (OR)", "OR")]
        [Choice("必须包含全部 (AND)", "AND")]
        string includeMethod = "OR",
        [Summary("排除标签1", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag1 = null,
        [Summary("排除标签2", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag2 = null,
        [Summary("排除标签3", "需要排除的标签")] [Autocomplete(typeof(AllTagsAutoCompletion))] string? excludeTag3 = null,
        // 为了不超过25个参数，在这里去掉第4个排除标签
        // string? excludeTag4 = null, 
        [Summary("排除模式", "如何处理上面提供的“排除标签”")]
        [Choice("排除任意一个即可 (OR)", "OR")]
        [Choice("必须排除全部 (AND)", "AND")]
        string excludeMethod = "OR",
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
        if (!await _authUtils.InGuild("添加快捷搜索", Context)) return;

        var quickSearchCommands = await _databaseService.GetUserQuickSearchesAsync(Context.User.Id);
        if (quickSearchCommands.Count >= 25)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("添加失败", "您的快捷搜索命令已达到25个上限，请先删除一些不用的命令。"), ephemeral: true);
            return;
        }

        string? excludeTag4 = null; // 因为参数限制，快捷搜索中这个值永远是null
        var criteria = await _criteriaBuilder.BuildAsync(
            Context,
            forumChannel, author,
            keyWord1, keyWord2, keyWord3,
            exKeyWord1, exKeyWord2, exKeyWord3,
            includeTag1, includeTag2, includeTag3, includeTag4, includeMethod,
            excludeTag1, excludeTag2, excludeTag3, excludeTag4, excludeMethod,
            exAuthor1, exAuthor2, exAuthor3,
            order, startTime, endTime, resultLimit
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
        if (!await _authUtils.InGuild("快捷搜索", Context)) return;

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
        if (!await _authUtils.InGuild("删除快捷搜索", Context)) return;

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