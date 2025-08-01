using Microsoft.Extensions.Logging;
using SearchBot.Database;
using System.Text.Json;



namespace SearchBot.FavoriteManager;



public class FavoriteManager
{
    private readonly ILogger<FavoriteManager> _logger;

    private readonly DatabaseService _databaseService;

    public FavoriteManager(ILogger<FavoriteManager> logger, DatabaseService databaseService)
    {
        _logger = logger;
        _databaseService = databaseService;
    }

    /// <summary>
    /// 用户收藏夹处理逻辑
    /// </summary>
    /// <param name="user"></param>
    /// <returns></returns>
    public async Task ProcessUserFavorite(ulong userId, string? favorite = null)
    {
        var result = await _databaseService.GetUserFavoriteTypeAsync(userId);
        if (favorite != null)
        
    }

}

