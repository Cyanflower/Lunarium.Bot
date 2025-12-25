using System.Text.Json;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.ReleaseBot.Utils;

namespace LunariumBot.Net.ReleaseBot.Models;

public class WorkData
{
    public ulong ReleaseMessageId { get; set; }
    public ulong ThreadId { get; set; }
    public ulong AuthorId { get; set; }
    public DateTimeOffset ReleaseAt { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public string MessageJumpUrl { get; set; } = "";

    public ReleaseModel ReleaseModel { get; set; } = ReleaseModel.NoLimit;
    public string ReleasePassword { get; set; } = "";
    public bool NeedPassword { get; set; } = false;
    public bool IsConsumeToken { get; set; } = false;

    public string CacheMessageUrl { get; set; } = "";
    public ulong CacheMessageId { get; set; }
    public ulong CacheThreadId { get; set; }
    public ulong CacheChannelId { get; set; }
    public ulong CacheGuildId { get; set; }
    public List<string> CacheAttachmentUrls { get; set; } = new();

    public string? Alerts { get; set; }
    public bool EnableAlerts { get; set; } = false;

    public bool IsUpdate { get; set; } = false;
    public bool IsDeleted { get; set; }
    public DateTimeOffset DeleteAt { get; set; }
}

public class WorkDataDb
{
    public long ReleaseMessageId { get; set; }
    public long ThreadId { get; set; }
    public long AuthorId { get; set; }
    public DateTimeOffset ReleaseAt { get; set; }
    public long GuildId { get; set; }
    public long ChannelId { get; set; }
    public string MessageJumpUrl { get; set; } = "";

    public ReleaseModel ReleaseModel { get; set; } = ReleaseModel.NoLimit;
    public string ReleasePassword { get; set; } = "";
    public bool NeedPassword { get; set; } = false;
    public bool IsConsumeToken { get; set; } = false;
    

    public string CacheMessageUrl { get; set; } = "";
    public long CacheMessageId { get; set; }
    public long CacheThreadId { get; set; }
    public long CacheChannelId { get; set; }
    public long CacheGuildId { get; set; }
    public string CacheAttachmentUrls { get; set; } = "";

    public string? Alerts { get; set; }
    public bool EnableAlerts { get; set; } = false;

    public bool IsUpdate { get; set; } = false;
    public bool IsDeleted { get; set; }
    public DateTimeOffset DeleteAt { get; set; }

    public static WorkDataDb FromDomain(WorkData workData) => new()
    {
        ThreadId = workData.ThreadId.ToDbId(),
        AuthorId = workData.AuthorId.ToDbId(),
        ReleaseAt = workData.ReleaseAt,
        GuildId = workData.GuildId.ToDbId(),
        ChannelId = workData.ChannelId.ToDbId(),
        ReleaseMessageId = workData.ReleaseMessageId.ToDbId(),
        MessageJumpUrl = workData.MessageJumpUrl,
        CacheMessageUrl = workData.CacheMessageUrl,
        CacheMessageId = workData.CacheMessageId.ToDbId(),
        CacheThreadId = workData.CacheThreadId.ToDbId(),
        CacheChannelId = workData.CacheChannelId.ToDbId(),
        CacheGuildId = workData.CacheGuildId.ToDbId(),
        CacheAttachmentUrls = workData.CacheAttachmentUrls.Count > 0 ? JsonSerializer.Serialize(workData.CacheAttachmentUrls, JsonSerializerConfig.Options) : "",
        Alerts = workData.Alerts,
        EnableAlerts = workData.EnableAlerts,
        ReleaseModel = workData.ReleaseModel,
        ReleasePassword = workData.ReleasePassword,
        NeedPassword = workData.NeedPassword,
        IsConsumeToken = workData.IsConsumeToken,
        IsUpdate = workData.IsUpdate,
        IsDeleted = workData.IsDeleted,
        DeleteAt = workData.DeleteAt
    };

    public WorkData ToDomain() => new()
    {
        ThreadId = ThreadId.ToDiscordId(),
        AuthorId = AuthorId.ToDiscordId(),
        ReleaseAt = ReleaseAt,
        GuildId = GuildId.ToDiscordId(),
        ChannelId = ChannelId.ToDiscordId(),
        ReleaseMessageId = ReleaseMessageId.ToDiscordId(),
        MessageJumpUrl = MessageJumpUrl,
        CacheMessageUrl = CacheMessageUrl,
        CacheMessageId = CacheMessageId.ToDiscordId(),
        CacheThreadId = CacheThreadId.ToDiscordId(),
        CacheChannelId = CacheChannelId.ToDiscordId(),
        CacheGuildId = CacheGuildId.ToDiscordId(),
        CacheAttachmentUrls = (CacheAttachmentUrls.Length > 0
            ? JsonSerializer.Deserialize<List<string>>(CacheAttachmentUrls)
            : null) ?? new(),
        Alerts = Alerts,
        EnableAlerts = EnableAlerts,
        ReleaseModel = ReleaseModel,
        ReleasePassword = ReleasePassword,
        NeedPassword = NeedPassword,
        IsConsumeToken = IsConsumeToken,
        IsUpdate = IsUpdate,
        IsDeleted = IsDeleted,
        DeleteAt = DeleteAt
    };
}