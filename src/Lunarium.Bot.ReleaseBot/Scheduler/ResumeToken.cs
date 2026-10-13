using Lunarium.Bot.Scheduler;
using Lunarium.Bot.Scheduler.Interfaces;
using Lunarium.Bot.ReleaseBot.Utils;
using Lunarium.Bot.Logger;
using Lunarium.Bot.ReleaseBot.TokenBucketServices;

namespace Lunarium.Bot.ReleaseBot.Scheduler;


public class ResumeToken : IJob
{
    public readonly ILogger _logger;

    public readonly TokenBucketService _tokenBucketService;
    public ResumeToken(ILogger logger, TokenBucketService tokenBucketService)
    {
        _logger = logger;
        _tokenBucketService = tokenBucketService;
    }

    public void Execute()
    {
        try
        {
            _logger.Info("🔄 重置全部令牌桶");
            _tokenBucketService.ResumeAllToken();
            _logger.Info("✅ 重置全部令牌桶完成");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ ResumeToken Execute() Exception");
        }
    }
}