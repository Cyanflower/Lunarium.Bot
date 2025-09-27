// .Net 核心库
using System.Collections.Concurrent;

// NuGet 包依赖
using Discord;
using Discord.Interactions;
using Coravel.Invocable;

// 解决方案内引用项目
using LunariumBot.Net.Logger;
using LunariumBot.Net.Extensions;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

/// <summary>
/// 为论坛分区名称提供自动补全建议。
/// 这些建议是固定的，从配置文件中加载。
/// </summary>
public class ForumCategoriesAutoCompletion : AutocompleteHandler
{
    private readonly ConfigLoader _configLoader;
    private readonly List<string> _forumCategories;
    private readonly ILogger _logger;

    // 通过依赖注入获取配置加载器
    public ForumCategoriesAutoCompletion(ILogger logger, ConfigLoader configLoader)
    {
        _logger = logger;
        _configLoader = configLoader;
        // 在构造函数中预先加载好建议列表
        _forumCategories = _configLoader.Config.GuildsConfig.ScanChannelIDS.Keys.ToList();
    }

    /// <summary>
    /// 生成建议的核心方法
    /// </summary>
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            // 获取用户当前输入的值
            string? userInput = (autocompleteInteraction.Data.Current.Value as string)?.ToLower();

            // 筛选出包含用户输入的分类
            var filteredCategories = string.IsNullOrWhiteSpace(userInput)
                ? _forumCategories // 如果用户还没输入，就显示所有
                : _forumCategories.Where(c => c.ToLower().Contains(userInput));

            // 创建自动补全的结果列表
            // 注意：Discord最多只显示25个选项
            var results = filteredCategories
                .Select(category => new AutocompleteResult(category, category))
                .Take(25) 
                .ToList();

            // 返回成功的结果
            return Task.FromResult(AutocompletionResult.FromSuccess(results));
        }
        catch (Exception ex)
        {
            // 如果发生错误，记录日志，并返回一个空列表
            _logger.Error("ForumCategoriesAutoCompletion",ex);
            return Task.FromResult(AutocompletionResult.FromError(ex));
        }
    }
}

/// <summary>
/// 为用户自己的收藏夹类别提供自动补全建议。
/// 这些建议是动态生成的，会根据当前用户的ID查询数据库。
/// </summary>
public class FavoriteClassAutoCompletion : AutocompleteHandler
{
    private readonly DatabaseService _databaseService;
    private readonly ILogger _logger;

    // 通过依赖注入获取的数据库服务
    public FavoriteClassAutoCompletion(ILogger logger, DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    /// <summary>
    /// 动态地为每个用户生成他们自己的收藏夹建议。
    /// </summary>
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            var userId = context.User.Id;
            string? userInput = (autocompleteInteraction.Data.Current.Value as string)?.ToLower();

            // 从数据库获取该用户的收藏夹列表
            var favoriteTypes = await _databaseService.GetUserFavoriteTypeAsync(userId);

            if (favoriteTypes == null || !favoriteTypes.Any())
            {
                // 如果用户还没有任何收藏夹，就返回一个 默认分类 的列表
                var defaultSuggestion = new List<AutocompleteResult>
                {
                    new AutocompleteResult("未分类", "未分类")
                };
                return AutocompletionResult.FromSuccess(defaultSuggestion);
            }

            // 根据用户输入进行筛选
            var filteredFavorites = string.IsNullOrWhiteSpace(userInput)
                ? favoriteTypes
                : favoriteTypes.Where(f => f.ToLower().Contains(userInput));

            // 构建返回结果
            var results = filteredFavorites
                .Select(type => new AutocompleteResult(type, type))
                .Take(25) // 最多25个的限制
                .ToList();

            return AutocompletionResult.FromSuccess(results);
        }
        catch (Exception ex)
        {
            // 记录异常，并返回一个安全的错误结果
            _logger.Error("FavoriteClassAutoCompletion",ex);
            return AutocompletionResult.FromError(ex);
        }
    }
}

/// <summary>
/// 为标签提供动态自动补全建议。
/// 1. 如果选择了"论坛分区"，则只显示该分区下的标签，按使用次数排序。
/// 2. 如果未选择分区，则显示所有标签，按总使用次数排序，并注明适用分区。
/// 实现了 IInvocable 接口，以便通过调度器定期刷新缓存。
/// </summary>
public class AllTagsAutoCompletion : AutocompleteHandler, IInvocable
{
    private readonly DatabaseService _databaseService;
    private readonly ConfigLoader _configLoader;
    private readonly ILogger _logger;

    // 缓存1: 存储按 "论坛分区" (Category) 分类的标签列表
    private ConcurrentDictionary<string, List<AutocompleteResult>> _categoryTagsCache = new();
    
