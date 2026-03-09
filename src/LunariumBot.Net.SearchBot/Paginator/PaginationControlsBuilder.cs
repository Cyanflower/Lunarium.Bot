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

    public const string ModalIdTemplate = "pg_modal";

    public static ActionRowBuilder Build(IPaginatedView view)
    {
        var paginatorId = view.PaginatorId;
        var currentPage = view.CurrentPageIndex + 1; // 转换为 1-based 用于显示
        var totalPages = view.TotalPages;

        var firstButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionFirst}:{paginatorId}")
            .WithEmote(new Emoji("⏮️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == 1);

        var prevButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionPrev}:{paginatorId}")
            .WithEmote(new Emoji("◀️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == 1);

        var pageButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionShowPageModal}:{paginatorId}")
            .WithLabel($"{currentPage} / {totalPages}")
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(totalPages <= 1);

        var nextButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionNext}:{paginatorId}")
            .WithEmote(new Emoji("▶️"))
            .WithStyle(ButtonStyle.Secondary)
            .WithDisabled(currentPage == totalPages);

        var lastButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionLast}:{paginatorId}")
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