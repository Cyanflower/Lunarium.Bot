namespace LunariumBot.Net.VoteBot.Models;

public record VoteBotConfig
{
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
    public ulong AuditChannel { get; init; }
    public ulong PublicChannel { get; init; }
}
