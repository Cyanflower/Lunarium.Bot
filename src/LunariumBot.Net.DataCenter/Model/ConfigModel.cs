namespace LunariumBot.Net.DataCenter.Model;

public class DataCenterBotConfig
{
    public string DatabaseConnection { get; set; } = "";
    public AuthConfig AuthConfig { get; set; } = new();
    public DeployConfig DeployConfig { get; set; } = new();
    public BannerConfig BannerConfig { get; set; } = new();
    public CreditConfig CreditConfig { get; set; } = new();
    public Dictionary<string, int> ChannelOrder { get; set; } = new();
}

public class AuthConfig
{
    public HashSet<ulong> AdminRoleIds { get; set; } = new();
    public ulong LogChannel { get; set; }
}

public class CreditConfig
{
    /// <summary>
    /// 发送消息所获得的积分设置
    /// </summary>
    public CreditLevelSettings ForMessage { get; set; } = new();

    /// <summary>
    /// 创建主题（帖子）所获得的积分设置
    /// </summary>
    public CreditLevelSettings ForThread { get; set; } = new();

    /// <summary>
    /// 更新内容所获得的积分设置
    /// </summary>
    public CreditLevelSettings ForUpdate { get; set; } = new();

    public Dictionary<ulong, int> ChannelFactor { get; set; } = new();
}

public class CreditLevelSettings
{
    public int Level1 { get; set; }
    public int Level2 { get; set; }
    public int Level3 { get; set; }
}

public class DeployConfig
{
    /// <summary>
    /// 部署的服务器ID列表（用于数据采集和监听）
    /// </summary>
    public HashSet<ulong> DeployGuildIds { get; set; } = new();

    /// <summary>
    /// 按服务器组织的数据分析配置
    /// Key: 服务器ID (GuildId)
    /// Value: 该服务器的分析配置
    /// </summary>
    public Dictionary<ulong, GuildAnalysisConfig> GuildAnalysisConfigs { get; set; } = new();

    public HashSet<ulong> ExcludeChannelIds { get; set; } = new();
    public HashSet<ulong> ExcludeCategoryIds { get; set; } = new();
    public HashSet<ulong> ExcludeGuildIds { get; set; } = new();
}

/// <summary>
/// 单个服务器的数据分析配置
/// </summary>
public class GuildAnalysisConfig
{
    /// <summary>
    /// 该服务器的 Dashboard 频道ID（用于接收数据分析报告）
    /// </summary>
    public ulong DashboardChannelId { get; set; }

    /// <summary>
    /// 该服务器的频道分类映射
    /// Key: 分类名称（例如："男性向"、"女性向"）
    /// Value: 属于该分类的频道ID列表
    /// </summary>
    public Dictionary<string, List<ulong>> CategoryChannels { get; set; } = new();
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