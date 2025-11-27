using Discord;
using Discord.Interactions;

using LunariumBot.Net.Extensions;

using LunariumBot.Net.SearchBot.Models;


namespace LunariumBot.Net.SearchBot.AppInteractionService;

public class InteractionUtils : InteractionModuleBase<SocketInteractionContext>
{
    public MessageComponent BuildInteractiveComponents(string uuid, int currentPage, int totalPages, List<PostDisplayModel> posts, PaginatorType type, IInteractionContext context)
    {
        var builder = new ComponentBuilder();

        // 1. 添加翻页按钮
        bool isFirstPage = currentPage == 1;
        bool isLastPage = currentPage == totalPages;
        builder.WithButton("首页", $"IP_first_{currentPage}_{uuid}", style: ButtonStyle.Primary, disabled: isFirstPage, row: 0);
        builder.WithButton("上一页", $"IP_prev_{currentPage}_{uuid}", style: ButtonStyle.Primary, disabled: isFirstPage, row: 0);
        builder.WithButton($"{currentPage}/{totalPages}", $"IP_jump_{uuid}", style: ButtonStyle.Secondary, row: 0);
        builder.WithButton("下一页", $"IP_next_{currentPage}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage, row: 0);
        builder.WithButton("尾页", $"IP_last_{currentPage}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage, row: 0);

        // 2. 添加删除按钮
        int i = 1;
        int row = 1;
        foreach (var post in posts)
        {
            string customIdPrefix = type == PaginatorType.Favorite ? "rm_F_inter" : "rm_ST_inter";
            string labelPrefix = type == PaginatorType.Favorite ? "移除收藏" : "移除订阅";
            // 截断标题以防超过按钮标签限制
            var label = $"{labelPrefix}: {(post.Title != null ? post.Title.Slice(15, "...") : "未知帖子, 请勿点击")}";
            builder.WithButton(label, $"{customIdPrefix}_{post.ThreadId}_{uuid}", ButtonStyle.Danger, row: row);
            if (i++ % 4 == 0) row++;
        }

        return builder.Build();
    }
}