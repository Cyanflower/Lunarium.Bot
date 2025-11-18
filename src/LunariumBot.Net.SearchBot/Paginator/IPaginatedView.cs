using Discord;

namespace LunariumBot.Net.SearchBot.Paginator;

public interface IPaginatedView
{
    public string PaginatorId { get; }
    public DateTimeOffset ExpiresAt { get; }
    /// <summary>
    /// 0-based
    /// </summary>
    int CurrentPageIndex { get; }
    int TotalPages { get; }
    public IPaginatedView PreviousPage();
    public IPaginatedView NextPage();
    public IPaginatedView ToPage(int page);
    public MessageComponent Render();
}
