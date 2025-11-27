using Discord;
using Discord.WebSocket;

using LunariumBot.Net.DataCenter.DataFetcher;
using LunariumBot.Net.DataCenter.Utils;

using LunariumBot.Net.Logger;
using LunariumBot.Net.Scheduler;
using LunariumBot.Net.Scheduler.Interfaces;

namespace LunariumBot.Net.DataCenter.Scheduler;
public class SyncMembersAndRolesJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly MembersScanner _scanner;

    public SyncMembersAndRolesJob(ILogger logger, MembersScanner scanner)
    {
        _logger = logger.ForContext("FullSyncJob");
        _scanner = scanner;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            _logger.Info("开始定时扫描成员与身份组");
            await _scanner.SyncScanAsync();
            _logger.Info("成员与身份组定时扫描完成");
        }
        catch (Exception ex)
        {
            _logger.Error("FullSyncJob 执行时发生未处理的异常:", ex);
        }
    }
}