namespace LunariumBot.Net.Archive.Model;

public sealed class ArchiveConfig
{
    public AuthConfig AuthConfig { get; set; } = new();
    public Dictionary<ulong, int> DeployGuildConfig { get; set; } = new();
    public int IntervalMin { get; set; } = 10;
}

public sealed class AuthConfig
{
    public List<ulong> AdminRoleIds { get; set; } = new();
    public ulong LogChannel { get; set; }
}

public sealed class ProxyConfig
{
    public bool Enable { get; set; } = false;
    public string ProxyUrl { get; set; } = "";
}