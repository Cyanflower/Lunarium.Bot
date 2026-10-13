using System.Text;
using Discord;
using Lunarium.Bot.Extensions;
using Lunarium.Bot.Logger;
using Lunarium.Bot.SearchBot.Models;
using Polly;

namespace Lunarium.Bot.SearchBot.Paginator;

public class FavoriteTypePaginatedView : BasePaginatedView<FavoriteTypeInfo>, IPaginatedView
{

    const int PageSize = 10;
    // 使用 : base() 将参数传递给父类的构造函数
    // 构造函数体可以是空的，因为所有的初始化逻辑都在基类中完成了。
    public FavoriteTypePaginatedView(IEnumerable<FavoriteTypeInfo> items, DateTimeOffset expiresAt, string id) : base(items, expiresAt, id, 6) { }

    public void LogViewDetail(ILogger logger)
    {
        logger.Warning("");
        logger.Warning("""
            LogViewDetail:
            PaginatorId: {PaginatorId}
            Items: {@Items}
            Pages: {@Pages}
            ExpiresAt: {ExpiresAt:O}
            _pageSize: {_pageSize}
            _totalPages: {_totalPages}
            _currentPageIndex {_currentPageIndex}
            """,
            PaginatorId, _items, _pages, ExpiresAt, _pageSize, _totalPages, _currentPageIndex);
    }

    public override MessageComponent Render()
    {
        int i = 0;
        var messageComponent = new ComponentBuilderV2();
        // Container 本身占用一个组件
        messageComponent.WithContainer(c =>
            {
                c.WithAccentColor(Color.Blue);
                // 3 * 10, 30个组件
                foreach (var item in _pages[_currentPageIndex].Items)
                {
                    c.WithTextDisplay($"""
                            ## 收藏夹: {item.TypeName}
                            {item.FavoriteCount}个收藏, 创建于: <t:{item.CreatedAt}:R> | <t:{item.CreatedAt}:D>
                            """);
                    c.WithActionRow(actionRow => actionRow
                        .AddComponent(new ButtonBuilder("查看", customId: $"FavView:View:{PaginatorId}:{i}", style: ButtonStyle.Success))
                        .AddComponent(new ButtonBuilder("重命名", customId: $"FavView:Rename:{PaginatorId}:{i}", style: ButtonStyle.Primary))
                        .AddComponent(new ButtonBuilder("删除", customId: $"FavView:Delete:{PaginatorId}:{i}", style: ButtonStyle.Danger))
                    );
                    c.WithSeparator();
                    i++;
                }
                // Web跳转 (2组件: 交互行, 按钮x1)
                c.WithActionRow(actionRow => actionRow
                    .AddComponent(new ButtonBuilder("或前往 Web 页面进行管理或查看", style: ButtonStyle.Link, url: "https://opizontas.org/"))
                );
            }
        );
        // 翻页部分: 5 按钮 + 1 容器, 6组件
        messageComponent.WithActionRow(PaginationControlsBuilder.Build(this));
        // 30(每item行) + 6(翻页交互) + 2(Web跳转) + 1(Container) + 1(Separator) = 40
        // 目前 discord 限制为 40 个组件
        return messageComponent.Build();
    }

    public void UpdateName(long favoriteId, string newName)
    {
        var favorite = _items.Where(f => f.FavoriteId == favoriteId).FirstOrDefault();
        if (favorite is not null)
        {
            favorite.UpdateName(newName);
        }
    }
}


public class FavoriteDisplayPaginatedView : BasePaginatedView<FavoriteDisplayItem>, IPaginatedView
{
    
    // 使用 : base() 将参数传递给父类的构造函数
    // 构造函数体可以是空的，因为所有的初始化逻辑都在基类中完成了。
    public FavoriteDisplayPaginatedView(IEnumerable<FavoriteDisplayItem> items, DateTimeOffset expiresAt, string id) : base(items, expiresAt, id, 5) { }
    
