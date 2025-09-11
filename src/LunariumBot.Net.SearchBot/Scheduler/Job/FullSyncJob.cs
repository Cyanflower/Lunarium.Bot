using Coravel.Invocable;

using Discord;
using Discord.WebSocket;

using LunariumBot.Net.SearchBot.ThreadFetcher;
using LunariumBot.Net.SearchBot.Utils;

using LunariumBot.Net.Logger;

namespace LunariumBot.Net.SearchBot.Scheduler;
public class FullSyncJob : IInvocable
{
    private readonly ILogger _logger;
    private readonly Scanner _scanner;
    private readonly DiscordSocketClient _client;
    private readonly ConfigLoader _configLoader;
    private IChannel? _cacheLogChannel;

    public FullSyncJob(ILogger logger, ConfigLoader configLoder, Scanner scanner, DiscordSocketClient client)
    {
        _logger = logger.ForContext("FullSyncJob");
        _configLoader = configLoder;
        _scanner = scanner;
        _client = client;
    }

    public async Task Invoke()
    {
        Embed embed;
        try
        {
            _logger.Info("定时扫描开始");
            // 执行同步扫描
            var scanResult = _scanner.FullSync();
            var description = $"""
                扫描结果:
                总帖子数量: {scanResult.ScanAllResult}
                ----------------
                并发处理帖子数据结果:
                > 成功: {scanResult.ThreadProcessResult.SuccessCount}
                > 失败: {scanResult.ThreadProcessResult.FailedCount}
                > 跳过: {scanResult.ThreadProcessResult.SkippedCount}
                > 总计: {scanResult.ThreadProcessResult.ThreadsCount}
                > 总耗时: {scanResult.ThreadProcessResult.TotalElapsed:F2}秒
                > 平均速度: {scanResult.ThreadProcessResult.OverallAvgSpeed:F2} 任务/秒 | {scanResult.ThreadProcessResult.OverallAvgSpeed * 2:F2}
                ----------------
                数据库写入结果:
                > 帖子主表写入行数: {scanResult.InsertPostResult.MainAffectedRows}
                > FTS表写入行数: {scanResult.InsertPostResult.FtsAffectedRows}
                > 帖子标签关系表写入行数: {scanResult.InsertPostResult.TagJunctionAffectedRows}
                数据库失效帖子数量: {scanResult.ScanObsoleteResult}
                数据库成功移除行数: {scanResult.ScanRemoveResult}
                """;

            _logger.Info("定时扫描完成");

            embed = EmbedUtils.SuccessEmbed(description, "定时扫描更新成功");

            // 判断是否有缓存或者已有缓存是否和配置相同(检查配置更新)
            if (_cacheLogChannel is not IMessageChannel || _cacheLogChannel.Id != _configLoader.Config.AuthConfig.LogChannel)
            {
                _cacheLogChannel = await _client.GetChannelAsync(_configLoader.Config.AuthConfig.LogChannel);
            }

            // 转换为通用可发消息频道
            if (_cacheLogChannel is IMessageChannel logChannel)
            {
                await logChannel.SendMessageAsync(embed: embed);
            }
            else
            {
                _logger.Error($"配置的日志频道 (ID: {_configLoader.Config.AuthConfig.LogChannel}) 不是一个有效的文本或线程频道，无法发送日志！");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("FullSyncJob 执行时发生未处理的异常:", ex);
        }
    }
}