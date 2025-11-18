using Discord;

using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot.Paginator;

public class BlockPaginatedView : BasePaginatedView<BlackedInfo>, IPaginatedView
{
    const int PageSize = 9;
    // 使用 : base() 将参数传递给父类的构造函数
    // 构造函数体可以是空的，因为所有的初始化逻辑都在基类中完成了。
    public BlockPaginatedView(IEnumerable<BlackedInfo> items, DateTimeOffset expiresAt, string id) : base(items, expiresAt, id, 10) { }

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
                        .WithAccessory(new ButtonBuilder("取消屏蔽", $"CancelBlock:{PaginatorId}.{i}", ButtonStyle.Danger))
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