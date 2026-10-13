using Lunarium.Bot.Extensions;

namespace Lunarium.Bot.ReleaseBot.Models;

public class DownloadLogDb
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long ThreadId { get; set; }
    public long ReleaseMessageId { get; set; }
    public DateTimeOffset DownloadAt { get; set; }

    public static DownloadLogDb FromDomain(DownloadLog downloadLog) => new()
    {
        Id = downloadLog.Id,
        UserId = downloadLog.UserId.ToDbId(),
        ThreadId = downloadLog.ThreadId.ToDbId(),
        ReleaseMessageId = downloadLog.ReleaseMessageId.ToDbId(),
        DownloadAt = downloadLog.DownloadAt,
    };

    public DownloadLog ToDomain() => new()
    {
        Id = Id,
        UserId = UserId.ToDiscordId(),
        ThreadId = ThreadId.ToDiscordId(),
        ReleaseMessageId = ReleaseMessageId.ToDiscordId(),
        DownloadAt = DownloadAt,
    };
}

public class DownloadLog
{
    public long Id { get; set; }
    public ulong UserId { get; set; }
    public ulong ThreadId { get; set; }
    public ulong ReleaseMessageId { get; set; }
    public DateTimeOffset DownloadAt { get; set; }
}