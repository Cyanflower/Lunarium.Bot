using Discord;
using Discord.WebSocket;

using Lunarium.Bot.DataCenter.DataFetcher;
using Lunarium.Bot.DataCenter.Utils;

using Lunarium.Logging;
using Lunarium.Scheduler;
using Lunarium.Scheduler.Interfaces;

namespace Lunarium.Bot.DataCenter.Scheduler;
public class FullSyncAsyncJob : IAsyncJob
{
    private readonly ILogger _logger;
    private readonly ThreadScanner _scanner;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;
    private IChannel? _cacheLogChannel;

    public FullSyncAsyncJob(ILogger logger, ConfigLoader configLoder, ThreadScanner scanner, DiscordSocketClient client)
    {
        _logger = logger.ForContext("FullSyncJob");
        _configLoader = configLoder;
        _scanner = scanner;
        _client = client;
    }

    public async Task ExecuteAsync()
    {
        Embed embed;
        try
        {
            _logger.Info("定时扫描开始");
            // 执行增量同步扫描
            var scanResult = await _scanner.SyncScanAsync(ScanMode.Incremental);

            _logger.Info("定时扫描完成");

            embed = EmbedUtils.SuccessEmbed(scanResult, "定时扫描更新成功");

            // 判断是否有缓存或者已有缓存是否和配置相同(检查配置更新)
            if (_cacheLogChannel is not IMessageChannel || _cacheLogChannel.Id != _configLoader.Config.AuthConfig.LogThread)
            {
                _cacheLogChannel = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogThread);
            }

            // 转换为通用可发消息频道
            if (_cacheLogChannel is IMessageChannel logChannel)
            {
                await logChannel.SendMessageAsync(embed: embed);
            }
            else
            {
                _logger.Error("配置的日志频道 (ID: {_configLoader.Config.AuthConfig.LogChannel}) 不是一个有效的文本或线程频道，无法发送日志！", _configLoader.Config.AuthConfig.LogChannel);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "FullSyncJob 执行时发生未处理的异常:");
        }
    }
}