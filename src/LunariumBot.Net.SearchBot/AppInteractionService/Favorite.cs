// .NET 核心库
using System.ComponentModel;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.AuthUtils;
using LunariumBot.Net.SearchBot.ApplicationServices;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Paginator;
using LunariumBot.Net.SearchBot.Utils;
using LunariumBot.Net.SearchBot.InteractionsCache;
using Discord.WebSocket;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

[Group("收藏功能", "收藏功能相关命令")]
public class Favorite : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly PaginatorManager _paginatorManager;
    private readonly ApplicationService _applicationService;
    private readonly InteractionsCacheManager _interactionsCacheManager;

    public Favorite(ILogger logger, AuthUtil authUtil, PaginatorManager paginatorManager, ApplicationService applicationService, InteractionsCacheManager interactionsCacheManager)
    {
        _logger = logger.ForContext("Favorite");
        _authUtil = authUtil;
        _paginatorManager = paginatorManager;
        _applicationService = applicationService;
        _interactionsCacheManager = interactionsCacheManager;
    }

    // =============================================
    #region 业务方法
    // =============================================
    public async Task<MessageComponent> ConfirmFavoriteAsync(IUser user, IThreadChannel threadChannel)
    {
        // ===== 收藏存在性检查 =====
        var existResult = await _applicationService.CheckFavoriteExistServiceAsync(user.Id, threadChannel.Id);
        switch (existResult.Status)
        {
            case ServiceStatus.NotFound:
                break;
            case ServiceStatus.Exist:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 收藏失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                        .WithSeparator()
                        .WithTextDisplay($"""
                            帖子: <#{existResult.Payload?.ThreadId}>
                            收藏夹: {existResult.Payload?.FavoriteTypeName}
                            允许推送: {(existResult.Payload is not null && existResult.Payload.AllowPush == true ? "允许推送" : "禁止推送")}
                            收藏于: <t:{existResult.Payload?.AddedAt.ToUnixTimeSeconds()}:f>
                            """)
                    )
                    .Build();
            case ServiceStatus.InternalError:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 收藏失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 确认收藏存在性失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                    errorId, existResult.Status.ToString(), (int)existResult.Status, user.Id);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 收藏失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
        }

        // ===== 获取收藏夹 =====
        var foldersResult = await _applicationService.GetFavoriteTypeServiceAsync(user.Id);
        switch (existResult.Status)
        {
            case ServiceStatus.NotFound:
            case ServiceStatus.Success:
                string? avatarUrl = null;
                if (threadChannel is SocketThreadChannel socketThreadChannel)
                {
                    avatarUrl = socketThreadChannel.Owner?.GetAvatarUrl();
                }

                List<FavoriteTypeInfo> folders;
                if (foldersResult.Status == ServiceStatus.Conflict || foldersResult.Payload is null)
                {
                    folders = new();
                }
                else
                {
                    folders = foldersResult.Payload;
                }

                var state = new FavoriteInteractionState
                {
                    ThreadId = threadChannel.Id,
                    ThreadName = threadChannel.Name,
                    AuthorAvatarUrl = avatarUrl,
                    AllFolders = folders,
                    IsPushEnabled = true,
                    CurrentFolderPage = 0
                    // SelectedFolderIds 初始为空
                };

                var view = _interactionsCacheManager.RegisterView(user.Id, state);

                return view.Render();
            case ServiceStatus.InternalError:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 收藏失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 获取用户的收藏的收藏夹列表失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                    errorId, existResult.Status.ToString(), (int)existResult.Status, user.Id);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 收藏失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
        }
    }

    public async Task<MessageComponent> ConfirmUnFavoriteAsync(IUser user, IThreadChannel threadChannel)
    {
        var existResult = await _applicationService.RemoveFavoriteServiceAsync(user.Id, threadChannel.Id);
        switch (existResult.Status)
        {
            case ServiceStatus.Success:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 取消收藏成功", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            case ServiceStatus.NotFound:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## 取消收藏失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            case ServiceStatus.InternalError:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 取消收藏失败", 1000)
                        .WithTextDisplay(existResult.Message, 1001)
                    )
                    .Build();
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: 取消收藏失败失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                    errorId, existResult.Status.ToString(), (int)existResult.Status, user.Id);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 取消收藏失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
        }
    }
    #endregion

    // =============================================
    #region  交互回调
    // =============================================

    [ComponentInteraction("Fav:*:*")]
    public async Task HandleFavoriteInteractionAsync(string action, string cacheId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: Fav:Select:* Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var cache = _interactionsCacheManager.GetView<FavoriteInteractionCache>(cacheId);

            if (cache is null)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 该交互已过期\n请使用命令发起新的交互")
                    )
                    .Build();
                await RespondAsync(components: component, ephemeral: true);
                return;
            }

            switch (action)
            {
                case "New":// 弹出新建收藏夹模态框
                    var mb = new ModalBuilder()
                        .WithTitle("新建收藏夹")
                        .WithCustomId($"Fav:Modal:Create:{cacheId}")
                        .AddTextInput("收藏夹名称", "folder_name", placeholder: "收藏夹名称", maxLength: 50, minLength: 1);
                    await RespondWithModalAsync(mb.Build());
                    return;
                case "First":
                    cache.ToFirst();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "Prev":
                    cache.Prev();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "Next":
                    cache.Next();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "Last":
                    cache.ToLast();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "Push:On":
                    cache.EnablePush();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "Push:Off":
                    cache.DisablePush();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "Cancel":
                    cache.Cancel();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    _interactionsCacheManager.TryRemove(cache.CacheId);
                    return;
                case "Confirm":
                    await DeferAsync(ephemeral:true);
                    var result = await _applicationService.CreateFavoriteServiceAsync(cache.Confirm());
                    switch (result.Status)
                    {
                        case ServiceStatus.Success:
                            var successComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Green)
                                    .WithTextDisplay($"## 收藏成功", 1000)
                                    .WithSeparator()
                                    .WithTextDisplay($"""
                                        帖子 <#{result.Payload?.ThreadId}> 已被添加于收藏夹 {result.Payload?.FavoriteTypeName}, 添加于: <t:{result.Payload?.AddedAt.ToUnixTimeSeconds()}:R>
                                        """, 1001)
                                )
                                .Build();
                            await interactionComponent.Message.ModifyAsync(msg => msg.Components = cache.Render());
                            await FollowupAsync(components: successComponent, ephemeral: true);
                            _interactionsCacheManager.TryRemove(cache.CacheId);
                            return;
                        case ServiceStatus.Conflict:
                        case ServiceStatus.NotFound:
                            var notFoundComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Orange)
                                    .WithTextDisplay($"## 收藏失败", 1000)
                                    .WithTextDisplay(result.Message, 1001)
                                )
                                .Build();
                            await FollowupAsync(components: notFoundComponent, ephemeral: true);
                            return;
                        case ServiceStatus.InternalError:
                            var internalErrorComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Orange)
                                    .WithTextDisplay($"## 收藏失败", 1000)
                                    .WithTextDisplay(result.Message, 1001)
                                )
                                .Build();
                            await FollowupAsync(components: internalErrorComponent, ephemeral: true);
                            return;
                        default:
                            string errorId = CorrelationIdGenerator.Generate();
                            _logger.Error("{errorId}: 为用户添加收藏失败, 意外的枚举: {result.Status}(result.StatusNum), 用户ID: {UserId}",
                                errorId, result.Status.ToString(), (int)result.Status, Context.User.Id);
                            var defaultComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay($"## 收藏失败", 1000)
                                    .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                                )
                                .Build();
                            await FollowupAsync(components: defaultComponent, ephemeral: true);
                            return;
                    }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleFavoriteInteractionAsync Exception");
        }
    }

    // 处理下拉菜单的选择
    [ComponentInteraction("Fav:Select:*")]
    public async Task HandleFolderSelectionAsync(string cacheId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: Fav:Select:* Context.Interaction is Not SocketMessageComponent");
                return;
            }
    
            var cache = _interactionsCacheManager.GetView<FavoriteInteractionCache>(cacheId);
    
            if (cache is null)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 该交互已过期\n请使用命令发起新的交互")
                    )
                    .Build();
                await RespondAsync(components: component, ephemeral: true);
                return;
            }
    
            var selectedValue = interactionComponent.Data.Values.FirstOrDefault(defaultValue: "");
    
            cache.UpdateSelected(selectedValue);
    
            await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleFolderSelectionAsync Exception");
        }
    }
    
    // 处理新建收藏夹的模态框提交
    [ModalInteraction("Fav:Modal:Create:*")]
    public async Task HandleCreateFolderModalAsync(string cacheId, SocketModal modal)
    {
        try
        {
            await DeferAsync(ephemeral:true);
            var componentData = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "folder_name");

            var folderName = componentData?.Value;

            if (string.IsNullOrWhiteSpace(folderName))
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 文件夹名称不能为空")
                    )
                    .Build();
                await FollowupAsync(components: component, ephemeral: true);
                return;
            }

            if (folderName.Length <= 1 || folderName.Length > 50)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 文件夹名称长度必须在 1 到 50 之间")
                    )
                    .Build();
                await FollowupAsync(components: component, ephemeral: true);
                return;
            }

            var cleanTypeName = folderName.Trim();
            var result = await _applicationService.CreateFavoriteTypeServiceAsync(Context.User.Id, cleanTypeName);

            switch (result.Status)
            {
                case ServiceStatus.Success:
                    break;
                case ServiceStatus.Conflict:
                    var conflictComponent = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Orange)
                            .WithTextDisplay($"## 收藏失败", 1000)
                            .WithTextDisplay(result.Message, 1001)
                        )
                        .Build();
                    await FollowupAsync(components: conflictComponent, ephemeral: true);
                    return;
                case ServiceStatus.InternalError:
                    var internalErrorComponent = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Orange)
                            .WithTextDisplay($"## 收藏失败", 1000)
                            .WithTextDisplay(result.Message, 1001)
                        )
                        .Build();
                    await FollowupAsync(components: internalErrorComponent, ephemeral: true);
                    return;
            }

            var cache = _interactionsCacheManager.GetView<FavoriteInteractionCache>(cacheId);

            if (cache is null)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 该交互已过期\n请使用命令发起新的交互")
                    )
                    .Build();
                await FollowupAsync(components: component, ephemeral: true);
                return;
            }

            if (result.Payload is null)
            {
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("错误ID: {errorId} | result.Payload is null", errorId);
                var payloadNullComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 收藏失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                await FollowupAsync(components: payloadNullComponent, ephemeral: true);
                return;
            }

            cache.UpdateFolders(result.Payload);

            await modal.Message.ModifyAsync(msg =>
            {
                msg.Components = cache.Render();
            });
            
            var successComponent = new ComponentBuilderV2()
                .WithContainer(container => container
                    .WithAccentColor(Color.Green)
                    .WithTextDisplay($"收藏夹 **{cleanTypeName}** 创建成功! 请点击刷新按钮刷新菜单, 然后在下拉菜单中选择", 1000)
                )
                .Build();
            await FollowupAsync(components: successComponent, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleCreateFolderModalAsync Exception");
        }
    }

    #endregion

    // =============================================
    #region  AppMenu
    // =============================================
    #endregion


    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("收藏该贴", "收藏该帖子")]
    public async Task FavoriteAsync()
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
                var result = await ConfirmFavoriteAsync(Context.User, threadChannel);
                await FollowupAsync(components: result, ephemeral: true);
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
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }

    [SlashCommand("取消收藏", "取消收藏该帖子")]
    public async Task UnfavoriteAsync()
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
    
            // === 命令逻辑 ===
            if (Context.Channel is IThreadChannel threadChannel)
            {
                var result = await ConfirmUnFavoriteAsync(Context.User, threadChannel);
                await FollowupAsync(components: result, ephemeral: true);
            }
            else
            {
                // 这个分支理论上不会被触发，因为 InGuildThread 已经检查过了
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("错误", "无法在非帖子频道执行此操作。"), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }

    [SlashCommand("查看收藏", "查看自己的收藏列表")]
    public async Task ViewFavoriteAsync()
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
    
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }

    [SlashCommand("删除收藏夹", "删除一个收藏夹并处理其中的帖子")]
    public async Task DeleteFavoriteCategoryAsync()
    {        try
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
    
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }

    [SlashCommand("重命名收藏夹", "更改一个收藏夹的名称")]
    public async Task RenameFavoriteCategoryAsync()
    {        try
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
    
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }
    #endregion
}