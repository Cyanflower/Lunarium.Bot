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

public class UserDashboard : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly PaginatorManager _paginatorManager;
    private readonly ApplicationService _applicationService;
    private readonly InteractionsCacheManager _interactionsCacheManager;
    private readonly ComponentUtils _componentUtils;


    public UserDashboard(ILogger logger, AuthUtil authUtil, PaginatorManager paginatorManager, ApplicationService applicationService, InteractionsCacheManager interactionsCacheManager)
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
    // public async Task<MessageComponent> ConfirmSubscriptionAsync(ulong userId, ulong authorId)
    // {

    // }
    #endregion

    // =============================================
    #region  交互回调
    // =============================================
    [ComponentInteraction($"Dash:*")]
    public async Task HandleDashboardAsync(string action)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning($"交互回调: [{SearchInteractionCache.ActionIncludeKw}:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            switch (action)
            {
                case "Search":
                    {
                        var cache = _interactionsCacheManager.RegisterView(Context.User.Id, new SearchConditionsBuilder());
                        await RespondAsync(components: cache.Render(), ephemeral: true);
                        return;
                    }
                // case "QuickSearch":
                //     {
                //         return;
                //     }
                case "ViewFav":
                    {
                        await DeferAsync(ephemeral: true);

                        var foldersResult = await _applicationService.GetFavoriteTypeServiceAsync(Context.User.Id);

                        if (foldersResult.Status != ServiceStatus.Success)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(foldersResult, "## 查看收藏列表失败"), ephemeral: true);
                        }

                        var view = _paginatorManager.RegisterView<FavoriteTypeInfo>(Context.User.Id, foldersResult.Payload!);
                        
                        await FollowupAsync(components: view.Render(), ephemeral: true);
                        return;
                    }
                case "ViewSub":
                    {
                        await DeferAsync(ephemeral: true);

                        var result = await _applicationService.QueryUserSubscriptionsAsync(Context.User.Id);
                        if (result.Status != ServiceStatus.Success)
                        {
                            await FollowupAsync(components: _componentUtils.ErrorComponente(result, "查看收藏列表失败"), ephemeral: true);
                            return;
                        }
                        var view = _paginatorManager.RegisterView<SubscriptionData>(Context.User.Id, result.Payload!);

                        await FollowupAsync(components: view.Render(), ephemeral: true);

                        return;
                    }
                case "ViewFavNotify":
                    {
                        
                        return;
                    }
                case "ViewSubNotify":
                    {
                        return;
                    }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleDashboardAsync Exception");
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
    [SlashCommand("个人中心", "打开您的个人面板 (仅自己可见)")]
    public async Task UserDashboardAsync()
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
    
            // 调用数据库查询通知情况
    
            var component = new ComponentBuilderV2()
                .WithContainer(container =>
                {
                    container.WithAccentColor(Color.Blue);

                    // 概览
                    container.WithSection(section => section
                        .WithTextDisplay($"""
                            ## 星图助手（旅程卡区助手）
                            👋 欢迎回来, <@{Context.User.Id}>
                            -# 📬 您的通知:
                            > -# 收藏更新通知: 
                            > -# 订阅作者通知: 
                            请选择您想要进行的操作:
                            """)
                        .WithAccessory(new ThumbnailBuilder().WithMedia(Context.User.GetAvatarUrl())));
                        
                    container.WithSeparator();

                    container.WithSection(section => section
                        .WithTextDisplay($"""✨ 前往星图ΣΤΑΘΜΗ""")
                        .WithAccessory(new ButtonBuilder("前往星图", style: ButtonStyle.Link, url:"https://opizontas.org/")));
    
                    container.WithSeparator();
    
                    // 搜索
                    // container.WithTextDisplay("🔍 搜索与发现\n-# 使用搜索面板设置条件查找帖子，或使用保存过的预设快捷指令。");
                    container.WithTextDisplay("🔍 搜索与发现\n-# 使用搜索面板设置条件查找帖子");
                    container.WithActionRow(actionRow => actionRow
                        .WithButton(new ButtonBuilder("搜索面板", "Dash:Search", ButtonStyle.Primary, emote: new Emoji("🔎")))
                        // .WithButton(new ButtonBuilder("快捷搜索", "Dash:QuickSearch", ButtonStyle.Primary, emote: new Emoji("🗃")))
                    );
    
                    container.WithSeparator();
    
                    // 收藏与关注
                    container.WithTextDisplay("🌟 收藏与关注\n-# 查看或管理您的收藏夹和关注的作者。");
                    container.WithActionRow(actionRow => actionRow
                        .WithButton(new ButtonBuilder("管理收藏", "Dash:ViewFav", ButtonStyle.Primary, emote: new Emoji("⭐")))
                        .WithButton(new ButtonBuilder("快捷搜索", "Dash:ViewSub", ButtonStyle.Primary, emote: new Emoji("❤️")))
                    );
                    container.WithSeparator();
    
                    // 更新通知
                    container.WithTextDisplay("🔔 消息通知\n-# 查看您收藏与订阅内容的最新更新推送。");
                    container.WithActionRow(actionRow => actionRow
                        .WithButton(new ButtonBuilder("收藏更新", "Dash:ViewFavNotify", ButtonStyle.Primary, emote: new Emoji("📨")))
                        .WithButton(new ButtonBuilder("订阅更新", "Dash:ViewSubNotify", ButtonStyle.Primary, emote: new Emoji("💌")))
                    );
                    // container.WithSeparator();
    
                    // 系统/帮助
                    // 需要编写一份使用指南
                    // container.WithActionRow(actionRow => actionRow
                    //     .WithButton(new ButtonBuilder("使用说明", "Dash:Help", ButtonStyle.Primary))
                    // );
                    // container.WithSeparator();
                });
    
            await FollowupAsync(components: component.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    #endregion
}