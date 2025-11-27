using Discord;
using LunariumBot.Net.Logger;
using LunariumBot.Net.SearchBot.Models;

namespace LunariumBot.Net.SearchBot.Paginator;

public class BlockPaginatedView : BasePaginatedView<BlockedInfo>, IPaginatedView
{
    
    const int PageSize = 10;
    // 使用 : base() 将参数传递给父类的构造函数
    // 构造函数体可以是空的，因为所有的初始化逻辑都在基类中完成了。
    public BlockPaginatedView(IEnumerable<BlockedInfo> items, DateTimeOffset expiresAt, string id) : base(items, expiresAt, id, 10) { }

    public BlockedInfo? GetItemByIndex(int index)
    {
        if (index < 0 || index >= _pages[_currentPageIndex].Items.Count)
        {
            return null;
        }
        return _pages[_currentPageIndex].GetItemByIndex(index);
    }
    
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
                // 3 * 10, 30个组件
                foreach (var item in _pages[_currentPageIndex].Items)
                {
                    c.WithSection(section => section
                        .WithTextDisplay($"""
                            ## 用户: <@{item.BlockedAuthorId}>
                            添加于: <t:{item.AddedAt}:R> | <t:{item.AddedAt}:D>
                            """)
                        .WithAccessory(new ButtonBuilder("取消屏蔽", $"UnBlockByList:{PaginatorId}.{i}", ButtonStyle.Primary))
                    );
                    i++;
                }
                c.WithAccentColor(Color.Blue);
                // 分隔线一个组件
                c.WithSeparator();
                // 翻页部分: 5 按钮 + 1 容器, 6组件
                c.WithActionRow(PaginationControlsBuilder.Build(this));
            }
        );
        // 30(每item行) + 6(翻页交互) + 1(Container) + 1(Separator) = 38
        // 目前 discord 限制为 40 个组件
        return messageComponent.Build();
    }
    
}