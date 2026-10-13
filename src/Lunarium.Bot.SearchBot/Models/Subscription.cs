using Lunarium.Bot.Extensions;

namespace Lunarium.Bot.SearchBot.Models;

public class SubscriptionData
{
    public ulong UserId { get; set; }
    public ulong AuthorId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

public class SubscriptionDataDb
{
    public long UserId { get; set; }
    public long AuthorId { get; set; }
    public DateTimeOffset AddedAt { get; set; }

    public static SubscriptionDataDb FromDomain(SubscriptionData subscriptionData) => new()
    {
        UserId = subscriptionData.UserId.ToDbId(),
        AuthorId = subscriptionData.AuthorId.ToDbId(),
        AddedAt = subscriptionData.AddedAt
    };

    public SubscriptionData ToDomain() => new()
    {
        UserId = UserId.ToDiscordId(),
        AuthorId = AuthorId.ToDiscordId(),
        AddedAt = AddedAt
    };
}

// public class FavoriteTypeInfoDb
// {
//     public long UserId { get; init; }
//     public long FavoriteId { get; init; }
//     public string TypeName { get; init; } = "";
//     public DateTimeOffset CreatedAt { get; init; }

//     public static FavoriteTypeInfoDb FromDomain(FavoriteTypeInfo favoriteTypeInfo) => new()
//     {
//         UserId = favoriteTypeInfo.UserId.ToDbId(),
//         FavoriteId = favoriteTypeInfo.FavoriteId,
//         TypeName = favoriteTypeInfo.TypeName,
//         CreatedAt = favoriteTypeInfo.CreatedAt
//     };

//     public FavoriteTypeInfo ToDomain() => new()
//     {
//         UserId = UserId.ToDiscordId(),
//         FavoriteId = FavoriteId,
//         TypeName = TypeName,
//         CreatedAt = CreatedAt
//     };
// }

