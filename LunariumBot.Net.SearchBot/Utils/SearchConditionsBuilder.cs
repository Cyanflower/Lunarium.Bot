
using LunariumBot.Net.Extensions;
using LunariumBot.Net.SearchBot.Model;


namespace LunariumBot.Net.SearchBot.Utils;

public class SearchConditionsBuilder
{

    private SearchMode _keywordSearchMode = SearchMode.Fuzzy;
    private List<string> _includeKeywords = new();
    private List<string> _excludeKeywords = new();
    private SearchMode _tagSearchMode = SearchMode.Fuzzy;
    private List<long> _includeTagIds = new();
    private List<long> _excludeTagIds = new();
    private List<long> _authorIds = new();
    private List<long> _channelIds = new();
    private PostSortOption _postSortOption = PostSortOption.CreatedTimeDesc;
    private long? _currentUserId { get; set; } 

    public SearchConditionsBuilder() { }
    public SearchConditionsBuilder WithKeywordSearchMode(SearchMode searchMode = SearchMode.Fuzzy)
    {
        _keywordSearchMode = searchMode;
        return this;
    }

    public SearchConditionsBuilder WithIncludeKeywords(string? includeKeywords)
    {
        if (includeKeywords == null) return this;
        _includeKeywords = KeywordParser.ParseQueryString(includeKeywords);
        return this;
    }

    public SearchConditionsBuilder WithExcludeKeywords(string? excludeTagIds)
    {
        if (excludeTagIds == null) return this;
        _excludeKeywords = KeywordParser.ParseQueryString(excludeTagIds);
        return this;
    }

    public SearchConditionsBuilder WithTagSearchMode(SearchMode searchMode = SearchMode.Fuzzy)
    {
        _tagSearchMode = searchMode;
        return this;
    }

    public SearchConditionsBuilder WithIncludeTags(List<ulong>? includeTagIds)
    {
        if (includeTagIds == null) return this;
        _includeTagIds = includeTagIds.Select(id => id.ToDbId()).ToList();
        return this;
    }

    public SearchConditionsBuilder WithExcludeTags(List<ulong>? excludeTagIds)
    {
        if (excludeTagIds == null) return this;
        _excludeTagIds = excludeTagIds.Select(id => id.ToDbId()).ToList();
        return this;
    }
    

    public SearchConditionsBuilder WithAuthorId(List<ulong>? authorIds)
    {
        if (authorIds == null) return this;
        _authorIds = authorIds.Select(id => id.ToDbId()).ToList();
        return this;
    }

    public SearchConditionsBuilder WithChannelId(List<ulong>? channnelIds)
    {
        if (channnelIds == null) return this;
        _channelIds = channnelIds.Select(id => id.ToDbId()).ToList();
        return this;
    }

    public SearchConditionsBuilder WithPostSortOption(PostSortOption postSortOption)
    {
        _postSortOption = postSortOption;
        return this;
    }

    public SearchConditionsBuilder WithCurrentUserId(ulong currentUserId)
    {
        _currentUserId = currentUserId.ToDbId();
        return this;
    }

    public SearchConditions Build()
    {
        KeywordConditions? keywordConditions = null;
        // 只有当存在关键词条件时，才创建 KeywordConditions 对象
        if ((_includeKeywords.Count > 0) || (_excludeKeywords.Count > 0))
        {
            keywordConditions = new KeywordConditions()
            {
                SearchMode = _keywordSearchMode,
                IncludeKeywords = _includeKeywords,
                ExcludeKeywords = _excludeKeywords
            };
        }
        TagConditions? tagConditions = null;
        // 只有当存在关键词条件时，才创建 KeywordConditions 对象
        if ((_includeTagIds.Count > 0) || (_excludeTagIds.Count > 0))
        {
            tagConditions = new TagConditions()
            {
                SearchMode = _tagSearchMode,
                IncludeTagIds = _includeTagIds,
                ExcludeTagIds = _excludeTagIds
            };
        }
        return new()
        {
            KeywordConditions = keywordConditions,
            TagConditions = tagConditions,
            AuthorIds = _authorIds,
            ChannelIds = _channelIds,
            PostSortOption = _postSortOption,
            CurrentUserId = _currentUserId
        };
    }
}
