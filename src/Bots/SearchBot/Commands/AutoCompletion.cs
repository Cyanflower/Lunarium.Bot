using Microsoft.Extensions.Logging;

using Discord;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.Model;

namespace SearchBot.AppInteractionService;

/// <summary>
/// 为论坛分区名称提供自动补全建议。
/// 这些建议是固定的，从配置文件中加载。
/// </summary>
public class ForumCategoriesAutoCompletion : AutocompleteHandler
{
    private readonly ConfigLoader _configLoader;
    private readonly List<string> _forumCategories;
    private readonly ILogger<AppCommands> _logger;

    // 通过依赖注入获取配置加载器
    public ForumCategoriesAutoCompletion(ILogger<AppCommands> logger, ConfigLoader configLoader)
    {
        _logger = logger;
        _configLoader = configLoader;
        // 在构造函数中预先加载好建议列表
        _forumCategories = _configLoader.ScanCategories.ToList();
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
            _logger.LogInformation("ForumCategoriesAutoCompletion {ex}",ex);
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
    private readonly ILogger<AppCommands> _logger;

    // 通过依赖注入获取的数据库服务
    public FavoriteClassAutoCompletion(ILogger<AppCommands> logger, DatabaseService databaseService)
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
                    new AutocompleteResult("默认分类", "默认分类")
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
            _logger.LogInformation("FavoriteClassAutoCompletion {ex}",ex);
            return AutocompletionResult.FromError(ex);
        }
    }
}

/// <summary>
/// 为所有论坛分区内的标签提供自动补全建议。
/// 它会查询数据库中所有不重复的标签名。
/// </summary>
public class AllTagsAutoCompletion : AutocompleteHandler
{
    private readonly DatabaseService _databaseService;
    private readonly ILogger<AppCommands> _logger;

    public AllTagsAutoCompletion(ILogger<AppCommands> logger, DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    /// <summary>
    /// 动态地为用户生成所有不重复的标签建议。
    /// </summary>
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            // 获取用户当前输入的值，并转为小写以便不区分大小写地匹配
            string? userInput = (autocompleteInteraction.Data.Current.Value as string)?.ToLower();

            // 调用数据库方法
            var allUniqueTags = await _databaseService.GetAllUniqueTagNamesAsync();

            // 如果数据库里还没有标签，就返回一个空列表
            if (allUniqueTags == null || !allUniqueTags.Any())
            {
                return AutocompletionResult.FromSuccess();
            }

            // 根据用户的输入，从所有标签中筛选出匹配的
            var filteredTags = string.IsNullOrWhiteSpace(userInput)
                ? allUniqueTags // 如果用户还没输入，就显示所有标签
                : allUniqueTags.Where(t => t.ToLower().Contains(userInput));

            // 将筛选后的结果构造成 Discord 需要的格式
            var results = filteredTags
                .Select(tag => new AutocompleteResult(tag, tag))
                .Take(25) // Discord 最多只显示25个选项
                .ToList();

            return AutocompletionResult.FromSuccess(results);
        }
        catch (Exception ex)
        {
            // 如果发生错误，记录日志并返回一个安全的错误结果
            _logger.LogInformation("AllTagsAutoCompletion {ex}",ex);
            return AutocompletionResult.FromError(ex);
        }
    }
}