// 在 Paginator 文件夹下创建一个新文件 PaginationControlsBuilder.cs
using Discord;

namespace LunariumBot.Net.SearchBot.Paginator;

public static class PaginationControlsBuilder
{
    // 定义通用的 Action 常量
    public const string ActionFirst = "first";
    public const string ActionPrev = "prev";
    public const string ActionShowPageModal = "show_page_modal";
    public const string ActionNext = "next";
    public const string ActionLast = "last";

    // 你甚至可以为 Modal 定义一个 CustomId 模板
    public const string ModalIdTemplate = "pg_modal:{0}"; // {0} 会被替换为 PaginatorId

    public static ActionRowBuilder Build(IPaginatedView view)
    {
        var paginatorId = view.PaginatorId;
        var currentPage = view.CurrentPageIndex + 1; // 转换为 1-based 用于显示
        var totalPages = view.TotalPages;

        var firstButton = new ButtonBuilder()
            .WithCustomId($"pg:{paginatorId}:{ActionFirst}")
            .WithEmote(new Emoji("⏮️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == 1);

        var prevButton = new ButtonBuilder()
            .WithCustomId($"pg:{paginatorId}:{ActionPrev}")
            .WithEmote(new Emoji("◀️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == 1);

        var pageButton = new ButtonBuilder()
            .WithCustomId($"pg:{paginatorId}:{ActionShowPageModal}")
            .WithLabel($"{currentPage} / {totalPages}")
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(totalPages <= 1);

        var nextButton = new ButtonBuilder()
            .WithCustomId($"pg:{paginatorId}:{ActionNext}")
            .WithEmote(new Emoji("▶️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == totalPages);

        var lastButton = new ButtonBuilder()
            .WithCustomId($"pg:{paginatorId}:{ActionLast}")
            .WithEmote(new Emoji("⏭️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == totalPages);

        return new ActionRowBuilder()
            .AddComponent(firstButton)
            .AddComponent(prevButton)
            .AddComponent(pageButton)
            .AddComponent(nextButton)
            .AddComponent(lastButton);
    }
}