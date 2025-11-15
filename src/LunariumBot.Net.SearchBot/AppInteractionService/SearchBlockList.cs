// NuGet 包依赖
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Utils;
using LunariumBot.Net.SearchBot.Model;
using LunariumBot.Net.SearchBot.ApplicationServices;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

[Group("搜索屏蔽用户", "屏蔽功能相关命令")]
public class SearchBlockList : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;

    private readonly ApplicationService _applicationService;

    public SearchBlockList(ILogger logger, AuthUtils authUtils, DatabaseService databaseService, ApplicationService applicationService)
    {
        _logger = logger.ForContext("BlockCommands");
        _authUtils = authUtils;
        _databaseService = databaseService;
        _applicationService = applicationService;
    }

    #region 业务方法
    private async Task ConfirmBlockAuthorAsync(IUser user, IUser author)
    {
        var componentV2 = new ComponentBuilderV2()
            .WithContainer(c => c
                .WithAccentColor(Color.Gold)
                .WithTextDisplay($"## 搜索屏蔽名单", 1000)
                .WithTextDisplay($"## 搜索屏蔽名单\n 你确定要将 <@{author.Id}> 添加到搜索时的结果排除名单吗? 之后的搜索结果中将不会包含该用户的帖子", 1001)
                .WithSeparator()
                .WithActionRow(row => row
                    .WithButton("确认", $"Block:{user.Id}>>{author.Id}", ButtonStyle.Danger)
                    .WithButton("取消", $"ComponentV2GeneralCancel", ButtonStyle.Danger)
                )
            )
            .Build();
        await FollowupAsync(components: componentV2, ephemeral: true);
    }
    private async Task<(MessageComponent component, ServiceResult result)> BlockAuthorAsync(ulong userId, ulong authorId)
    {
        await DeferAsync(ephemeral:true);
        var result = await _applicationService.AddBlockedAuthorServiceAsync(userId, authorId);
        switch (result.Status)
        {
            case ServiceStatus.Success:
                var successComponent = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 搜索屏蔽名单添加成功", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return ( successComponent, result );
            case ServiceStatus.InternalError:
                var internalErrorComponent = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return ( internalErrorComponent, result );
            case ServiceStatus.InvalidInput:
                var invalidInputComponent = new ComponentBuilderV2()
                    .WithContainer(c=> c
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return ( invalidInputComponent, result );
            case ServiceStatus.Conflict:
                var conflictComponent = new ComponentBuilderV2()
                    .WithContainer(c=> c
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return ( conflictComponent, result );
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 搜索屏蔽名单添加失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}, 目标ID: {AuthorId}",
                    errorId, result.Status.ToString(), (int)result.Status, userId, authorId);
                var defaultComponent = new ComponentBuilderV2()
                    .WithContainer(c=> c
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                return ( defaultComponent, result );
        }
    }
    private async Task<(MessageComponent component, ServiceResult result)> UnBlockAuthorAsync(ulong userId, ulong authorId)
    {
        await DeferAsync(ephemeral: true);
        var result = await _applicationService.RemoveBlockedAuthorServiceAsync(userId, authorId);
        switch (result.Status)
        {
            case ServiceStatus.Success:
                var successComponent = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 搜索屏蔽名单移除成功", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (successComponent, result);
            case ServiceStatus.InternalError:
                var internalErrorComponent = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (internalErrorComponent, result);
            case ServiceStatus.NotFound:
                var invalidInputComponent = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (invalidInputComponent, result);
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 搜索屏蔽名单移除失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}, 目标ID: {AuthorId}",
                    errorId, result.Status.ToString(), (int)result.Status, userId, authorId);
                var defaultComponent = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                return (defaultComponent, result);
        }
    }
    #endregion
    
    // =============================================
    #region  AppMenu
    // =============================================
    // --- Message Commands ---
    [MessageCommand("搜索屏蔽该作者")]
    public async Task MessageMenuBlockUserAsync(IMessage message)
    {
        await _searchCommands.SearchAsync(author: Context.User);
    }
    [MessageCommand("取消搜索屏蔽该作者")]
    public async Task MessageMenuUnBlockUserAsync(IMessage message)
    {
        await _searchCommands.SearchAsync(author: Context.User);
    }

    // --- User Commands ---

    [UserCommand("搜索屏蔽该作者")]
    public async Task UserMenuBlockUserAsync(IUser user)
    {
        // 直接把右键点击的用户对象作为参数传进去
        await _searchCommands.SearchAsync(author: user);
    }

    [UserCommand("取消搜索屏蔽该作者")]
    public async Task UserMenuUnBlockUserAsync(IUser user)
    {
        // 直接把右键点击的用户对象作为参数传进去
        await _searchCommands.SearchAsync(author: user);
    }
    #endregion

    // =============================================
    #region  按钮响应
    // =============================================
    [ComponentInteraction("Block:*>>*")]
    public async Task ConfirmBlockButtonCallbackAsync(ulong userId, ulong authorId)
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
            (var component, var result) = await BlockAuthorAsync(userId, authorId);
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await interaction.UpdateAsync(porp =>
                {
                    porp.Components = component;
                });
                if (result.Status == ServiceStatus.Conflict)
                {
                    var dateTimeOffset = await _applicationService.QueryBlockedAuthorInfoServiceAsync(userId, authorId);
                    if (dateTimeOffset is not null)
                    {
                        var InfoComponent = new ComponentBuilderV2()
                            .WithContainer(c => c
                                .WithAccentColor(Color.Green)
                                .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                                .WithTextDisplay($"<@{authorId}>在此前已经位于您的搜索排除名单中了\n> 添加时间: <t:{dateTimeOffset.Value.ToUnixTimeSeconds()}:f>", 1001)
                            )
                            .Build();
                        await interaction.UpdateAsync(porp =>
                        {
                            porp.Components = InfoComponent;
                        });
                    }
                    else
                    {
                        var InfoComponent = new ComponentBuilderV2()
                            .WithContainer(c => c
                                .WithAccentColor(Color.Green)
                                .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                                .WithTextDisplay($"<@{authorId}>在此前已经位于您的搜索排除名单中了\n> 但未能查询到详细信息, 请尝试联系管理组", 1001)
                            )
                            .Build();
                        await interaction.UpdateAsync(porp =>
                        {
                            porp.Components = InfoComponent;
                        });
                    }
                }
            }
            else
            {
                _logger.Error("ConfirmBlockButtonCallbackAsync is not SocketMessageComponent");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ConfirmBlockButtonCallbackAsync Exception");
        }
    }
    #endregion

    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("屏蔽本帖作者", "在搜索结果中屏蔽当前帖子的作者")]
    public async Task BlockCurrentThreadAuthor()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InScanChannelThread("屏蔽本帖作者", Context)) return;

        if (Context.Channel is IThreadChannel threadChannel)
        {
            var owner = await threadChannel.GetUserAsync(threadChannel.OwnerId);
            if(owner != null)
            {
                await ConfirmBlockAuthorAsync(Context.User, owner);
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "无法获取到该帖子的作者信息。"), ephemeral: true);
            }
        }
    }

    [SlashCommand("屏蔽指定作者", "在搜索结果中屏蔽一位指定的用户")]
    public async Task BlockSpecifiedAuthor([Summary("用户", "要屏蔽的用户")] IUser user)
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("屏蔽指定作者", Context)) return;
        
        await BlockAuthorAsync(user);
    }

    [SlashCommand("取消屏蔽本帖作者", "将当前帖子的作者从你的屏蔽列表中移除")]
    public async Task UnblockCurrentThreadAuthor()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InScanChannelThread("取消屏蔽本帖作者", Context)) return;

        if (Context.Channel is IThreadChannel threadChannel)
        {
            var owner = await threadChannel.GetUserAsync(threadChannel.OwnerId);
            if(owner != null)
            {
                await UnblockAuthorAsync(owner);
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "无法获取到该帖子的作者信息。"), ephemeral: true);
            }
        }
    }

    [SlashCommand("取消屏蔽指定作者", "将指定的用户从你的屏蔽列表中移除")]
    public async Task UnblockSpecifiedAuthor([Summary("用户", "要取消屏蔽的用户")] IUser user)
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("取消屏蔽指定作者", Context)) return;
        
        await UnblockAuthorAsync(user);
    }
    
    [SlashCommand("查看屏蔽名单", "查看你当前屏蔽的所有作者")]
    public async Task ViewBlockedAuthors()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("查看屏蔽名单", Context)) return;

        var blockedIds = await _databaseService.GetBlockedAuthorsAsync(Context.User.Id);

        if (!blockedIds.Any())
        {
            await FollowupAsync(embed: EmbedUtils.InfoEmbed("你的屏蔽列表是空的", "你没有屏蔽任何作者哦。"), ephemeral: true);
            return;
        }

        const int maxDescriptionLength = 4000; // Discord Embed 描述字段的最大长度略小于 4096，我们保守一点
        var lines = blockedIds.Select(id => $"- <@{id}>").ToList();

        var pages = new List<string>();
        var currentPageBuilder = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            // 检查加上新的一行后是否会超长
            if (currentPageBuilder.Length + line.Length + 1 > maxDescriptionLength)
            {
                pages.Add(currentPageBuilder.ToString());
                currentPageBuilder.Clear();
            }
            currentPageBuilder.AppendLine(line);
        }
        // 添加最后一页
        pages.Add(currentPageBuilder.ToString());

        // --- 发送分页消息 ---
        var firstPage = true;
        for (int i = 0; i < pages.Count; i++)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"📝 你的屏蔽作者名单 (第 {i + 1}/{pages.Count} 页)")
                .WithDescription(pages[i])
                .WithColor(Color.Blue)
                .WithFooter($"共屏蔽了 {blockedIds.Count} 位作者")
                .Build();

            if (firstPage)
            {
                await FollowupAsync(embed: embed, ephemeral: true);
                firstPage = false;
            }
            else
            {
                // 对于后续页，我们需要使用 FollowupAsync 的另一个重载
                // 注意：因为原始交互是 ephemeral 的，所以后续的 Followup 也必须是 ephemeral
                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
        }
    }
    #endregion
}