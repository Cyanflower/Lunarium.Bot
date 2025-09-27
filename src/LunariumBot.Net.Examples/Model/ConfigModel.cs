namespace LunariumBot.Net.Examples.Model;

public class ExamplesConfig
{
    public AuthConfig AuthConfig { get; set; } = new();
    public Dictionary<ulong, int> DeployGuildConfig { get; set; } = new();
}

public class AuthConfig
{
    public List<ulong> AdminRoleIds { get; set; } = new();
    public ulong LogChannel { get; set; }
}
