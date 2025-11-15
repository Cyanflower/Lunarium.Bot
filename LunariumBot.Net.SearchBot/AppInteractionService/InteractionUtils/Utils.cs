// NuGet 包依赖
using Discord;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public static class AppInteractionUitls
{
    public static MessageComponent DirectSearchMenuComponent(ConfigLoader _configLoader)
    {
        var directSearchMenu = new SelectMenuBuilder()
            .WithCustomId("direct_search_menu")
            .WithPlaceholder("选择一个分区快速浏览最新帖子...");

        directSearchMenu.AddOption("浏览全部分区最新帖子", "__ALL__", "不按分区筛选, 查看最新的200个帖子");
        var orderdChannels = _configLoader.Config.ChannelOrder.OrderBy(kvpair => kvpair.Value).Select(kvpair => kvpair.Key);
        foreach (var channelName in orderdChannels)
        {
            directSearchMenu.AddOption($"浏览「{channelName}」分区最新帖子", channelName, $"查看「{channelName}」分区的最新200个帖子");
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(directSearchMenu, row: 0)
            .WithButton("查看全部收藏的帖子", customId: "view_favorite", style: ButtonStyle.Secondary, row: 1)
            .WithButton("查看全部订阅的帖子", customId: "view_subthread", style: ButtonStyle.Secondary, row: 1)
            .WithButton("查看更新: 查看订阅帖子的更新推送消息", customId: "view_pushlist", style: ButtonStyle.Primary, row: 2)
            .WithButton("快捷搜索: 打开快速搜索菜单", customId: "quick_search", style: ButtonStyle.Success, row: 3)
            .Build();
        return component;
    }
}