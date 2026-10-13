using Lunarium.Bot.Scheduler;
using Lunarium.Bot.Scheduler.Interfaces;
using Lunarium.Bot.Logger;
using Lunarium.Bot.VoteBot.VoteServices;

namespace Lunarium.Bot.VoteBot.Scheduler;


public class SaveData : IJob
{
    public readonly ILogger _logger;

    public readonly VoteService _voteService;
    public SaveData(ILogger logger, VoteService voteService)
    {
        _logger = logger;
        _voteService = voteService;
    }

    public void Execute()
    {
        try
        {
            _logger.Info("🔄 保存数据中");
            _voteService.SaveSubmits();
            _voteService.SaveVote();
            _logger.Info("✅ 保存数据完成");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SaveData Execute() Exception");
        }
    }
}