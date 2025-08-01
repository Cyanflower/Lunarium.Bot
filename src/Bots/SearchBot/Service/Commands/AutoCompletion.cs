using Discord.Interactions;
using Discord;
using SearchBot;
using SearchBot.Database;

namespace SearchBot.Utils;

public class ForumCategoriesAutoCompletion : AutocompleteHandler
{
    private readonly ConfigLoader _configLoader;

    private readonly List<string> _forumCategories = new();

    public ForumCategoriesAutoCompletion(ConfigLoader configLoader)
    {
        _configLoader = configLoader;
        _forumCategories = _configLoader.ScanCategories.ToList();
    }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction interaction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            var results = new List<AutocompleteResult>();
            foreach (var category in _forumCategories)
            {
                results.Add(new AutocompleteResult(category, category));
            }
            return Task.FromResult(AutocompletionResult.FromSuccess(results));
        }
        catch (Exception)
        {
            return Task.FromResult(AutocompletionResult.FromSuccess(new List<AutocompleteResult>()));
        }
    }
    
}

public class FavoriteClassAutoCompletion : AutocompleteHandler
{
    private readonly DatabaseService _databaseService;

    public FavoriteClassAutoCompletion(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction interaction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            var userId = context.User.Id;
            var favoriteType = await _databaseService.GetUserFavoriteTypeAsync(userId);
            if (favoriteType == null)
            {
                return AutocompletionResult.FromSuccess(new List<AutocompleteResult>());
            }
            var results = new List<AutocompleteResult>();
            foreach (var type in favoriteType)
            {
                results.Add(new AutocompleteResult(type, type));
            }
            return AutocompletionResult.FromSuccess(results);
        }
        catch (Exception)
        {
            return AutocompletionResult.FromSuccess(new List<AutocompleteResult>() {new AutocompleteResult("默认分类", "默认分类")});
        }
    }
    
}