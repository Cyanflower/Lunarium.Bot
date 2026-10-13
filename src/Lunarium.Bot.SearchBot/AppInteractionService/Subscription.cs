// .NET 核心库
using System.ComponentModel;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using Lunarium.Bot.Logger;

// 项目内命名空间
using Lunarium.Bot.SearchBot.AuthUtils;
using Lunarium.Bot.SearchBot.ApplicationServices;
using Lunarium.Bot.SearchBot.Models;
using Lunarium.Bot.SearchBot.Paginator;
using Lunarium.Bot.SearchBot.Utils;
using Lunarium.Bot.SearchBot.InteractionsCache;
using Discord.WebSocket;

namespace Lunarium.Bot.SearchBot.AppInteractionService;

[Group("关注功能", "关注功能相关命令")]
public class Subscription : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly PaginatorManager _paginatorManager;
    private readonly ApplicationService _applicationService;
    private readonly InteractionsCacheManager _interactionsCacheManager;

    public Subscription(ILogger logger, AuthUtil authUtil, PaginatorManager paginatorManager, ApplicationService applicationService, InteractionsCacheManager interactionsCacheManager)
    {
        _logger = logger.ForContext("Subscription");
        _authUtil = authUtil;
        _paginatorManager = paginatorManager;
        _applicationService = applicationService;
        _interactionsCacheManager = interactionsCacheManager;
    }

    // =============================================
    #region 业务方法
    // =============================================
    public async Task<MessageComponent> ConfirmSubscriptionAsync(ulong userId, ulong authorId)
    {
        // ===== 收藏存在性检查 =====
        var existResult = await _applicationService.CheckSubscriptionExistAsync(userId, authorId);
        switch (existResult.Status)
        {
            case ServiceStatus.NotFound:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Gold)
                        .WithTextDisplay($"## 关注作者", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                        .WithSeparator()
                        .WithTextDisplay($"""
                            是否要关注作者: <@{authorId}> ?
                            """)
                        .WithActionRow(actionRow =>
                        {
                            actionRow.WithButton("确认", $"Sub:{userId}:{authorId}", ButtonStyle.Success, new Emoji("✅"));
                            actionRow.WithButton("取消", $"GeneralCancelV2", ButtonStyle.Danger, new Emoji("❌"));
                        })
                    )
                    .Build();
            case ServiceStatus.Exist:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 关注失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                        .WithSeparator()
                        .WithTextDisplay($"""
                            作者: <@{existResult.Payload?.AuthorId}>
                            关注于: <t:{existResult.Payload?.AddedAt.ToUnixTimeSeconds()}:f>
                            """)
                    )
                    .Build();
            case ServiceStatus.InternalError:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 关注失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 确认关注存在性失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                    errorId, existResult.Status.ToString(), (int)existResult.Status, userId);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 关注失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
        }
    }

    public async Task<MessageComponent> ConfirmUnSubscriptionAsync(ulong userId, ulong authorId)
    {
        var existResult = await _applicationService.CheckSubscriptionExistAsync(userId, authorId);
        switch (existResult.Status)
        {
            case ServiceStatus.NotFound:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 取消关注失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            case ServiceStatus.Exist:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Gold)
                        .WithTextDisplay($"## 取消关注作者", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                        .WithSeparator()
                        .WithTextDisplay($"""
                            是否要取消关注作者: <@{authorId}> ?
                            """)
                        .WithActionRow(actionRow =>
                        {
                            actionRow.WithButton("确认", $"UnSub:{userId}:{authorId}", ButtonStyle.Success, new Emoji("✅"));
                            actionRow.WithButton("取消", $"GeneralCancelV2", ButtonStyle.Danger, new Emoji("❌"));
                        })
                    )
                    .Build();
            case ServiceStatus.InternalError:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 取消关注失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 确认关注存在性失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                    errorId, existResult.Status.ToString(), (int)existResult.Status, userId);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 取消关注失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
        }
    }
    #endregion

    // =============================================
    #region  交互回调
    // =============================================

    [ComponentInteraction("Sub:*:*")]
    public async Task HandleSubscriptionAsync(ulong userId, ulong authorId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Sub:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            await DeferAsync(ephemeral: true);

            try
            {
                var result = await _applicationService.AddSubscriptionAsync(userId, authorId);
                switch (result.Status)
                {
                    case ServiceStatus.Success:
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Green)
                                .WithTextDisplay($"## 关注成功", 1000)
                                .WithTextDisplay(result.Message, 1001)
                            )
                            .Build());
                        break;
                    case ServiceStatus.Conflict:
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Orange)
                                .WithTextDisplay($"## 关注失败", 1000)
                                .WithTextDisplay(result.Message, 1001)
                            )
                            .Build());
                        break;
                    case ServiceStatus.InternalError:
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"## 关注失败", 1000)
                                .WithTextDisplay(result.Message, 1001)
                            )
                            .Build());
                        break;
                    default:
                        string errorId = CorrelationIdGenerator.Generate();
                        _logger.Error("{errorId}: 关注失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}, 作者ID: {AuthorId}",
                            errorId, result.Status.ToString(), (int)result.Status, userId, authorId);
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"## 关注失败", 1000)
                                .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                            )
                            .Build());
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                string errorId = CorrelationIdGenerator.Generate();

                _logger.Error(ex, "{errorId}: 关注失败, 用户ID: {UserId}, 作者ID: {AuthorId}",
                    errorId, userId, authorId);

                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 关注失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                await interactionComponent.FollowupAsync(components: component);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleSubscriptionAsync Exception");
        }
    }

    [ComponentInteraction("UnSub:*:*")]
    public async Task HandleUnSubscriptionAsync(ulong userId, ulong authorId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [UnSub:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            await DeferAsync(ephemeral: true);

            try
            {
                var result = await _applicationService.RemoveSubscriptionAsync(userId, authorId);
                switch (result.Status)
                {
                    case ServiceStatus.Success:
                        await interactionComponent.Message.ModifyAsync( msg => msg.Components = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Green)
                                .WithTextDisplay($"## 取消关注成功", 1000)
                                .WithTextDisplay(result.Message, 1001)
                            )
                            .Build());
                        break;
                    case ServiceStatus.NotFound:
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Orange)
                                .WithTextDisplay($"## 取消关注失败", 1000)
                                .WithTextDisplay(result.Message, 1001)
                            )
                            .Build());
                        break;
                    case ServiceStatus.InternalError:
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"## 取消关注失败", 1000)
                                .WithTextDisplay(result.Message, 1001)
                            )
                            .Build());
                        break;
                    default:
                        string errorId = CorrelationIdGenerator.Generate();
                        _logger.Error("{errorId}: 取消关注失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}, 作者ID: {AuthorId}",
                            errorId, result.Status.ToString(), (int)result.Status, userId, authorId);
                        await interactionComponent.FollowupAsync(components: new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"## 取消关注失败", 1000)
                                .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                            )
                            .Build());
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                string errorId = CorrelationIdGenerator.Generate();

                _logger.Error(ex, "{errorId}: 取消关注失败, 用户ID: {UserId}, 作者ID: {AuthorId}",
                    errorId, userId, authorId);

                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 取消关注失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                await interactionComponent.FollowupAsync(components: component);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleUnSubscriptionAsync Exception");
        }
    }
    #endregion

    // =============================================
    #region  AppMenu
    // =============================================
    [MessageCommand("关注该作者")]
    public async Task MessageMenuSubscriptionfAsync(IMessage message)
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

            var component = await ConfirmSubscriptionAsync(Context.User.Id, message.Author.Id);
            await FollowupAsync(components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MessageMenuSubscriptionfAsync Ex");
        }
    }

    [MessageCommand("取消关注该作者")]
    public async Task MessageMenuUnSubscriptionfAsync(IMessage message)
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

            var component = await ConfirmUnSubscriptionAsync(Context.User.Id, message.Author.Id);
                await FollowupAsync(components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MessageMenuUnSubscriptionfAsync Ex");
        }
    }

    [UserCommand("关注该作者")]
    public async Task UserMenuSubscriptionAsync(IUser user)
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

            var component = await ConfirmSubscriptionAsync(Context.User.Id, user.Id);
                await FollowupAsync(components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UserMenuSubscriptionAsync Ex");
        }
    }

    [UserCommand("取消关注该作者")]
    public async Task UserMenuUnSubscriptionAsync(IUser user)
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

            var component = await ConfirmUnSubscriptionAsync(Context.User.Id, user.Id);
                await FollowupAsync(components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UserMenuUnSubscriptionAsync Ex");
        }
    }
    #endregion

    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("关注本帖作者", "将本帖作者添加到关注列表")]
    public async Task SubscriptionAsync()
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
                var component = await ConfirmSubscriptionAsync(Context.User.Id, threadChannel.OwnerId);
                await FollowupAsync(components: component, ephemeral: true);
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
            _logger.Error(ex, "SubscriptionAsync Ex");
        }
    }

    [SlashCommand("取消关注本帖作者", "将本帖作者从关注列表移除")]
    public async Task UnSubscriptionAsync()
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
                var component = await ConfirmUnSubscriptionAsync(Context.User.Id, threadChannel.OwnerId);
                await FollowupAsync(components: component, ephemeral: true);
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
            _logger.Error(ex, "UnSubscriptionAsync Ex");
        }
    }

    [SlashCommand("查看关注列表", "查看自己的关注列表")]
    public async Task ViewSubscriptionAsync()
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

            var result = await _applicationService.QueryUserSubscriptionsAsync(Context.User.Id);
            switch (result.Status)
            {
                case ServiceStatus.Success:
                    var view = _paginatorManager.RegisterView<SubscriptionData>(Context.User.Id, result.Payload!);
                    await FollowupAsync(components: view.Render(), ephemeral: true);
                    return;
                case ServiceStatus.NotFound:
                    var notFoundComponents = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("""
                            ## 查看关注列表
                            您当前的关注列表还没有任何内容, 快去关注自己喜欢的作者吧!
                            """)
                        )
                        .Build();
                    await FollowupAsync(components: notFoundComponents, ephemeral: true);
                    return;
                case ServiceStatus.InternalError:
                    var internalErrorComponent = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay($"## 查看关注列表失败", 1000)
                            .WithTextDisplay(result.Message, 1001)
                        )
                        .Build();
                    await FollowupAsync(components: internalErrorComponent, ephemeral: true);
                    return;
                default:
                    string errorId = CorrelationIdGenerator.Generate();
                    _logger.Error("{errorId}: 查看关注列表失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                        errorId, result.Status.ToString(), (int)result.Status, Context.User.Id);
                    var defaultComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 查看关注列表失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                    await FollowupAsync(components: defaultComponent, ephemeral: true);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ViewSubscriptionAsync Ex");
        }
    }
    #endregion
}