using Microsoft.Extensions.Logging;

namespace SearchBot.Utils;

public class Authentication
{
    private readonly ILogger<Authentication> _logger;
    private readonly ConfigLoader _configLoader;

    public Authentication(ILogger<Authentication> logger, ConfigLoader configLoader)
    {
        _logger = logger;
        _configLoader = configLoader;
    }

    public bool IsAdmin(IEnumerable<ulong> userRoleIds)
    {
        return userRoleIds.Any(roleId => _configLoader.AdminRoleIds.Contains(roleId));
    }

    public bool InGuild(ulong guildId)
    {
        return _configLoader.DeployGuildIds.Contains(guildId);
    }
}