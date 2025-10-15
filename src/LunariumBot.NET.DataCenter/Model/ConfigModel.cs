namespace LunariumBot.Net.DataCenter.Model;

public class DataCenterBotConfig
{
    public string DatabaseConnection { get; set; } = "";
    public AuthConfig AuthConfig { get; set; } = new();
    public GuildsConfig GuildsConfig { get; set; } = new();
    public BannerConfig BannerConfig { get; set; } = new();
    public CreditConfig CreditConfig { get; set; } = new();
    public Dictionary<string, int> ChannelOrder { get; set; } = new();
}

public class AuthConfig
{
    public List<ulong> AdminRoleIds { get; set; } = new();
    public ulong LogChannel { get; set; }
}

public class CreditConfig
{
    public int MessageCreditLv1 { get; set; }
    public int MessageCreditLv2 { get; set; }
    public int MessageCreditLv3 { get; set; }
    public int ThreadCreditLv1 { get; set; }
    public int ThreadCreditLv2 { get; set; }
    public int ThreadCreditLv3 { get; set; }
    public int UpdateCreditLv1 { get; set; }
    public int UpdateCreditLv2 { get; set; }
    public int UpdateCreditLv3 { get; set; }
}

public class GuildsConfig
{
    public List<ulong> DeployGuildIds { get; set; } = new();
    public List<ulong> DashboardChannels { get; set; } = new();
    public Dictionary<string, List<ulong>> ScanChannelIDs { get; set; } = new();
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