    public void LogViewDetail(ILogger logger)
    {
        logger.Warning("");
        logger.Warning("""
            LogViewDetail:
            PaginatorId: {PaginatorId}
            Items: {@Items}
            Pages: {@Pages}
            ExpiresAt: {ExpiresAt:O}
            _pageSize: {_pageSize}
            _totalPages: {_totalPages}
            _currentPageIndex {_currentPageIndex}
            """,
            PaginatorId, _items, _pages, ExpiresAt, _pageSize, _totalPages, _currentPageIndex);
    }

    public override MessageComponent Render()
    {
        int i = 0;
        var messageComponent = new ComponentBuilderV2();
        // Container 本身占用一个组件
        messageComponent.WithContainer(c =>
            {
                c.WithAccentColor(Color.Blue);
                // 5 * 6, 30个组件
                foreach (var item in _pages[_currentPageIndex].Items)
                {
                    var sb = new StringBuilder();

                    sb.AppendLine($"## {item.PostDisplayModel.Title}({item.PostDisplayModel.JumpUrl})");
                    sb.AppendLine($"-# {item.PostDisplayModel.ChannelName}丨作者: <@{item.PostDisplayModel.AuthorId}>");
                    sb.AppendLine($"-# 帖子信息: ❤️ {item.PostDisplayModel.ReactionCount}丨💬 {item.PostDisplayModel.ReplyCount}丨🏷 {item.PostDisplayModel.Tags}");
                    sb.AppendLine($"-# 发帖时间: <t:{item.PostDisplayModel.CreatedAt}:D>丨最近活跃: <t:{item.PostDisplayModel.LastActiveAt}:R>");
                    if (item.PostDisplayModel.UpdatedAt is not null)
                    {
                        sb.AppendLine($"-# 更新时间: <t:{item.PostDisplayModel.UpdatedAt.Value.ToUnixTimeSeconds()}:R>");
                        sb.AppendLine($"-# 更新链接: {item.PostDisplayModel.UpdateMessageJumpUrl}");
                    }
                    sb.AppendLine("============");
                    if (string.IsNullOrWhiteSpace(item.PostDisplayModel.Content))
                    {
                        sb.AppendLine("无首楼");
                    }
                    else
                    {
                        sb.AppendLine($"{item.PostDisplayModel.Content.Slice(150)}");
                    }
                    sb.AppendLine("============");
                    sb.AppendLine($"-# 收藏于: {item.FavoriteInfo.FavoriteTypeName} <t:{item.FavoriteInfo.AddedAt}:R> | <t:{item.FavoriteInfo.AddedAt}:D>");
                    // 1
                    c.WithSection(section =>
                    {
                        // 2
                        section.WithTextDisplay(sb.ToString());
                        if (item.PostDisplayModel.AttachmentUrls?.Count > 0)
                        {
                            // 3
                            section.WithAccessory(new ThumbnailBuilder().WithMedia(item.PostDisplayModel.AttachmentUrls[0]));
                        }
                    }
                    );
                    // 4
                    c.WithActionRow(actionRow => actionRow
                        // 5
                        .AddComponent(new ButtonBuilder("取消收藏", customId: $"UnFavByView:{PaginatorId}:{item.FavoriteInfo.UserId}:{item.PostDisplayModel.ThreadId}", style: ButtonStyle.Danger))
                    );
                    // 6
                    c.WithSeparator();
                    i++;
                }
                // Web跳转 (2组件: 交互行, 按钮x1)
                c.WithActionRow( actionRow => actionRow
                    .AddComponent(new ButtonBuilder("或前往 Web 页面进行管理或查看", style: ButtonStyle.Link, url: "https://opizontas.org/"))
                );
            }
        );
        // 翻页部分: 5 按钮 + 1 容器, 6组件
        messageComponent.WithActionRow(PaginationControlsBuilder.Build(this));
        // 30(每item行) + 6(翻页交互) + 2(Web跳转) + 1(Container) + 1(Separator) = 40
        // 目前 discord 限制为 40 个组件
        return messageComponent.Build();
    }
}