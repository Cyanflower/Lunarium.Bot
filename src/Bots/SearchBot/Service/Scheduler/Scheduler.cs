// "INSERT INTO posts_fts(posts_fts) VALUES('optimize')"
// 每天凌晨4点, 执行一次fts优化

using Quartz;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SearchBot.ThreadFetcher;

namespace SearchBot.Service.Scheduler;

[DisallowConcurrentExecution]
public class SchedulerService: IJob
{
    private readonly ILogger<SchedulerService> _logger;

    public SchedulerService(ILogger<SchedulerService> logger)
    {
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {

        return Task.CompletedTask;
    }
}