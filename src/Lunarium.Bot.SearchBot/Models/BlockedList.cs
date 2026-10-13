using Lunarium.Bot.Extensions;

namespace Lunarium.Bot.SearchBot.Models;

public class BlockedInfo
{
    public ulong BlockedAuthorId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

internal class BlockedInfoDb
{
    public long BlockedAuthorId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public static BlockedInfoDb FromDomain(BlockedInfo domain) => new()
    {
        BlockedAuthorId = domain.BlockedAuthorId.ToDbId(),
        AddedAt = domain.AddedAt
    };

    public BlockedInfo ToDomain() => new()
    {
        BlockedAuthorId = BlockedAuthorId.ToDiscordId(),
        AddedAt = AddedAt
    };
}