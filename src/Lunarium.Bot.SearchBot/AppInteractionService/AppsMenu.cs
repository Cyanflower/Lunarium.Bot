// NuGet 包依赖
using Discord;
using Discord.Interactions;

namespace Lunarium.Bot.SearchBot.AppInteractionService;

public class AppMenus : InteractionModuleBase<SocketInteractionContext>
{
    private readonly Favorite _favoriteCommands;
    private readonly SubscriptionCommands _subscriptionCommands;
    private readonly SearchCommands _searchCommands;
    public AppMenus(
        Favorite favoriteCommands,
        SubscriptionCommands subscriptionCommands,
        SearchCommands searchCommands
    )
    {
        _favoriteCommands = favoriteCommands;
        _subscriptionCommands = subscriptionCommands;
        _searchCommands = searchCommands;
    }
    // --- Message Commands ---
    // 这些命令会在右键点击 "消息" -> "Apps" 时出现

    /// <summary>
    /// 右键菜单快捷入口：收藏此帖子
    /// </summary>
    [MessageCommand("收藏该帖")]
    public async Task MessageMenuFavoriteAsync(IMessage message)
    {
        await _favoriteCommands.FavoriteAsync();
    }
    /// <summary>
    /// 右键菜单快捷入口：取消收藏
    /// </summary>
    [MessageCommand("取消收藏")]
    public async Task MessageMenuUnfavoriteAsync(IMessage message)
    {
        await _favoriteCommands.UnfavoriteAsync();
    }
    /// <summary>
    /// 右键菜单快捷入口：订阅此帖子
    /// </summary>
    [MessageCommand("订阅该帖")]
    public async Task MessageMenuSubscribeAsync(IMessage message)
    {
        await _subscriptionCommands.SubscribeAsync();
    }

    /// <summary>
    /// 右键菜单快捷入口：搜索该消息作者的作品
    /// </summary>
    [MessageCommand("搜索该作者")]
    public async Task MessageMenuSearchByMessageAuthorAsync(IMessage message)
    {
        await _searchCommands.SearchAsync(author: message.Author);
    }

    /// <summary>
    /// 右键菜单快捷入口：搜索自己的作品
    /// </summary>
    [MessageCommand("搜索自己的作品")]
    public async Task MessageMenuSearchSelfAsync(IMessage message)
    {
        await _searchCommands.SearchAsync(author: Context.User);
    }

    // --- User Commands ---

    /// <summary>
    /// 右键菜单快捷入口：搜索该用户的作品
    /// </summary>
    [UserCommand("搜索该作者")]
    public async Task UserMenuSearchByUserAsync(IUser user)
    {
        // 直接把右键点击的用户对象作为参数传进去
        await _searchCommands.SearchAsync(author: user);
    }

    [UserCommand("搜索自己的作品")]
    public async Task UserMenuSearchSelfAsync(IUser user)
    {
        await _searchCommands.SearchAsync(author: Context.User);
    }

    /// <summary>
    /// 右键菜单快捷入口：查看收藏
    /// </summary>
    [UserCommand("查看收藏")]
    public async Task UserMenuViewFavoriteAsync(IUser user)
    {
        await _favoriteCommands.ViewFavoriteAsync();
    }

    /// <summary>
    /// 右键菜单快捷入口：查看订阅
    /// </summary>
    [UserCommand("查看订阅")]
    public async Task UserMenuViewSubscriptionsAsync(IUser user)
    {
        await _subscriptionCommands.ViewSubscriptionsAsync();
    }

    /// <summary>
    /// 右键菜单快捷入口：查看更新推送
    /// </summary>
    [UserCommand("查看更新推送")]
    public async Task UserMenuViewUpdatesAsync(IUser user)
    {
        await _subscriptionCommands.ViewUpdatesAsync();
    }
}