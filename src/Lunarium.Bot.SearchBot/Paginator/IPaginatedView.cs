using Discord;

namespace Lunarium.Bot.SearchBot.Paginator;

public interface IPaginatedView
{
    public string PaginatorId { get; }
    public DateTimeOffset ExpiresAt { get; }
    /// <summary>
    /// 0-based
    /// </summary>
    int CurrentPageIndex { get; }
    int TotalPages { get; }
    public void RefreshExpiration(TimeSpan duration);
    public IPaginatedView FirstPage();
    public IPaginatedView PrevPage();
    public IPaginatedView NextPage();
    public IPaginatedView LastPage();
    public IPaginatedView ToPage(int page);
    public IPaginatedView RemoveItem(int itemIndex);
    public MessageComponent Render();
}
