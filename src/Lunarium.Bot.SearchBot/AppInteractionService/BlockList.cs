// NuGet 包依赖
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// 解决方案内引用项目
using Lunarium.Bot.Logger;

// 项目内命名空间
using Lunarium.Bot.SearchBot.AuthUtils;
using Lunarium.Bot.SearchBot.Utils;
using Lunarium.Bot.SearchBot.Models;
using Lunarium.Bot.SearchBot.Paginator;
using Lunarium.Bot.SearchBot.ApplicationServices;

namespace Lunarium.Bot.SearchBot.AppInteractionService;

[Group("搜索屏蔽用户", "屏蔽功能相关命令")]
public class BlockList : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly PaginatorManager _paginatorManager;
    private readonly ApplicationService _applicationService;

    public BlockList(ILogger logger, AuthUtil authUtil, PaginatorManager paginatorManager, ApplicationService applicationService)
    {
        _logger = logger.ForContext("BlockCommands");
        _authUtil = authUtil;
        _paginatorManager = paginatorManager;
        _applicationService = applicationService;
    }

    // =============================================
    #region 业务方法
    // =============================================
    private MessageComponent GetConfirmBlockAuthorComponent(IUser user, IUser author)
    {
        var componentV2 = new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Gold)
                .WithSection( section => {
                    section.WithTextDisplay($"## 搜索屏蔽名单\n 你确定要将 <@{author.Id}> 添加到搜索时的结果排除名单吗? 之后的搜索结果中将不会包含该用户的帖子", 1001);
                    if (author.GetAvatarUrl() is not null)
                    {
                        section.WithAccessory(new ThumbnailBuilder().WithMedia(author.GetAvatarUrl()));
                    }
                })
                .WithSeparator()
                .WithActionRow(row => row
                    .WithButton("确认", $"Block:{user.Id}:{author.Id}", ButtonStyle.Danger)
                    .WithButton("取消", $"GeneralCancelV2", ButtonStyle.Danger)
                )
            )
            .Build();
        return componentV2;
    }

    private MessageComponent GetConfirmUnBlockAuthorComponent(IUser user, IUser author)
    {
        var componentV2 = new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Gold)
                .WithSection( section => {
                    section.WithTextDisplay($"## 移除搜索屏蔽\n 你确定要将 <@{author.Id}> 从搜索屏蔽名单中移除吗? \n\n**移除后，该用户的帖子将重新出现在你的搜索结果中。**", 1001);
                    if (author.GetAvatarUrl() is not null)
                    {
                        section.WithAccessory(new ThumbnailBuilder().WithMedia(author.GetAvatarUrl()));
                    }
                })
                .WithSeparator()
                .WithActionRow(row => row
                    // ID格式: UnBlock:操作者ID:目标ID
                    .WithButton("确认移除", $"UnBlock:{user.Id}:{author.Id}", ButtonStyle.Success) // 使用绿色按钮表示"恢复/通过"
                    .WithButton("取消", $"GeneralCancelV2", ButtonStyle.Secondary)
                )
            )
            .Build();
        // 发送 ephemeral (仅自己可见) 的交互消息
        return componentV2;
    }
    
    private async Task<(MessageComponent component, ServiceResult result)> BlockAuthorAsync(ulong userId, ulong authorId)
    {
        var result = await _applicationService.AddBlockedAuthorServiceAsync(userId, authorId);
        switch (result.Status)
        {
            case ServiceStatus.Success:
                var successComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 搜索屏蔽名单添加成功", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (successComponent, result );
            case ServiceStatus.InternalError:
                var internalErrorComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (internalErrorComponent, result);
            case ServiceStatus.InvalidInput:
                var invalidInputComponent = new ComponentBuilderV2()
                    .WithContainer(container=> container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (invalidInputComponent, result);
            case ServiceStatus.Conflict:
                var conflictComponent = new ComponentBuilderV2()
                    .WithContainer(container=> container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (conflictComponent, result);
            default:
                string errorId = CorrelationIdGenerator.Generate();

                _logger.Error("{errorId}: 搜索屏蔽名单添加失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}, 目标ID: {AuthorId}",
                    errorId, result.Status.ToString(), (int)result.Status, userId, authorId);

                var defaultComponent = new ComponentBuilderV2()
                    .WithContainer(container=> container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                return (defaultComponent, result);
        }
    }
    
    private async Task<(MessageComponent component, ServiceResult result)> UnBlockAuthorAsync(ulong userId, ulong authorId)
    {
        var result = await _applicationService.RemoveBlockedAuthorServiceAsync(userId, authorId);
        switch (result.Status)
        {
            case ServiceStatus.Success:
                var successComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 搜索屏蔽名单移除成功", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (successComponent, result);
            case ServiceStatus.InternalError:
                var internalErrorComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay(result.Message, 1001)
                    )
                    .Build();
                return (internalErrorComponent, result);
            case ServiceStatus.NotFound:
                var invalidInputComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
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
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                return (defaultComponent, result);
        }
    }
    #endregion

    // =============================================
    #region  按钮回调
    // =============================================
    [ComponentInteraction("Block:*:*")]
    public async Task HandleConfirmBlockAsync(ulong userId, ulong authorId)
    {
        try
        {
            await DeferAsync(ephemeral: true);
            (var component, var result) = await BlockAuthorAsync(userId, authorId);
            if (Context.Interaction is SocketMessageComponent interactionComponent)
            {
                await interactionComponent.UpdateAsync(msg =>
                {
                    msg.Components = component;
                });
                if (result.Status == ServiceStatus.Conflict)
                {
                    var dateTimeOffset = await _applicationService.QueryBlockedAuthorInfoServiceAsync(userId, authorId);
                    if (dateTimeOffset is not null)
                    {
                        var InfoComponent = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                                .WithTextDisplay($"<@{authorId}>在此前已经位于您的搜索排除名单中了\n> 添加时间: <t:{dateTimeOffset.Value.ToUnixTimeSeconds()}:f>", 1001)
                            )
                            .Build();
                        await interactionComponent.UpdateAsync(msg =>
                        {
                            msg.Components = InfoComponent;
                        });
                    }
                    else
                    {
                        var InfoComponent = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"## 搜索屏蔽名单添加失败", 1000)
                                .WithTextDisplay($"<@{authorId}>在此前已经位于您的搜索排除名单中了\n> 但未能查询到详细信息, 请尝试联系管理组", 1001)
                            )
                            .Build();
                        await interactionComponent.UpdateAsync(msg =>
                        {
                            msg.Components = InfoComponent;
                        });
                    }
                }
            }
            else
            {
                _logger.Error("HandleConfirmBlockAsync is not SocketMessageComponent");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleConfirmBlockAsync Exception");
        }
    }
    
    [ComponentInteraction("UnBlock:*:*")]
    public async Task HandleConfirmUnBlockAsync(ulong userId, ulong authorId)
    {
        try
        {
            await DeferAsync(ephemeral: true);
            var result = await UnBlockAuthorAsync(userId, authorId);
            if (Context.Interaction is SocketMessageComponent interactionComponent)
            {
                await interactionComponent.UpdateAsync(msg =>
                {
                    msg.Components = result.component;
                });
            }
            else
            {
                _logger.Error("HandleConfirmUnBlockAsync is not SocketMessageComponent");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleConfirmUnBlockAsync Exception");
        }
    }
    
    [ComponentInteraction("UnBlockByList:*:*")]
    public async Task HandleConfirmUnBlockAsync(string key, string index)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [UnBlockByList:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }
            var view = _paginatorManager.GetView<BlockPaginatedView>(key);

            if (view == null)
            {
                var nullVicwComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 该交互已过期\n请使用命令发起新的交互")
                    )
                    .Build();
                await RespondAsync(components: nullVicwComponent, ephemeral: true);
                return;
            }


            if (int.TryParse(index, out var indexNum)) { }
            else
            {
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("搜索屏蔽名单移除失败, index 无法解析为 indexNum: 用户ID: {UserId}, 原始参数: key:{key}; index:{index}; 错误ID: {errorId}",
                    Context.User.Id, key, index, errorId);
                var indexCantParseComponent = new ComponentBuilderV2()
                    .WithContainer(container=> container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                await RespondAsync(components: indexCantParseComponent, ephemeral: true);
                return;
            }


            var targetItem = view.GetItemByIndex(indexNum);

            if (targetItem is null)
            {
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("搜索屏蔽名单移除失败, view.GetItemByIndex(indexNum) 发生数组越界: 用户ID: {UserId}, 原始参数: key:{key}; index:{index}; 错误ID: {errorId}",
                    Context.User.Id, key, index, errorId);
                var arrayBoundsComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 搜索屏蔽名单移除失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                view.LogViewDetail(_logger);
                await RespondAsync(components: arrayBoundsComponent, ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var(component, result) = await UnBlockAuthorAsync(Context.User.Id, targetItem.BlockedAuthorId);

            if (result.Status == ServiceStatus.Success)
            {
                view.RemoveItem(indexNum);
                await interactionComponent.UpdateAsync(msg =>
                {
                    msg.Components = view.Render();
                });
            }
            else
            {
                await FollowupAsync(components: component, ephemeral: true);
            }


        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleConfirmUnBlockAsync(ByList) Exception");
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
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }

            if (message is not null && message.Author is not null)
            {
                var components = GetConfirmBlockAuthorComponent(Context.User, message.Author);
                await RespondAsync(components: components, ephemeral: true);
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        无法获取到指定用户的信息。
                        """)
                    )
                    .Build();
                await RespondAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MessageMenuBlockUserAsync Exception");
        }
    }
    
    [MessageCommand("取消搜索屏蔽该作者")]
    public async Task MessageMenuUnBlockUserAsync(IMessage message)
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
            
            if(message is not null && message.Author is not null)
            {
                var components = GetConfirmUnBlockAuthorComponent(Context.User, message.Author);
                await RespondAsync(components: components, ephemeral: true);
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        无法获取到指定用户的信息。
                        """)
                    )
                    .Build();
                await FollowupAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MessageMenuUnBlockUserAsync Exception");
        }
    }

    // --- User Commands ---

    [UserCommand("搜索屏蔽该作者")]
    public async Task UserMenuBlockUserAsync(IUser user)
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            if(user is not null)
            {
                var components = GetConfirmBlockAuthorComponent(Context.User, user);
                await RespondAsync(components: components, ephemeral: true);
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        无法获取到指定用户的信息。
                        """)
                    )
                    .Build();
                await RespondAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UserMenuBlockUserAsync Exception");
        }
    }

    [UserCommand("取消搜索屏蔽该作者")]
    public async Task UserMenuUnBlockUserAsync(IUser user)
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            // 直接把右键点击的用户对象作为参数传进去
            if(user is not null)
            {
                var components = GetConfirmUnBlockAuthorComponent(Context.User, user);
                await RespondAsync(components: components, ephemeral: true);
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        无法获取到指定用户的信息。
                        """)
                    )
                    .Build();
                await RespondAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UserMenuUnBlockUserAsync Exception");
        }
    }
    #endregion


    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("屏蔽本帖作者", "在搜索结果中屏蔽当前帖子的作者")]
    public async Task BlockCurrentThreadAuthor()
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            await DeferAsync(ephemeral: true);
            
            if (Context.Channel is IThreadChannel threadChannel)
            {
                var owner = await threadChannel.GetUserAsync(threadChannel.OwnerId);
                if(owner is not null)
                {
                    var components = GetConfirmBlockAuthorComponent(Context.User, owner);
                    await FollowupAsync(components: components, ephemeral: true);
                }
                else
                {
                    var components = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("""
                            ## 操作失败
                            无法获取到该帖子的作者信息。
                            """)
                        )
                        .Build();
                    await FollowupAsync(components: components, ephemeral: true);
                }
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        **xxxx本帖** 系列命令仅可在论坛频道的帖子内使用
                        """)
                    )
                    .Build();
                await FollowupAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "BlockCurrentThreadAuthor Exception");
        }
    }

    [SlashCommand("屏蔽指定作者", "在搜索结果中屏蔽一位指定的用户")]
    public async Task BlockSpecifiedAuthor([Summary("用户", "要屏蔽的用户")] IUser user)
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            if(user is not null)
            {
                var components = GetConfirmBlockAuthorComponent(Context.User, user);
                await RespondAsync(components: components, ephemeral: true);
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        无法获取到指定用户的信息。
                        """)
                    )
                    .Build();
                await RespondAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "BlockSpecifiedAuthor Exception");
        }
    }

    [SlashCommand("取消屏蔽本帖作者", "将当前帖子的作者从你的屏蔽列表中移除")]
    public async Task UnblockCurrentThreadAuthor()
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            await DeferAsync(ephemeral: true);
    
            if (Context.Channel is IThreadChannel threadChannel)
            {
                var owner = await threadChannel.GetUserAsync(threadChannel.OwnerId);
                if (owner is not null)
                {
                    var components = GetConfirmUnBlockAuthorComponent(Context.User, owner);
                    await FollowupAsync(components: components, ephemeral: true);
                }
                else
                {
                    var components = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("""
                            ## 操作失败
                            无法获取到该帖子的作者信息。
                            """)
                        )
                        .Build();
                    await FollowupAsync(components: components, ephemeral: true);
                }
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        **xxxx本帖** 系列命令仅可在论坛频道的帖子内使用
                        """)
                    )
                    .Build();
                await FollowupAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UnblockCurrentThreadAuthor Exception");
        }
    }

    [SlashCommand("取消屏蔽指定作者", "将指定的用户从你的屏蔽列表中移除")]
    public async Task UnblockSpecifiedAuthor([Summary("用户", "要取消屏蔽的用户")] IUser user)
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            if(user is not null)
            {
                var components = GetConfirmUnBlockAuthorComponent(Context.User, user);
                await RespondAsync(components: components, ephemeral: true);
            }
            else
            {
                var components = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("""
                        ## 操作失败
                        无法获取到该作者信息。
                        """)
                    )
                    .Build();
                await RespondAsync(components: components, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UnblockSpecifiedAuthor Exception");
        }
    }
    
    [SlashCommand("查看屏蔽名单", "查看你当前屏蔽的所有作者")]
    public async Task ViewBlockedAuthors()
    {
        try
        {
            // 1. 定义此命令所需的权限条件
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
            // 2. 进行鉴权
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            // 3. 检查鉴权结果，如果失败则提前返回
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                // 以 "仅自己可见" 的方式回复失败原因
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return; // 终止命令执行
            }
    
            await DeferAsync(ephemeral: true);
    
            var result = await _applicationService.QueryUserAllBlockedInfoServiceAsync(Context.User.Id);
    
            try
            {
                if (result.Count > 0)
                {
                    var view = _paginatorManager.RegisterView<BlockedInfo>(Context.User.Id, result);
                    await FollowupAsync(components: view.Render(), ephemeral: true);
                }
                else
                {
                    var components = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("""
                            ## 查看屏蔽名单
                            您当前的屏蔽名单还没有任何内容
                            """)
                        )
                        .Build();
                    await FollowupAsync(components: components, ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ViewBlockedAuthors Exception");
        }
    }
    #endregion
}