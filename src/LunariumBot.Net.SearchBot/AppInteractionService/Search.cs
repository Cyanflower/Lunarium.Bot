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

public class FastSearch : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly PaginatorManager _paginatorManager;
    private readonly ApplicationService _applicationService;
    private readonly InteractionsCacheManager _interactionsCacheManager;
    private readonly ComponentUtils _componentUtils;

    public FastSearch(ILogger logger, AuthUtil authUtil, PaginatorManager paginatorManager, ApplicationService applicationService, InteractionsCacheManager interactionsCacheManager)
    {
        _logger = logger.ForContext("Subscription");
        _authUtil = authUtil;
        _paginatorManager = paginatorManager;
        _applicationService = applicationService;
        _interactionsCacheManager = interactionsCacheManager;
        _componentUtils = new(_logger);
    }

    // =============================================
    #region 业务方法
    // =============================================
    public IInteractionsCache SearchInteractionsCache(IUser user)
    {
        var cache = _interactionsCacheManager.RegisterView(user.Id, new SearchConditionsBuilder());
        return cache;
    }
    #endregion

    // =============================================
    #region  交互回调
    // =============================================
    [ComponentInteraction($"Search:*:*")]
    public async Task HandleActionIncludeKwAsync(string action, string cacheId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning($"交互回调: [{SearchInteractionCache.ActionIncludeKw}:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var cache = _interactionsCacheManager.GetView<SearchInteractionCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            switch (action)
            {
                case "IncludeKw":
                    {
                        var mb = new ModalBuilder()
                            .WithTitle("搜索关键词")
                            .WithCustomId($"{SearchInteractionCache.ActionIncludeKw}:Modal:{cacheId}")
                            .AddTextInput("关键词", "IncludeKw", placeholder: "要包含的关键词...", maxLength: 200, minLength: 1);

                        await RespondWithModalAsync(mb.Build());
                        return;
                    }
                case "ExcludeKw":
                    {
                        var mb = new ModalBuilder()
                            .WithTitle("排除关键词")
                            .WithCustomId($"{SearchInteractionCache.ActionExcludeKw}:Modal:{cacheId}")
                            .AddTextInput("排除词", "ExcludeKw", placeholder: "要排除的关键词...", maxLength: 200, minLength: 1);

                        await RespondWithModalAsync(mb.Build());
                        return;
                    }
                case "KwExact":
                    {
                        cache.Builder.WithKeywordSearchMode(SearchMode.Exact);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "KwFuzzy":
                    {
                        cache.Builder.WithKeywordSearchMode(SearchMode.Fuzzy);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "Author":
                    {
                        if (interactionComponent.Data.Type == ComponentType.UserSelect)
                        {
                            var selectedValues = interactionComponent.Data.Users;
                            cache.Builder.WithAuthorId(selectedValues.Select(user => user.Id).ToList());
                            await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        }
                        return;
                    }
                case "Channel":
                    {
                        var selectedValue = interactionComponent.Data.Values.FirstOrDefault();
                        var channelId = ulong.Parse(selectedValue!);
                        cache.Builder.WithChannelId([channelId]);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "IncludeTag":
                    {
                        var selectedValues = interactionComponent.Data.Values;
                        var tagId = selectedValues.Select(id => ulong.Parse(id)).ToList();
                        cache.Builder.WithIncludeTags(tagId);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "ExcludeTag":
                    {
                        var selectedValues = interactionComponent.Data.Values;
                        var tagId = selectedValues.Select(id => ulong.Parse(id)).ToList();
                        cache.Builder.WithExcludeTags(tagId);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "TagExact":
                    {
                        cache.Builder.WithTagSearchMode(SearchMode.Exact);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "TagFuzzy":
                    {
                        cache.Builder.WithTagSearchMode(SearchMode.Exact);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "Sort":
                    {
                        var selectedValue = interactionComponent.Data.Values.FirstOrDefault();
                        var sortOption = Enum.Parse<PostSortOption>(selectedValue!);
                        cache.Builder.WithPostSortOption(sortOption);
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "Confirm":
                    {
                        await DeferAsync(ephemeral: true);
                        var result = await _applicationService.GeneralSerachServiceAsync(Context.User.Id, cache.Builder.Build());
                        if (result.Status != ServiceStatus.Success)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(result, "搜索失败"));
                            return;
                        }
                        await FollowupAsync(components: result.Payload!.Render());
                        return;
                    }
                // case "SaveQuickSearch":
                //     return;
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleActionIncludeKwAsync Exception");
        }
    }

    [ModalInteraction($"{SearchInteractionCache.ActionIncludeKw}:Modal:*")]
    public async Task HandleActionIncludeKwModalAsync(string cacheId, SocketModal modal)
    {
        try
        {
            var componentData = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "IncludeKw");

            var includeKeywords = componentData?.Value;

            var cache = _interactionsCacheManager.GetView<SearchInteractionCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            cache.Builder.WithIncludeKeywords(includeKeywords);

            await modal.Message.ModifyAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleActionIncludeKwModalAsync Exception");
        }
    }

    [ModalInteraction($"{SearchInteractionCache.ActionExcludeKw}:Modal:*")]
    public async Task HandleActionExcludeKwAsync(string cacheId, SocketModal modal)
    {
        try
        {
            var componentData = modal.Data.Components
                .FirstOrDefault(x => x.CustomId == "ExcludeKw");

            var excludeKeywords = componentData?.Value;

            var cache = _interactionsCacheManager.GetView<SearchInteractionCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            cache.Builder.WithExcludeKeywords(excludeKeywords);

            await modal.Message.ModifyAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleActionExcludeKwAsync Exception");
        }
    }
    #endregion

    // =============================================
    #region  AppMenu
    // =============================================
    // [MessageCommand("关注该作者")]
    // public async Task MessageMenuSubscriptionfAsync(IMessage message)
    // {
    //     try
    //     {
    //         // 1. 定义此命令所需的权限条件
    //         var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
    //         // 2. 进行鉴权
    //         var authResult = await _authUtil.Authorization(requiredConditions, Context);
    //         // 3. 检查鉴权结果，如果失败则提前返回
    //         if (authResult.Status != AuthorizationStatus.Success)
    //         {
    //             var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
    //             // 以 "仅自己可见" 的方式回复失败原因
    //             await RespondAsync(components: failedReasonComponent, ephemeral: true);
    //             return; // 终止命令执行
    //         }

    //         await DeferAsync(ephemeral: true);

    //         var component = await ConfirmSubscriptionAsync(Context.User.Id, message.Author.Id);
    //         await FollowupAsync(components: component, ephemeral: true);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error(ex, "MessageMenuSubscriptionfAsync Ex");
    //     }
    // }

    // [UserCommand("关注该作者")]
    // public async Task UserMenuSubscriptionAsync(IUser user)
    // {
    //     try
    //     {
    //         // 1. 定义此命令所需的权限条件
    //         var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild;
    //         // 2. 进行鉴权
    //         var authResult = await _authUtil.Authorization(requiredConditions, Context);
    //         // 3. 检查鉴权结果，如果失败则提前返回
    //         if (authResult.Status != AuthorizationStatus.Success)
    //         {
    //             var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
    //             // 以 "仅自己可见" 的方式回复失败原因
    //             await RespondAsync(components: failedReasonComponent, ephemeral: true);
    //             return; // 终止命令执行
    //         }

    //         await DeferAsync(ephemeral: true);

    //         var component = await ConfirmSubscriptionAsync(Context.User.Id, user.Id);
    //         await FollowupAsync(components: component, ephemeral: true);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error(ex, "UserMenuSubscriptionAsync Ex");
    //     }
    // }
    #endregion

    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("搜索", "打开搜索面板")]
    public async Task Search()
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
    
            var cache = SearchInteractionsCache(Context.User);
    
            await FollowupAsync(components: cache.Render(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Search Exception");
        }
    }
    #endregion
}