    // 缓存2: 存储合并后的所有标签列表
    private List<AutocompleteResult> _allTagsCache = new();

    public AllTagsAutoCompletion(ILogger logger, DatabaseService databaseService, ConfigLoader configLoader)
    {
        _databaseService = databaseService;
        _logger = logger;
        _configLoader = configLoader;

        // 在机器人启动时，立即执行一次缓存加载
        // 使用 .Wait() 是因为构造函数不能是 async 的，这在启动时是安全的。
        Invoke().Wait(); 
    }

    /// <summary>
    /// 实现 IInvocable 接口，用于定期刷新标签缓存。
    /// </summary>
    public async Task Invoke()
    {
        _logger.Info("🔄 正在刷新标签自动补全缓存...");
        try
        {
            // 1. 从数据库一次性获取所有标签的详细信息
            var allTagsFromDb = await _databaseService.GetAllTagsWithDetailsAsync();
            if (!allTagsFromDb.Any())
            {
                _logger.Warning("数据库中没有找到任何标签，缓存将为空。");
                _categoryTagsCache.Clear();
                _allTagsCache = new List<AutocompleteResult>();
                return;
            }

            // 2. 生成【合并后】的所有标签缓存 (_allTagsCache)
            var allTagsGrouped = allTagsFromDb
                .GroupBy(t => t.TagName) // 按标签名分组
                .Select(group => new
                {
                    Name = group.Key,
                    // 合并所有适用分区，并去重
                    ApplicableChannels = string.Join(", ", group.Select(t => t.ChannelName).Distinct()),
                    // 将组内所有标签的使用次数相加
                    TotalUsage = group.Sum(t => t.UsageCount) 
                })
                .OrderByDescending(g => g.TotalUsage) // 按总使用次数降序排序
                .Select(g => new AutocompleteResult($"{g.Name} (适用: {g.ApplicableChannels})".Slice(50,"...)"), g.Name)) // 创建 Discord 结果对象
                .ToList();
            
            _allTagsCache = allTagsGrouped;

            // 3. 生成【按分区】分类的标签缓存 (_categoryTagsCache)
            var newCategoryCache = new ConcurrentDictionary<string, List<AutocompleteResult>>();
            var scanChannelsConfig = _configLoader.Config.GuildsConfig.ScanChannelIDS;

            foreach (var category in scanChannelsConfig)
            {
                var categoryName = category.Key;
                var channelIdsForCategory = category.Value.ToHashSet(); // 转为 HashSet 以提高查找效率

                var tagsForCategory = allTagsFromDb
                    .Where(t => t.ChannelId.HasValue && channelIdsForCategory.Contains(t.ChannelId.Value)) // 筛选出属于当前分区的标签
                    .OrderByDescending(t => t.UsageCount) // 按使用次数降序排序
                    .Select(t => new AutocompleteResult(t.TagName, t.TagName)) // 创建 Discord 结果对象
                    .ToList();
                
                newCategoryCache[categoryName] = tagsForCategory;
            }

            _categoryTagsCache = newCategoryCache;

            _logger.Info("✅ 标签自动补全缓存刷新成功！");
        }
        catch (Exception ex)
        {
            _logger.Error("刷新标签自动补全缓存时发生严重错误:", ex);
        }
    }

    /// <summary>
    /// 动态地为用户生成标签建议。
    /// </summary>
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            var userInput = (autocompleteInteraction.Data.Current.Value as string ?? "").ToLower();

            // 魔法在这里！获取 "论坛分区" 参数的值
            var forumChannelValue = autocompleteInteraction.Data.Options
                .FirstOrDefault(o => o.Name == "论坛分区")?.Value?.ToString();

            List<AutocompleteResult> sourceList;

            // 根据 "论坛分区" 是否有值，来决定使用哪个缓存
            if (!string.IsNullOrEmpty(forumChannelValue) && _categoryTagsCache.TryGetValue(forumChannelValue, out var categoryTags))
            {
                // 情况1: 用户选择了有效的分区 -> 使用分区缓存
                sourceList = categoryTags;
            }
            else
            {
                // 情况2: 用户未选择或选择了无效分区 -> 使用合并后的总缓存
                sourceList = _allTagsCache;
            }

            // 根据用户输入过滤结果
            var filteredResults = sourceList
                .Where(r => r.Name.ToLower().Contains(userInput))
                .Take(25);

            return Task.FromResult(AutocompletionResult.FromSuccess(filteredResults));
        }
        catch (Exception ex)
        {
            _logger.Error("生成标签建议时出错:", ex);
            return Task.FromResult(AutocompletionResult.FromError(ex));
        }
    }
}