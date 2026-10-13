using Lunarium.Bot.Extensions;

namespace Lunarium.Bot.SearchBot.Models;

public class FavoriteDisplayItem
{
    public FavoriteInfo FavoriteInfo { get; init; }
    public PostDisplayModel PostDisplayModel { get; init; }

    public FavoriteDisplayItem(FavoriteInfo favoriteInfo, PostDisplayModel postDisplayModel)
    {
        FavoriteInfo = favoriteInfo;
        PostDisplayModel = postDisplayModel;
    }
}

public class FavoriteInteractionState
{
    // 基础信息
    public ulong UserId { get; init; }
    public ulong ThreadId { get; init; }
    public string ThreadName { get; init; } = "";
    public string? AuthorAvatarUrl { get; init; }

    // --- 交互状态 ---
    public List<FavoriteTypeInfo> AllFolders { get; set; } = new();

    // 当前选中的文件夹 ID
    public string SelectedFolderId { get; set; } = ""; 

    // 分页状态
    public int CurrentFolderPage { get; set; } = 0;
    public const int PageSize = 25;

    // 推送开关
    public bool IsPushEnabled { get; set; } = true;

    // 辅助计算
    public int TotalPages => AllFolders.Count == 0 ? 1 : (int)Math.Ceiling((double)AllFolders.Count / PageSize);
}

public class FavoriteTypeInfo
{
    public ulong UserId { get; init; }
    public long FavoriteId { get; init; }
    public int FavoriteCount { get; init; }
    public string TypeName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; }

    public void UpdateName(string newName)
    {
        TypeName = newName;
    }
}

public class FavoriteTypeInfoDb
{
    public long UserId { get; init; }
    public long FavoriteId { get; init; }
    public int FavoriteCount { get; init; }
    public string TypeName { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }

    public static FavoriteTypeInfoDb FromDomain(FavoriteTypeInfo favoriteTypeInfo) => new()
    {
        UserId = favoriteTypeInfo.UserId.ToDbId(),
        FavoriteId = favoriteTypeInfo.FavoriteId,
        FavoriteCount = favoriteTypeInfo.FavoriteCount,
        TypeName = favoriteTypeInfo.TypeName,
        CreatedAt = favoriteTypeInfo.CreatedAt
    };

    public FavoriteTypeInfo ToDomain() => new()
    {
        UserId = UserId.ToDiscordId(),
        FavoriteId = FavoriteId,
        FavoriteCount = FavoriteCount,
        TypeName = TypeName,
        CreatedAt = CreatedAt
    };
}

public class FavoriteInfo
{
    public ulong UserId { get; init; }
    public ulong ThreadId { get; init; }
    public long FavoriteAs { get; init;}
    public string FavoriteTypeName { get; init; } = "";
    public bool AllowPush { get; init; }
    public DateTimeOffset AddedAt { get; init; }
}

public class FavoriteInfoDb
{
    public long UserId { get; init;}
    public long ThreadId { get; init;}
    public long FavoriteAs { get; init;}
    public string FavoriteTypeName { get; init;} = "";
    public bool AllowPush { get; init;}
    public DateTimeOffset AddedAt { get; init; }

    public static FavoriteInfoDb FromDomain(FavoriteInfo favoriteInfo) => new()
    {
        UserId = favoriteInfo.UserId.ToDbId(),
        ThreadId = favoriteInfo.ThreadId.ToDbId(),
        FavoriteAs = favoriteInfo.FavoriteAs,
        FavoriteTypeName = favoriteInfo.FavoriteTypeName,
        AllowPush = favoriteInfo.AllowPush,
        AddedAt = favoriteInfo.AddedAt
    };

    public FavoriteInfo ToDomain() => new()
    {
        UserId = UserId.ToDiscordId(),
        ThreadId = ThreadId.ToDiscordId(),
        FavoriteAs = FavoriteAs,
        FavoriteTypeName = FavoriteTypeName,
        AllowPush = AllowPush,
        AddedAt = AddedAt
    };
}
