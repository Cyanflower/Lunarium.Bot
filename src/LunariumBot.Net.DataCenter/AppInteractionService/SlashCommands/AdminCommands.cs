
// using System.Diagnostics;
// using System.Runtime.InteropServices;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.DataFetcher;
using LunariumBot.Net.DataCenter.Scheduler;
using LunariumBot.Net.DataCenter.Utils;

namespace LunariumBot.Net.DataCenter.AppInteractionService;

[Group("维护命令", "BOT管理员维护命令")]
public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly Scanner _scanner;
    private readonly DataAnalysis _dataAnalysis;
    private readonly AnalysisJob _analysisJob;
    public AdminCommands(
        ILogger logger,
        AuthUtils authUtils,
        ConfigLoader configLoader,
        Scanner scanner,
        DataAnalysis dataAnalysis,
        AnalysisJob analysisJob
    )
    {
        _logger = logger.ForContext("AdminCommands");
        _authUtils = authUtils;
        _configLoader = configLoader;
        _scanner = scanner;
        _dataAnalysis = dataAnalysis;
        _analysisJob = analysisJob;
    }
    [SlashCommand("ping", "回复Pong消息")]
    public async Task PingAsync()
    {
        await DeferAsync(ephemeral: true);
        if (await _authUtils.IsAdmin("ping", Context))
        {
            // === 命令逻辑 ===
            // 获取延迟
            var websocketLatency = Context.Client.Latency;
            // 回复
            var FollowupMessage = await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"Pong! 🏓\nWebSocket延迟: {websocketLatency}ms"), ephemeral: true);
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed($"{FollowupMessage.Id}"), ephemeral: true);
        }
    }

    [SlashCommand("获取分析报告", "立即执行一次分析并上传分析报告")]
    public async Task GetAnalysisReportAsync(
        [Summary("显示热门标签", "是否在报告中包含热门标签统计")]
        bool showTags = false,
        [Summary("显示焦点帖子", "是否在报告中包含热门帖子链接")]
        bool showFocusPosts = false
    )
    {
        await DeferAsync(ephemeral: false);
        if (await _authUtils.IsAdmin("获取分析报告", Context))
        {
            try
            {
                // 调用新的分析方法
                var reportBytes = await _dataAnalysis.GenerateAnalysisReportAsync(showTags, showFocusPosts);

                if (reportBytes == null || reportBytes.Length == 0)
                {
                    await ModifyOriginalResponseAsync(msg => msg.Embed = EmbedUtils.FailedEmbed("生成失败", "分析过程没有产生任何数据，无法生成报告。"));
                    return;
                }

                // 准备上传文件
                var reportTime = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8));
                string fileName = $"DataAnalysisReport_{reportTime:yyyyMMdd_HHmmss}.md";

                using (var stream = new MemoryStream(reportBytes))
                {
                    var attachment = new FileAttachment(stream, fileName);

                    await FollowupAsync(embed: EmbedUtils.SuccessEmbed("文件已上传", "报告生成完毕"), ephemeral: false);
                    await Context.Channel.SendFileAsync(attachment);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("获取分析报告时发生严重错误！", ex);
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("哎呀，出错了！", "生成报告的过程中遇到了一个意想不到的问题，请检查日志。"), ephemeral: false);
            }
        }
    }

    [SlashCommand("立即分析", "立即执行一次分析并更新资源区信息")]
    public async Task StartAnalysisNwoReportAsync()
    {
        await DeferAsync(ephemeral: false);
        if (await _authUtils.IsAdmin("立即分析", Context))
        {
            try
            {
                await _analysisJob.ExecuteAsync();
                await FollowupAsync(embed: EmbedUtils.SuccessEmbed("执行完毕", "立即分析"));
            }
            catch (Exception ex)
            {
                _logger.Error("获取分析报告时发生严重错误！", ex);
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("哎呀，出错了！", "执行分析的过程中遇到了一个意想不到的问题，请检查日志。"), ephemeral: false);
            }
        }
    }

    [SlashCommand("全扫描", "执行全扫描")]
    public async Task FullSyncScanAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("全扫描", Context)) return;
        // === 命令逻辑 ===
        try
        {
            // 执行扫描
            var scanResult = await _scanner.SyncScan();
            // 回复
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed(scanResult, "扫描更新成功"), ephemeral: false);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("扫描失败", ex.ToString()));
            _logger.Error("扫描失败, 原因:", ex);
        }
    }

    [SlashCommand("重载配置", "重新载入配置")]
    public async Task ReloadConfigAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("重载配置", Context)) return;
        // === 命令逻辑 ===
        try
        {
            // 执行扫描
            var reloadResult = _configLoader.ReloadConfig();
            if (reloadResult.result)
            {
                // 回复
                await FollowupAsync(embed: EmbedUtils.SuccessEmbed(reloadResult.reason, "配置更新成功"), ephemeral: false);
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.SuccessEmbed(reloadResult.reason, "配置更新失败"), ephemeral: false);
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("重载配置失败", ex.ToString()));
            _logger.Error("重载配置命令失败, 原因:", ex);
        }
    }
}