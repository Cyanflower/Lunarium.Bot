using Microsoft.Extensions.Logging;
using System.Text.Json;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.Scheduler;
using SearchBot.Model;
using SearchBot.Utils;
using LunariumExtensionMethod;

namespace SearchBot.AppInteractionService;

// 本项目 customId 命名约定:
// {操作?}_{功能类别?}_*_*...
// 操作类型 + 功能类别 + 通配符传参
// 如: add_F_{user id}_{target favorite}

// 功能列表:
// F: 收藏夹功能(favorite)
// S: 搜索功能(search)
// P: 分页功能(paginator)
// ST: 订阅帖子功能(sub thread)
// SA: 订阅作者功能(sub author)

// === --- === 交互按钮 === --- ===
public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<Buttons> _logger;
    private readonly DatabaseService _databaseService;
    private readonly PaginatorManager _paginatorManager;
    private readonly UserCooldown _userCooldown;
    private readonly SearchCriteriaBuilder _criteriaBuilder;

    public Buttons(
        ILogger<Buttons> logger,
        DatabaseService databaseService,
        PaginatorManager paginatorManager,
        UserCooldown userCooldown,
        SearchCriteriaBuilder criteriaBuilder
    )
    {
        _userCooldown = userCooldown;
        _criteriaBuilder = criteriaBuilder;
        _logger = logger;
        _databaseService = databaseService;
        _paginatorManager = paginatorManager;
    }

    /// <summary>
    /// 通用取消按钮回调
    /// </summary>
    /// <returns></returns>
    [ComponentInteraction("general_cancel")]
    public async Task GeneralCancelButton()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                var oldEmbed = interaction.Message.Embeds.FirstOrDefault();
                var cancelledEmbed = new EmbedBuilder();

                if (oldEmbed != null)
                {
                    cancelledEmbed
                        .WithTitle("操作已取消")
                        .WithDescription(oldEmbed.Description)
                        .WithColor(Color.Red);
                }
                else
                {
                    cancelledEmbed
                        .WithTitle("操作已取消")
                        .WithColor(Color.Red);
                }
                var embed = cancelledEmbed.Build();

                await interaction.UpdateAsync(props =>
                {
                    props.Embed = embed;
                    props.Components = new ComponentBuilder().Build();
                });
            }
            else
            {
                // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
                // 可以在这里处理错误，虽然理论上不应该发生。
                await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("GeneralCancelButton {ex}", ex);
        }
    }

    [ComponentInteraction("direct_search")]
    public async Task DirectSearch()
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);
                if (_userCooldown.IsOnCooldown(interaction.User.Id))
                {
                    await FollowupAsync(embed: EmbedUtils.AuthEmbed("命令尚在一分钟冷却之内, 请稍候再试"));
                    return;
                }
                _logger.LogInformation("用户 {User} 正在执行直接搜索...", interaction.User.Username);
                var criteria = await _criteriaBuilder.BuildAsync(
                    null, null,
                    null, null, null,
                    null, null, null,
                    null, null, null,
                    null, null, null, null,
                    null, null, null,
                    null, null, null,
                    "posted_date_asc", null, null, 200
                );
                _logger.LogDebug("用户 {User} 的搜索模型构建完毕...", interaction.User.Username);

                await SearchUtils.ExecuteAndDisplaySearchAsync(_logger, _databaseService, _paginatorManager, criteria, interaction);

            }
            else
            {
                // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
                // 可以在这里处理错误，虽然理论上不应该发生。
                await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("DirectSearch {ex}", ex);
        }
    }

    [ComponentInteraction("view_favorite")]
    public async Task ViewFavorite()
    {
        try
        {
            string? favorite = null;
            string? sort = "new";
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                // === 查询收藏 ===
                var queryResult = await _databaseService.QueryUserFavoriteAsync(interaction.User.Id, favorite);
                if (queryResult == null || !queryResult.Any()) // 检查是否为空或无元素
                {
                    var emptyEmbed = new EmbedBuilder()
                        .WithTitle("空空如也")
                        .WithDescription(favorite == null ? "您的收藏夹里还没有任何帖子哦。" : $"在 **{favorite}** 收藏夹里没有找到任何帖子。")
                        .WithColor(Color.Blue)
                        .Build();
                    await FollowupAsync(embed: emptyEmbed, ephemeral: true);
                    _logger.LogInformation("用户 {UserId} 查询收藏夹 '{favorite}' 结果为空", interaction.User.Id, favorite ?? "未分类");
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
                var uuid = $"{interaction.User.Id}_{currentTime}_favorite"; // 确保uuid的唯一性
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
            else
            {
                // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
                // 可以在这里处理错误，虽然理论上不应该发生。
                await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("DirectSearch {ex}", ex);
        }
    }

    [ComponentInteraction("view_subthread")]
    public async Task ViewSubthread()
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);
                string? sort = "new";
                // === 1. 查询订阅列表 ===
                var queryResult = await _databaseService.QueryUserSubscriptionsAsync(interaction.User.Id);
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
                var uuid = $"{interaction.User.Id}_{currentTime}_subscriptions";
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
            else
            {
                // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
                // 可以在这里处理错误，虽然理论上不应该发生。
                await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("DirectSearch {ex}", ex);
        }
    }

    [ComponentInteraction("view_pushlist")]
    public async Task ViewPushlist()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);

                // === 1. 查询推送列表 ===
                var queryResult = await _databaseService.QueryUserPushListAsync(interaction.User.Id);
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
                var uuid = $"{interaction.User.Id}_{currentTime}_updates";
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
            else
            {
                // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
                // 可以在这里处理错误，虽然理论上不应该发生。
                await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("GeneralCancelButton {ex}", ex);
        }
    }
    
    [ComponentInteraction("quick_search")]
    public async Task QuickSearch()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);

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
            else
            {
                // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
                // 可以在这里处理错误，虽然理论上不应该发生。
                await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("GeneralCancelButton {ex}", ex);
        }
    }
}
