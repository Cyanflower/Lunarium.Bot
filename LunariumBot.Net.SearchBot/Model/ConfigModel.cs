namespace LunariumBot.Net.SearchBot.Model;

public class SearchBotConfig
{
    public string DatabaseConnection { get; set; } = "";
    public AuthConfig AuthConfig { get; set; } = new();
    public GuildsConfig GuildsConfig { get; set; } = new();
    public BannerConfig BannerConfig { get; set; } = new();
    public Dictionary<string, int> ChannelOrder { get; set; } = new();
    public CreatorAutomatic CreatorAutomatic { get; set; } = new();
}

public class AuthConfig
{
    public List<ulong> AdminRoleIds { get; set; } = new();
    public ulong LogChannel { get; set; }
}

public class GuildsConfig
{
    public List<ulong> DeployGuildIds { get; set; } = new();
    public List<ulong> DashboardChannels { get; set; } = new();
    public Dictionary<string, List<ulong>> ScanChannelIDS { get; set; } = new();
}

public class BannerConfig
{
    public BannerChannelConfig New { get; set; } = new();
    public BannerChannelConfig Popular { get; set; } = new();
    public BannerChannelConfig Feature { get; set; } = new();
    public BannerChannelConfig Toplist { get; set; } = new();
}

public class BannerChannelConfig
{
    public List<ulong> ChannelId { get; set; } = new();
}

public class CreatorAutomatic
{
    public Dictionary<ulong, List<ulong>> CreatorRoleIDs { get; set; } = new();
    public List<ulong> ListenerChannel { get; set; } = new();
    public int ReactionThreshold { get; set; }
}


public class ProxyConfig
{
    public bool Enable { get; set; } = false;
    public string ProxyUrl { get; set; } = "";
}