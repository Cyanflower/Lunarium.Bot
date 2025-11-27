using Discord;

namespace LunariumBot.Net.SearchBot.Paginator;

public abstract class BasePaginatedView<T> : IPaginatedView
{
    public string PaginatorId { get; }
    public DateTimeOffset ExpiresAt { get; private set; } 
    protected int _pageSize;
    protected int _totalPages;
    public int CurrentPageIndex => _currentPageIndex;
    public int TotalPages => _totalPages;
    protected readonly List<T> _items;
    protected List<Page<T>> _pages = new();
    // 使用 0-based 索引来操作 _pages 列表
    protected int _currentPageIndex = 0;

    public BasePaginatedView(IEnumerable<T> items, DateTimeOffset expiresAt, string id, int pageSize = 5)
    {
        // 确保传入的数据不为 null
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }
        ExpiresAt = expiresAt;
        PaginatorId = id;
        _pageSize = pageSize;
        _items = items.ToList();
        BuildPages();
    }

    #region 状态操作 (Commands) - 只修改状态
    public void RefreshExpiration(TimeSpan duration)
    {
        ExpiresAt = DateTimeOffset.UtcNow.Add(duration);
    }

    public IPaginatedView FirstPage()
    {
        _currentPageIndex = 0;
        return this;
    }

    public IPaginatedView PrevPage()
    {
        if (_currentPageIndex > 0)
        {
            _currentPageIndex--;
        }
        return this;
    }
    
    public IPaginatedView NextPage()
    {
        if (_currentPageIndex < _totalPages)
        {
            _currentPageIndex++;
        }
        return this;
    }

    public IPaginatedView LastPage()
    {
        _currentPageIndex = _totalPages > 0 ? _totalPages - 1 : 0;
        return this;
    }

    public IPaginatedView ToPage(int page)
    {
        // 使用 Math.Clamp 将页码限制在有效范围内
        _currentPageIndex = Math.Clamp(page, 0, _totalPages > 0 ? _totalPages : 1);
        return this;
    }

    public IPaginatedView RemoveItem(int itemIndex)
    {
        try
        {
            var targetItem = _pages[_currentPageIndex].GetItemByIndex(itemIndex);
            _items.RemoveAll(item => Object.ReferenceEquals(item, targetItem));
            BuildPages();
            return this;
        }
        catch
        {
            throw;
        }
    }
    #endregion

    #region 渲染 (Query) - 根据当前状态生成视图
    public abstract MessageComponent Render();
    #endregion

    /// <summary>
    /// 核心的页面构建逻辑。
    /// </summary>
    protected void BuildPages()
    {
        _pages = new List<Page<T>>();
        if (!_items.Any())
        {
            return;
        }

        for (int i = 0; i < _items.Count; i += _pageSize)
        {
            // 从主列表取出一小块数据
            var pageItems = _items.GetRange(i, Math.Min(_pageSize, _items.Count - i));
            _pages.Add(new Page<T>(pageItems));
        }

        _totalPages = _pages.Count;

        // 确保当前页索引不会越界
        _currentPageIndex = Math.Clamp(_currentPageIndex, 0, _pages.Count - 1);
    }
}


/// <summary>
/// 代表一个分页视图中的单个页面。
/// </summary>
public class Page<T>
{
    /// <summary>
    /// 此页面包含的项目列表。
    /// </summary>
    public List<T> Items { get; }

    public Page(List<T> items)
    {
        Items = items;
    }

    public T GetItemByIndex(int index)
    {
        // 保证访问页面内元素不会数组越界
        if (index < 0 || index > Items.Count) throw new ArgumentOutOfRangeException(nameof(index), "索引越界: 访问页面内元素的索引参数的值不在页面列表的索引范围内");
        return Items[index];
    }
}
