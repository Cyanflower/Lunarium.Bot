namespace Lunarium.Bot.Archive.Model;

public sealed class ArchiveConfig
{
    public AuthConfig AuthConfig { get; set; } = new();
    public Dictionary<ulong, int> DeployGuildConfig { get; set; } = new();
    public int IntervalMin { get; set; } = 10;
}

public sealed class AuthConfig
{
    public List<ulong> AdminRoleIds { get; set; } = new();

    /// <summary>HM 报告相关频道(暂未启用)。</summary>
    public ulong LogChannel { get; set; }

    /// <summary>状态/通知频道。</summary>
    public ulong StatusChannel { get; set; }

    /// <summary>维护者用户ID(用于 @ 提及)。</summary>
    public ulong MaintainerId { get; set; }
}

public sealed class ProxyConfig
{
    public bool Enable { get; set; } = false;
    public string ProxyUrl { get; set; } = "";
}