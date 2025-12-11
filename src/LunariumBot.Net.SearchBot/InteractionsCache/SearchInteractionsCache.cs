using Discord;
using Discord.WebSocket;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Paginator;
using LunariumBot.Net.SearchBot.SearchCache;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.InteractionsCache;

public class SearchInteractionCache : BaseInteractionsCache<SearchConditionsBuilder>
{
    // ID 前缀常量
    public const string ActionIncludeKw = "Search:IncludeKw";
    public const string ActionExcludeKw = "Search:ExcludeKw";
    public const string ActionKwExact = "Search:KwExact";
    public const string ActionKwFuzzy = "Search:KwFuzzy";
    public const string ActionAuthor = "Search:Author";
    public const string ActionChannel = "Search:Channel";
    public const string ActionIncludeTag = "Search:IncludeTag";
    public const string ActionExcludeTag = "Search:ExcludeTag";
    public const string ActionTagExact = "Search:TagExact";
    public const string ActionTagFuzzy = "Search:TagFuzzy";
    public const string ActionSort = "Search:Sort";
    public const string ActionConfirm = "Search:Confirm";
    public const string ActionSaveQuickSearch = "Search:SaveQuickSearch";

    public SearchConditionsBuilder Builder => _data;
    private ConfigLoader? _configLoader;
    private SearchCacheService? _searchCache;

    public SearchInteractionCache(SearchConditionsBuilder data, DateTimeOffset expiresAt, string id)
        : base(data, expiresAt, id) { }

    public void SetConfig(ConfigLoader configLoader, SearchCacheService searchCache)
    {
        _configLoader = configLoader;
        _searchCache = searchCache;
    }

