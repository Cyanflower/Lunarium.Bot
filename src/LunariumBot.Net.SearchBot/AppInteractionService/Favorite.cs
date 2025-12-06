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
    private readonly ComponentUtils _componentUtils;

    public Favorite(ILogger logger, AuthUtil authUtil, PaginatorManager paginatorManager, ApplicationService applicationService, InteractionsCacheManager interactionsCacheManager)
    {
        _logger = logger.ForContext("Favorite");
        _authUtil = authUtil;
        _paginatorManager = paginatorManager;
        _applicationService = applicationService;
        _interactionsCacheManager = interactionsCacheManager;
        _componentUtils = new(_logger);
    }

    // =============================================
    #region 业务方法
    // =============================================
    public async Task<MessageComponent> ConfirmFavoriteAsync(IUser user, IThreadChannel threadChannel)
    {
        // ===== 收藏存在性检查 =====
        var existResult = await _applicationService.CheckFavoriteExistServiceAsync(user.Id, threadChannel.Id);
        if (existResult.Status != ServiceStatus.NotFound)
        {
            return _componentUtils.ErrorComponente(existResult, "## 确认收藏失败");
        }

        // ===== 获取收藏夹 =====
        var foldersResult = await _applicationService.GetFavoriteTypeServiceAsync(user.Id);
        if (foldersResult.Status != ServiceStatus.NotFound && foldersResult.Status != ServiceStatus.Success)
        {
            return _componentUtils.ErrorComponente(existResult, "## 收藏失败");
        }

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
    }

    public async Task<MessageComponent> ConfirmUnFavoriteAsync(IUser user, IThreadChannel threadChannel)
    {
        var existResult = await _applicationService.CheckFavoriteExistServiceAsync(user.Id, threadChannel.Id);
        if (existResult.Status != ServiceStatus.NotFound)
        {
            return _componentUtils.ErrorComponente(existResult, "## 确认取消收藏失败");
        }

        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Orange)
                .WithTextDisplay($"## 确认取消收藏\n确定取消收藏帖子: {threadChannel.Name}?", 1000)
                .WithTextDisplay("", 1001)
                .WithActionRow(actionRow =>
                {
                    actionRow.WithButton("确认", $"UnFav:{user.Id}:{threadChannel.Id}", ButtonStyle.Success, new Emoji("✅"));
                    actionRow.WithButton("取消", $"GeneralCancelV2", ButtonStyle.Danger, new Emoji("❌"));
                })
            )
            .Build();
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
                _logger.Warning("交互回调: [Fav:Select:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var cache = _interactionsCacheManager.GetView<FavoriteInteractionCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
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
                    await DeferAsync(ephemeral: true);
                    var result = await _applicationService.AddFavoriteServiceAsync(cache.Confirm());
                    if (result.Status != ServiceStatus.Success)
                    {
                        var component = _componentUtils.ErrorComponente(result, "## 收藏失败");
                        await FollowupAsync(components: component, ephemeral: true);
                        return;
                    }
                    else
                    {
                        var component = new ComponentBuilderV2()
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
                        await FollowupAsync(components: component, ephemeral: true);
                        _interactionsCacheManager.TryRemove(cache.CacheId);
                        return;
                    }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleFavoriteInteractionAsync Exception");
        }
    }

    [ComponentInteraction("UnFav:*:*")]
    public async Task HandleUnFavoriteInteractionAsync(ulong userId, ulong threadId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Fav:Select:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            await DeferAsync(ephemeral: true);

            var result = await _applicationService.RemoveFavoriteServiceAsync(userId, threadId);

            if (result.Status != ServiceStatus.Success)
            {
                await FollowupAsync(components: _componentUtils.ErrorComponente(result, "## 取消收藏失败"), ephemeral: true);
                return;
            }
            
            var component = new ComponentBuilderV2()
                .WithContainer(container => container
                    .WithAccentColor(Color.Green)
                    .WithTextDisplay($"## 取消收藏成功", 1000)
                    .WithTextDisplay(result.Message, 1001)
                )
                .Build();
            await FollowupAsync(components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleUnFavoriteInteractionAsync Exception");
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
                _logger.Warning("交互回调: [Fav:Select:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var cache = _interactionsCacheManager.GetView<FavoriteInteractionCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
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
            await DeferAsync(ephemeral: true);
            var componentData = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "folder_name");

            var folderName = componentData?.Value;

            if (string.IsNullOrWhiteSpace(folderName))
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 文件夹名称不能为空"), ephemeral: true);
                return;
            }

            if (folderName.Length <= 1 || folderName.Length > 50)
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 文件夹名称长度必须在 1 到 50 之间"), ephemeral: true);
                return;
            }

            var cleanTypeName = folderName.Trim();
            var result = await _applicationService.CreateFavoriteTypeServiceAsync(Context.User.Id, cleanTypeName);
            if (result.Status != ServiceStatus.Success)
            {
                await FollowupAsync(components: _componentUtils.ErrorComponente(result, "## 收藏失败"), ephemeral: true);
                return;
            }

            var cache = _interactionsCacheManager.GetView<FavoriteInteractionCache>(cacheId);

            if (cache is null)
            {
                await FollowupAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            if (result.Payload is null)
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("## 收藏失败", "result.Payload is null"), ephemeral: true);
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

    [ComponentInteraction("FavView:*:*:*")]
    public async Task HandleFavView(string action, string viewId, int index)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调:[FavView:Select:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var view = _paginatorManager.GetView<FavoriteTypePaginatedView>(viewId);

            if (view is null)
            {
                await FollowupAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            var favoriteType = view.GetItemByIndex(index);

            if (favoriteType is null)
            {
                await RespondAsync(components: _componentUtils.OutBoundComponente("查看收藏列表失败", $"action: {action}, viewId: {viewId}, index: {index}"), ephemeral: true);
                return;
            }

            switch (action)
            {
                case "View":
                    {
                        await DeferAsync(ephemeral: true);

                        var favorites = await _applicationService.QueryUserFavoritesServiceAsync(Context.User.Id, favoriteType.FavoriteId);

                        if (favorites.Status != ServiceStatus.Success || favorites.Payload is null)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(favorites, "查看收藏列表失败"), ephemeral: true);
                            return;
                        }

                        var posts = await _applicationService.GeneralPostCacheServiceAsync(favorites.Payload!.Select(f => f.ThreadId).ToList());

                        if (posts.Status != ServiceStatus.Success || posts.Payload is null)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(favorites, "查看收藏列表失败"), ephemeral: true);
                            return;
                        }

                        List<FavoriteDisplayItem> items = new();
                        foreach (var favoritesInfo in favorites.Payload)
                        {
                            if (posts.Payload.TryGetValue(favoritesInfo.ThreadId, out var post))
                            {
                                items.Add(new(favoritesInfo, post));
                            }
                        }

                        var postView = _paginatorManager.RegisterView<FavoriteDisplayItem>(Context.User.Id, items);

                        await FollowupAsync(components: postView.Render(), ephemeral: true);
                        return;
                    }

                case "Rename":
                    {
                        var mb = new ModalBuilder()
                            .WithTitle("重命名收藏夹")
                            .WithCustomId($"Fav:Modal:Rename:{viewId}:{favoriteType.FavoriteId}")
                            .AddTextInput("新收藏夹名称", "folder_name", placeholder: "新收藏夹名称", maxLength: 50, minLength: 1);

                        await RespondWithModalAsync(mb.Build());
                        return;
                    }
                case "Delete":
                    {
                        await DeferAsync(ephemeral: true);

                        var foldersResult = await _applicationService.GetFavoriteTypeServiceAsync(Context.User.Id);

                        if (foldersResult.Status != ServiceStatus.Success || foldersResult.Payload == null)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(foldersResult, "无法获取收藏夹列表"), ephemeral: true);
                            return;
                        }

                        var removeCache = _interactionsCacheManager.RegisterView(Context.User.Id, foldersResult.Payload) as RemoveFavoriteInteractionCache;
                        if (removeCache == null) return;

                        removeCache.SetContext(favoriteType.FavoriteId, interactionComponent, view);

                        await FollowupAsync(components: removeCache.Render(), ephemeral: true);
                        return;
                    }
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    [ComponentInteraction("FavRemove:*:*")]
    public async Task HandleRemoveFavoriteInteraction(string action, string cacheId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent) return;

            var cache = _interactionsCacheManager.GetView<RemoveFavoriteInteractionCache>(cacheId);
            if (cache is null)
            {
                await interactionComponent.UpdateAsync(msg => msg.Components = _componentUtils.ExpiredComponente());
                return;
            }

            switch (action)
            {
                case "Mode:Drop":
                    cache.SetMode(true);
                    break;

                case "Mode:Trans":
                    cache.SetMode(false);
                    break;

                case "Select":
                    var selected = interactionComponent.Data.Values.FirstOrDefault();
                    if (selected != null) cache.SelectDestination(selected);
                    break;

                case "First":
                    cache.PageFirst();
                    break;

                case "Prev":
                    cache.PagePrev();
                    break;

                case "Next":
                    cache.PageNext();
                    break;

                case "Last":
                    cache.PageLast();
                    break;

                case "Cancel":
                    cache.Cancel();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    _interactionsCacheManager.TryRemove(cacheId);
                    return;

                case "Confirm":
                    await DeferAsync(ephemeral: true);
                    var (isDropAll, targetFavorite, destinationFavorite) = cache.Confirm();

                    ServiceResult result;
                    if (isDropAll)
                    {
                        // 直接删除收藏夹
                        result = await _applicationService.RemoveFavoriteCategoryServiceAsync(Context.User.Id, targetFavorite);
                        if (result.Status != ServiceStatus.Success)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(result, "## 删除收藏夹失败"));
                            return;
                        }
                    }
                    else
                    {
                        // 执行转移并删除
                        result = await _applicationService.MoveAndRemoveFavoriteCategoryServiceAsync(Context.User.Id, targetFavorite.FavoriteId, destinationFavorite!.FavoriteId);
                        if (result.Status != ServiceStatus.Success)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(result, "## 删除并转移收藏夹失败"));
                            return;
                        }
                    }

                    // 更新 UI 为完成状态
                    await interactionComponent.Message.ModifyAsync(msg => msg.Components = cache.Render());

                    // 发送结果提示
                    var resultComponent = new ComponentBuilderV2()
                        .WithContainer(c => c
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"## 操作成功\n{result.Message}"))
                        .Build();

                    await FollowupAsync(components: resultComponent, ephemeral: true);

                    _interactionsCacheManager.TryRemove(cacheId);
                    return;
            }

            // 对于非终结操作，更新 UI
            await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleRemoveFavoriteInteraction Exception");
        }
    }

    [ModalInteraction("Fav:Modal:Rename:*:*")]
    public async Task HandleRenameFavoriteInteraction(string viewId, long favoriteId, SocketModal modal)
    {
        try
        {
            await DeferAsync(ephemeral: true);

            var componentData = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "folder_name");

            var folderName = componentData?.Value;

            if (string.IsNullOrWhiteSpace(folderName))
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 文件夹名称不能为空"), ephemeral: true);
                return;
            }

            if (folderName.Length <= 1 || folderName.Length > 50)
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 文件夹名称长度必须在 1 到 50 之间"), ephemeral: true);
                return;
            }

            var view = _paginatorManager.GetView<FavoriteTypePaginatedView>(viewId);

            if (view is null)
            {
                await FollowupAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            var result = await _applicationService.RenameFavoriteCategoryServiceAsync(Context.User.Id, favoriteId, folderName);
            if (result.Status != ServiceStatus.Success)
            {
                await FollowupAsync(components: _componentUtils.ErrorComponente(result, "## 重命名文件夹失败"), ephemeral: true);
                return;
            }

            view.UpdateName(favoriteId, folderName);

            var component = new ComponentBuilderV2()
                .WithContainer(container => container
                    .WithAccentColor(Color.Green)
                    .WithTextDisplay($"## 重命名文件夹成功", 1000)
                    .WithSeparator()
                    .WithTextDisplay(result.Message, 1001)
                )
                .Build();

            await FollowupAsync(components: component, ephemeral: true);
            await modal.Message.ModifyAsync(msg =>
            {
                msg.Components = view.Render();
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleRenameFavoriteInteraction Exception");
        }
    }

    [ComponentInteraction("UnFavByView:*:*:*")]
    public async Task HandleUnFavoriteByViewInteractionAsync(string viewId, ulong userId, ulong threadId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent) return;

            var cache = _interactionsCacheManager.GetView<RemoveFavoriteInteractionCache>(viewId);
            if (cache is null)
            {
                await interactionComponent.UpdateAsync(msg => msg.Components = _componentUtils.ExpiredComponente());
                return;
            }

            await HandleUnFavoriteInteractionAsync(userId, threadId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }
    #endregion

    // =============================================
    #region  AppMenu
    // =============================================
    [MessageCommand("收藏本帖")]
    public async Task MessageMenuFavoriteAsync(IMessage message)
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

            if (message.Channel is IThreadChannel threadChannel)
            {
                var component = await ConfirmFavoriteAsync(Context.User, threadChannel);
                await FollowupAsync(components: component, ephemeral: true);
            }
            else
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 操作失败\n本交互仅可在资源论坛频道的帖子内使用"), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MessageMenuFavoriteAsync Ex");
        }
    }

    [MessageCommand("取消收藏本帖")]
    public async Task MessageMenuUnFavoriteAsync(IMessage message)
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

            if (message.Channel is IThreadChannel threadChannel)
            {
                var component = await ConfirmUnFavoriteAsync(Context.User, threadChannel);
                await FollowupAsync(components: component, ephemeral: true);
            }
            else
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 操作失败\n本交互仅可在资源论坛频道的帖子内使用"), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MessageMenuUnFavoriteAsync Ex");
        }
    }
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
                var result = await ConfirmFavoriteAsync(Context.User, threadChannel);
                await FollowupAsync(components: result, ephemeral: true);
            }
            else
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 操作失败\n**xxxx本帖** 系列命令仅可在资源论坛频道的帖子内使用"), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }

    [SlashCommand("取消收藏本帖", "取消收藏该帖子")]
    public async Task UnfavoriteAsync()
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
                var result = await ConfirmUnFavoriteAsync(Context.User, threadChannel);
                await FollowupAsync(components: result, ephemeral: true);
            }
            else
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("## 操作失败\n**xxxx本帖** 系列命令仅可在资源论坛频道的帖子内使用"), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }

    [SlashCommand("查看收藏列表", "查看自己的收藏列表")]
    public async Task ViewFavoriteAsync()
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

            var foldersResult = await _applicationService.GetFavoriteTypeServiceAsync(Context.User.Id);

            if (foldersResult.Status != ServiceStatus.Success)
            {
                await FollowupAsync(components: _componentUtils.ErrorComponente(foldersResult, "## 查看收藏列表失败"), ephemeral: true);
            }

            var view = _paginatorManager.RegisterView<FavoriteTypeInfo>(Context.User.Id, foldersResult.Payload!);
            
            await FollowupAsync(components: view.Render(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FavoriteAsync Exception");
        }
    }
    #endregion
}