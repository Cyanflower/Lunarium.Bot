// .NET 核心库
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Model;


namespace LunariumBot.Net.SearchBot.Utils;

public class SearchCriteriaBuilder
{
    private readonly DatabaseService _db;
    private readonly ConfigLoader _configLoader;

    private readonly ILogger _logger;

    public SearchCriteriaBuilder(DatabaseService db, ConfigLoader configLoader, ILogger logger)
    {
        _db = db;
        _configLoader = configLoader;
        _logger = logger.ForContext("SearchCriteriaBuilder");
    }

    public async Task<SearchCriteria> BuildAsync(
        IInteractionContext context,
        string? forumChannel, IUser? author,
        string? keyWord1, string? keyWord2, string? keyWord3,
        string? exKeyWord1, string? exKeyWord2, string? exKeyWord3,
        string? includeTag1, string? includeTag2, string? includeTag3, string? includeTag4, string includeMethod,
        string? excludeTag1, string? excludeTag2, string? excludeTag3, string? excludeTag4, string excludeMethod,
        IUser? exAuthor1, IUser? exAuthor2, IUser? exAuthor3,
        string orderBy, string? startTime, string? endTime, int resultLimit
    )
    {
        var criteria = new SearchCriteria
        {
            AuthorId = author?.Id,
            OrderBy = orderBy,
            ResultLimit = resultLimit
        };

        // 解析论坛分区
        if (!string.IsNullOrWhiteSpace(forumChannel))
        {
            if (_configLoader.Config.GuildsConfig.ScanChannelIDS.TryGetValue(forumChannel, out var channelIds))
            {
                criteria.ForumChannelIds.AddRange(channelIds);
            }
            _logger.Debug("解析分区 '{forumChannel}' -> 找到 {channelIds.Count} 个频道ID", forumChannel, channelIds?.Count ?? 0);
        }
        
        // 解析关键词
        criteria.IncludeKeywords = ParseKeywords(keyWord1, keyWord2, keyWord3);
        criteria.ExcludeKeywords = ParseKeywords(exKeyWord1, exKeyWord2, exKeyWord3);

        // 解析标签
        var includeTags = new[] { includeTag1, includeTag2, includeTag3, includeTag4 };
        if (includeMethod == "AND")
        {
            criteria.IncludeTagsAnd = await ParseTagsToIdGroupsAsync(includeTags);
        }
        else // 默认 OR
        {
            criteria.IncludeTagsOr = await ParseTagsToFlatIdListAsync(includeTags);
        }

        var excludeTags = new[] { excludeTag1, excludeTag2, excludeTag3, excludeTag4 };
        if (excludeMethod == "AND")
        {
            criteria.ExcludeTagsAnd = await ParseTagsToIdGroupsAsync(excludeTags);
        }
        else // 默认 OR
        {
            criteria.ExcludeTagsOr = await ParseTagsToFlatIdListAsync(excludeTags);
        }

        // 解析排除作者
        criteria.ExcludeAuthorIds = new[] { exAuthor1, exAuthor2, exAuthor3 }
            .Where(u => u != null).Select(u => u!.Id).ToList();

        // 自动合入用户的屏蔽名单
        var userBlockedAuthors = await _db.GetBlockedAuthorsAsync(context.User.Id);
        if (userBlockedAuthors.Any())
        {
            criteria.ExcludeAuthorIds.AddRange(userBlockedAuthors);
            // 去重，防止手动排除的作者和屏蔽列表中的重复
            criteria.ExcludeAuthorIds = criteria.ExcludeAuthorIds.Distinct().ToList();
            _logger.Debug("为用户 {context.User.Username} 合入了 {userBlockedAuthors.Count} 个屏蔽作者到搜索条件中。", context.User.Username, userBlockedAuthors.Count);
        }
            
        // 解析时间
        (criteria.StartTimestamp, criteria.EndTimestamp) = ParseTimeRange(startTime, endTime);

        return criteria;
    }

    private List<List<string>> ParseKeywords(params string?[] keywordGroups)
    {
        var result = new List<List<string>>();
        foreach (var group in keywordGroups)
        {
            if (string.IsNullOrWhiteSpace(group)) continue;

            var keywords = Regex.Split(group, @"[,\s]+")
                                .Where(k => !string.IsNullOrWhiteSpace(k))
                                .ToList();
            if (keywords.Any())
            {
                result.Add(keywords);
            }
        }
        return result;
    }

    /// <summary>
    /// 用于AND模式：将每个标签名解析为其所有ID，形成一个ID列表（组）。
    /// </summary>
    private async Task<List<List<ulong>>> ParseTagsToIdGroupsAsync(params string?[] tagNames)
    {
        var result = new List<List<ulong>>();
        var uniqueTagNames = tagNames.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
        if (!uniqueTagNames.Any()) return result;

        var tagIdMap = await _db.GetTagIdsByNamesAsync(uniqueTagNames);

        foreach (var tagName in uniqueTagNames)
        {
            if (tagIdMap.TryGetValue(tagName, out var ids) && ids.Any())
            {
                result.Add(ids);
                _logger.Debug("解析AND标签 '{tagName}' -> 找到 {ids.Count} 个ID组", tagName, ids.Count);
            }
        }
        return result;
    }
    
    private async Task<List<ulong>> ParseTagsToFlatIdListAsync(params string?[] tagNames)
    {
        var uniqueTagNames = tagNames.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).Distinct().ToList();
        if (!uniqueTagNames.Any()) return new List<ulong>();

        var tagIdMap = await _db.GetTagIdsByNamesAsync(uniqueTagNames);

        var allTagIds = tagIdMap.Values.SelectMany(ids => ids).Distinct().ToList();
        _logger.Debug("解析OR标签 {uniqueTagNames} -> 找到 {allTagIds.Count} 个唯一ID", string.Join(", ", uniqueTagNames, allTagIds.Count));
        return allTagIds;
    }

    private (long?, long?) ParseTimeRange(string? startStr, string? endStr)
    {
        long? startTimestamp = null;
        long? endTimestamp = null;
        if (!string.IsNullOrWhiteSpace(startStr))
        {
            startTimestamp = DateParser.ParseToUnixTimestamp(startStr, isEndTime: false);
        }

        if (!string.IsNullOrWhiteSpace(endStr))
        {
            // 包含当天，所以时间是当天结束时
            endTimestamp = DateParser.ParseToUnixTimestamp(endStr, isEndTime: true);
        }

        return (startTimestamp, endTimestamp);
    }
}