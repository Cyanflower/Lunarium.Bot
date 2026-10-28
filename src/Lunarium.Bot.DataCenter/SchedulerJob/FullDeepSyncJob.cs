using Discord;
using Discord.WebSocket;

using Lunarium.Bot.DataCenter.DataFetcher;
using Lunarium.Bot.DataCenter.Utils;

using Lunarium.Logging;
using Lunarium.Scheduler.Interfaces;

namespace Lunarium.Bot.DataCenter.Scheduler;

/// <summary>
/// 月度全量深扫：对所有帖子执行一次完整深度扫描，用于兜底漏事件、
/// 归档后仅发生反应/编辑的漂移，以及增量扫描可能遗留的数据。
/// <para>由调度器在每月 25 号 04:00（Asia/Shanghai）触发。</para>
/// </summary>
public class FullDeepSyncAsyncJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly ThreadScanner _scanner;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;
    private IChannel? _cacheLogChannel;

    public FullDeepSyncAsyncJob(ILogger logger, ConfigLoader configLoader, ThreadScanner scanner, DiscordSocketClient client)
    {
        _logger = logger.ForContext("FullDeepSyncJob");
        _configLoader = configLoader;
        _scanner = scanner;
        _client = client;
    }

    public async Task ExecuteAsync()
    {
        Embed embed;
        try
        {
            _logger.Info("月度全量扫描开始");
            var scanResult = await _scanner.SyncScanAsync(ScanMode.Full);
            _logger.Info("月度全量扫描完成");

            embed = EmbedUtils.SuccessEmbed(scanResult, "月度全量扫描更新成功");

            if (_cacheLogChannel is not IMessageChannel || _cacheLogChannel.Id != _configLoader.Config.AuthConfig.LogThread)
            {
                _cacheLogChannel = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogThread);
            }

            if (_cacheLogChannel is IMessageChannel logChannel)
            {
                await logChannel.SendMessageAsync(embed: embed);
            }
            else
            {
                _logger.Error("配置的日志频道 (ID: {LogChannel}) 不是一个有效的文本或线程频道，无法发送日志！",
                    _configLoader.Config.AuthConfig.LogChannel);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FullDeepSyncJob 执行时发生未处理的异常:");
        }
    }
}
