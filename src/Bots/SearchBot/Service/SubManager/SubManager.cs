using Microsoft.Extensions.Logging;

namespace SearchBot.SubManager;

public class SubManager
{
    private readonly ILogger<SubManager> _logger;

    public SubManager(ILogger<SubManager> logger)
    {
        _logger = logger;
    }


}