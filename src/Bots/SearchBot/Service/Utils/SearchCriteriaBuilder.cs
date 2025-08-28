using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;
using SearchBot.Database;
using SearchBot.Model;

namespace SearchBot.Utils;

public class SearchCriteriaBuilder
{
    private readonly DatabaseService _db;
    private readonly ConfigLoader _config;

    private readonly ILogger<SearchCriteriaBuilder> _logger;

    public SearchCriteriaBuilder(DatabaseService db, ConfigLoader config, ILogger<SearchCriteriaBuilder> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<SearchCriteria> BuildAsync(
        string? forumChannel,
        IUser? author,
        string? keyWord1, string? keyWord2, string? keyWord3,
        string? tags1and, string? tags2and, string? tags3and,
        string? tags1or, string? tags2or, string? tags3or,
        string? exTags1, string? exTags2, string? exTags3, string? exTags4,
        string? exKeyWord1, string? exKeyWord2, string? exKeyWord3,
        IUser? exAuthor1, IUser? exAuthor2, IUser? exAuthor3,
        string orderBy,
        string? startTime, string? endTime,
        int resultLimit
    )
    {
        var criteria = new SearchCriteria
        {
            AuthorId = author?.Id,
            OrderBy = orderBy,
            ResultLimit = resultLimit
        };

        // 1. 解析论坛分区
        if (!string.IsNullOrWhiteSpace(forumChannel))
        {
            if (_config.GuildsConfig.ScanChannelIDS.TryGetValue(forumChannel, out var channelIds))
            {
                criteria.ForumChannelIds.AddRange(channelIds);
            }
            _logger.LogInformation("解析分区 '{ForumChannel}' -> 找到 {Count} 个频道ID", forumChannel, channelIds?.Count ?? 0);
        }
        
        // 2. 解析关键词
        criteria.IncludeKeywords = ParseKeywords(keyWord1, keyWord2, keyWord3);
        criteria.ExcludeKeywords = ParseKeywords(exKeyWord1, exKeyWord2, exKeyWord3);

        // 3. 解析标签
        criteria.IncludeTagsAnd = await ParseTagsToIdsAsync(tags1and, tags2and, tags3and);
        criteria.IncludeTagsOr = await ParseTagsToIdsAsync(tags1or, tags2or, tags3or);
        criteria.ExcludeTags = await ParseTagsToIdsAsync(exTags1, exTags2, exTags3, exTags4);

        // 4. 解析排除作者
        criteria.ExcludeAuthorIds = new[] { exAuthor1, exAuthor2, exAuthor3 }
            .Where(u => u != null)
            .Select(u => u!.Id)
            .ToList();
            
        // 5. 解析时间
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

    private async Task<List<ulong>> ParseTagsToIdsAsync(params string?[] tagNames)
    {
        var uniqueTagNames = tagNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct();

        if (!uniqueTagNames.Any())
        {
            return new List<ulong>();
        }

        // 这是魔法发生的地方！我们一次性从数据库查询所有需要的标签名
        var tagIdMap = await _db.GetTagIdsByNamesAsync(uniqueTagNames.ToList());
        
        // 展平所有找到的ID列表，并去重
        var allTagIds = tagIdMap.Values.SelectMany(ids => ids).Distinct().ToList();

        _logger.LogInformation("解析标签 {TagNames} -> 找到 {Count} 个唯一标签ID", string.Join(", ", uniqueTagNames), allTagIds.Count);

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