    public override MessageComponent Render()
    {
        var builder = new ComponentBuilderV2();

        builder.WithContainer(container =>
        {

            container.WithAccentColor(Color.Blue);
            // 1
            container.WithTextDisplay($"## 搜索");
            // 2
            container.WithSeparator();
            // 3
            container.WithTextDisplay($"""
                -# - 搜索模式说明:
                > -# - 精确模式: 必须包含所有关键词/标签
                > -# - 模糊模式: 包含任意一个关键词/标签
                """);
            // 4
            container.WithSeparator();
            // 关键词
            string keyword = "无";
            if (_data.IncludeKeywords.Count > 0)
            {
                if (_data.KeywordSearchMode == SearchMode.Exact)
                    keyword = string.Join("  &  ", _data.IncludeKeywords);
                else
                    keyword = string.Join("  |  ", _data.IncludeKeywords);
            }
            string excludeKeyword = "无";
            if (_data.ExcludeKeywords.Count > 0)
            {
                if (_data.KeywordSearchMode == SearchMode.Exact)
                    excludeKeyword = string.Join("  &  ", _data.ExcludeKeywords);
                else
                    excludeKeyword = string.Join("  |  ", _data.ExcludeKeywords);
            }
            // 5
            container.WithSection(section =>
            {
                // 6
                section.WithTextDisplay($"""
                    包含关键词: 在搜索时要包含的关键词
                    当前关键词: {keyword}
                    """);
                // 7
                // Modal
                section.WithAccessory(new ButtonBuilder(
                    label: "关键词",
                    customId: $"{ActionIncludeKw}:{CacheId}",
                    style: ButtonStyle.Success
                    ));
            });
            // 8
            container.WithSection(section =>
            {
                // 9
                section.WithTextDisplay($"""
                    排除关键词: 在搜索时要包含的关键词
                    当前排除词: {excludeKeyword}
                    """);
                // 10
                // Modal
                section.WithAccessory(new ButtonBuilder(
                    label: "排除词",
                    customId: $"{ActionExcludeKw}:{CacheId}",
                    style: ButtonStyle.Danger
                ));
            });
            // 11
            container.WithTextDisplay($"""
                当前模式: {(_data.KeywordSearchMode == SearchMode.Exact ? "精确模式" : "模糊模式")}
                """);
            // 12
            container.WithActionRow(row =>
            {
                // 13
                row.WithButton(
                    label: "精确搜索",
                    customId: $"{ActionKwExact}:{CacheId}",
                    style: ButtonStyle.Primary,
                    disabled: _data.KeywordSearchMode == SearchMode.Exact
                );
                // 14
                row.WithButton(
                    label: "模糊搜索",
                    customId: $"{ActionKwFuzzy}:{CacheId}",
                    style: ButtonStyle.Primary,
                    disabled: _data.KeywordSearchMode == SearchMode.Fuzzy
                );
            });

            // 15
            container.WithSeparator();
            // 16
            container.WithTextDisplay($"""
                选择要搜索的作者
                """);
            // 作者
            // 17
            container.WithActionRow(row =>
            {
                // 18
                row.WithSelectMenu(new SelectMenuBuilder()
                    .WithCustomId($"{ActionAuthor}:{CacheId}")
                    .WithPlaceholder("选择一个作者")
                    .WithType(ComponentType.UserSelect)
                    .WithMinValues(1)
                    .WithMaxValues(1));
            });

            // 19
            // 选择频道
            container.WithSeparator();
            // 20
            // container.WithTextDisplay($"""
            //     选择要搜索的频道
            //     选择的频道: {(
            //         _data.ChannelIds.Count > 0 ?
            //         string.Join(", ", _searchCache!.ChannelCache
            //             .Where(kv => _data.ChannelIds.Contains(kv.Value.ChannelId.ToDbId()))
            //             .Select(kv => kv.Value.ChannelName).ToList())
            //         : "无限制"
            //     )}
            //     """);
            container.WithTextDisplay($"""
                选择要搜索的频道
                """);
            // 21
            container.WithActionRow(row =>
            {
                // 22
                var options = _searchCache!.ChannelCache
                    .Where(kv => _configLoader!.ValidChannelSet.Contains(kv.Value.ChannelId))
                    .Select(kv => new SelectMenuOptionBuilder(
                        label: kv.Value.ChannelName,
                        value: kv.Value.ChannelId.ToString()
                    ))
                    .ToList();
                // 23
                row.WithSelectMenu(
                    customId: $"{ActionChannel}:{CacheId}",
                    options: options,
                    placeholder: "选择要搜索的频道...",
                    minValues: 1,
                    maxValues: 1
                );
            });

            // 23
            container.WithSeparator();
            // 24
            container.WithTextDisplay($"""
                {(_data.ChannelIds.Count == 0 ? "如需使用标签筛选, 请先选择频道" : "选择包含标签")}
                """);

            // 选择包含标签
            // 25
            container.WithActionRow(row =>
            {
                var options = _searchCache!.TagCache
                    .Where(kv => _data.ChannelIds.Contains(kv.Value.ChannelId.ToDbId()))
                    .Select(kv => new SelectMenuOptionBuilder(
                        label: kv.Value.TagName,
                        value: kv.Value.TagId.ToString()
                    ))
                    .ToList();
                 
                // 26
                row.WithSelectMenu(
                    customId: $"{ActionIncludeTag}:{CacheId}",
                    options: options,
                    placeholder: "选择包含的标签...",
                    minValues: 1,
                    maxValues: 25,
                    disabled: _data.ChannelIds.Count == 0
                );
            });
            // 27
            container.WithTextDisplay($"""
                {(_data.ChannelIds.Count == 0 ? "如需使用标签筛选, 请先选择频道" : "选择排除标签")}
                """);
            // 选择排除标签
            // 28
            container.WithActionRow(row =>
            {
                var options = _searchCache!.TagCache
                    .Where(kv => _data.ChannelIds.Contains(kv.Value.ChannelId.ToDbId()))
                    .Select(kv => new SelectMenuOptionBuilder(
                        label: kv.Value.TagName,
                        value: kv.Value.TagId.ToString()
                    ))
                    .ToList();
                
                // 29
                row.WithSelectMenu(
                    customId: $"{ActionExcludeTag}:{CacheId}",
                    options: options,
                    placeholder: "选择排除的标签...",
                    minValues: 1,
                    maxValues: 25,
                    disabled: _data.ChannelIds.Count == 0
                );
            });
            // 30
            container.WithActionRow(row =>
            {
                // 31
                row.WithButton(
                    label: "精确搜索",
                    customId: $"{ActionTagExact}:{CacheId}",
                    style: ButtonStyle.Primary,
                    disabled: _data.TagSearchMode == SearchMode.Exact
                );
                // 32
                row.WithButton(
                    label: "模糊搜索",
                    customId: $"{ActionTagFuzzy}:{CacheId}",
                    style: ButtonStyle.Primary,
                    disabled: _data.TagSearchMode == SearchMode.Fuzzy
                );
            });
            // 排序方式
            // 33
            container.WithSeparator();
            // 34
            container.WithActionRow(row =>
            {
                List<SelectMenuOptionBuilder> options =
                [
                    new("相关性 (从高到低)", $"{PostSortOption.RelevanceDesc}"),
                    new("按创建时间降序 (从新到旧)", $"{PostSortOption.CreatedTimeDesc}"),
                    new("按创建时间升序 (从旧到新)", $"{PostSortOption.CreatedTimeAsc}"),
                    new("按更新时间降序 (从新到旧)", $"{PostSortOption.UpdateTimeDesc}"),
                    new("按更新时间升序 (从旧到新)", $"{PostSortOption.UpdateTimeAsc}"),
                    new("按点赞数降序 (从多到少)", $"{PostSortOption.ReactionDesc}"),
                    new("按点赞数升序 (从少到多)", $"{PostSortOption.ReactionAsc}"),
                    new("按评论数降序 (从多到少)", $"{PostSortOption.ReplyCountDesc}"),
                    new("按评论数升序 (从少到多)", $"{PostSortOption.ReplyCountAsc}"),
                ];
                // 36
                row.WithSelectMenu(
                    customId: $"{ActionSort}:{CacheId}",
                    options: options,
                    placeholder: "选择排除的标签...",
                    minValues: 1,
                    maxValues: 1
                );
            });

            // 确认/设为快捷搜索
            // 36
            container.WithSeparator();
            // 37
            container.WithActionRow(row =>
            {
                // 38
                row.WithButton(
                    label: "搜索",
                    customId: $"{ActionConfirm}:{CacheId}",
                    style: ButtonStyle.Success,
                    emote: new Emoji("🔍")
                );
                // 39
                // modal
                // row.WithButton(
                //     label: "保存当前条件为快捷搜索",
                //     customId: $"{ActionSaveQuickSearch}:{CacheId}",
                //     style: ButtonStyle.Secondary,
                //     emote: new Emoji("💾")
                // );
            });
        });

        return builder.Build();
    }
}