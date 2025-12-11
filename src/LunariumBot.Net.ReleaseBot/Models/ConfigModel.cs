namespace LunariumBot.Net.ReleaseBot.Models;

public record ReleaseBotBotConfig
{
    public required string DatabaseConnection { get; init; }
    public AuthConfig AuthConfig { get; init; } = new();
    public DeployConfig DeployConfig { get; init; } = new();
}

public record AuthConfig
{
    public HashSet<ulong> AdminRoleIds { get; init; } = new();
    public ulong LogChannel { get; init; }
    public ulong LogThread { get; init; }
}

public record DeployConfig
{
    public HashSet<ulong> DeployGuildIds { get; init; } = new();
    public Dictionary<ulong, CacheLocation> CacheLocations { get; init; } = new();
    public ReleaseConfig ReleaseConfig { get; init; } = new();
    public Dictionary<ulong, GuildConfig> GuildConfigs { get; init; } = new();
}

public record ReleaseConfig
{
    public int DailyLimit { get; set; }
    public int Lv1Threshold { get; set; }
    public int Lv1Interval { get; set; }
    public int Lv2Threshold { get; set; }
    public int Lv2Interval { get; set; }
}

public record CacheLocation
{
    public ulong Guild{ get; set; }
    public Dictionary<string, ulong> Channels { get; init; } = new();
}

public record GuildConfig
{
    public Dictionary<string, ulong> ValidChannels { get; init; } = new();
}
