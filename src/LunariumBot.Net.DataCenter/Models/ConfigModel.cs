namespace LunariumBot.Net.DataCenter.Models;

public record DataCenterBotConfig
{
    public required string DatabaseConnection { get; init; }
    public AuthConfig AuthConfig { get; init; } = new();
    public DeployConfig DeployConfig { get; init; } = new();
    public CreditConfig CreditConfig { get; init; } = new();
    public Dictionary<string, int> ChannelOrder { get; init; } = new();
}

public record AuthConfig
{
    public HashSet<ulong> AdminRoleIds { get; init; } = new();
    public ulong LogChannel { get; init; }
    public ulong LogThread { get; init; }
}

public record CreditConfig
{
    /// <summary>
    /// 发送消息所获得的积分设置
    /// </summary>
    public CreditLevelSettings ForMessage { get; init; } = new();

    /// <summary>
    /// 创建主题（帖子）所获得的积分设置
    /// </summary>
    public CreditLevelSettings ForThread { get; init; } = new();

    /// <summary>
    /// 更新内容所获得的积分设置
    /// </summary>
    public CreditLevelSettings ForUpdate { get; init; } = new();

    /// <summary>
    /// 所在频道积分因素
    /// </summary>
    public Dictionary<ulong, int> ChannelFactor { get; init; } = new();
}

public record CreditLevelSettings
{
    public int Level1 { get; init; }
    public int Level2 { get; init; }
    public int Level3 { get; init; }
}

public record DeployConfig
{
    public HashSet<ulong> DeployGuildIds { get; init; } = new();
    public Dictionary<ulong, GuildAnalysisConfig> GuildAnalysisConfigs { get; init; } = new();
    public HashSet<ulong> ExcludeChannelIds { get; init; } = new();
    public HashSet<ulong> ExcludeCategoryIds { get; init; } = new();
    public HashSet<ulong> ExcludeGuildIds { get; init; } = new();
    public Dictionary<ulong, ScanConfig> ScanConfigs { get; init; } = new();
}

public record ScanConfig
{
    public Dictionary<string, ulong> ValidChannels { get; init; } = new();
}

/// <summary>
/// 单个服务器的数据分析配置
/// </summary>
public record GuildAnalysisConfig
{
    /// <summary>
    /// 该服务器的 Dashboard 频道ID（用于接收数据分析报告）
    /// </summary>
    public ulong DashboardChannelId { get; init; }

    /// <summary>
    /// 该服务器的频道分类映射
    /// Key: 分类名称（例如："男性向"、"女性向"）
    /// Value: 属于该分类的频道ID列表
    /// </summary>
    public Dictionary<string, ulong> CategoryChannels { get; init; } = new();
}

public enum CreditLevel
{
    Level1 = 1,
    Level2 = 2,
    Level3 = 3
}