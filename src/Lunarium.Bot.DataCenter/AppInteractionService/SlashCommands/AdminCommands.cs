
// using System.Diagnostics;
// using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using Lunarium.Logging;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

// 项目内命名空间
using Lunarium.Bot.Framework.RateLimiting;
using Lunarium.Bot.DataCenter.DataFetcher;
using Lunarium.Bot.DataCenter.Scheduler;
using Lunarium.Bot.DataCenter.Utils;
using Lunarium.Bot.DataCenter.Models;
using System.Text;
using Lunarium.Bot.Framework;
using Lunarium.Bot.DataCenter.UrlSignedInfoCache;

namespace Lunarium.Bot.DataCenter.AppInteractionService;

[Group("维护命令", "BOT管理员维护命令")]
public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly ThreadScanner _scanner;
    private readonly Listener _listener;
    private readonly DataAnalysis _dataAnalysis;
    private readonly AnalysisAsyncJob _analysisJob;
    private readonly MembersScanner _membersScanner;
    private readonly UrlSignedInfoCacheService _urlSignedInfoCacheService;
    private readonly UrlSignedInfoCacheCheckAsyncJob _urlSignedInfoCacheCheckAsyncJob;
    private readonly UrlSignedInfoCacheSyncAsyncJob _urlSignedInfoCacheSyncAsyncJob;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    public AdminCommands(
        ILogger logger,
        AuthUtils authUtils,
        ConfigLoader configLoader,
        ThreadScanner scanner,
        Listener listener,
        DataAnalysis dataAnalysis,
        AnalysisAsyncJob analysisJob,
        MembersScanner membersScanner,
        UrlSignedInfoCacheService urlSignedInfoCacheService,
        UrlSignedInfoCacheCheckAsyncJob urlSignedInfoCacheCheckAsyncJob,
        UrlSignedInfoCacheSyncAsyncJob urlSignedInfoCacheSyncAsyncJob,
        RateLimitedExecutor rateLimitedExecutor,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy
    )
    {
        _logger = logger.ForContext("AdminCommands");
        _authUtils = authUtils;
        _configLoader = configLoader;
        _scanner = scanner;
        _listener = listener;
        _dataAnalysis = dataAnalysis;
        _analysisJob = analysisJob;
        _membersScanner = membersScanner;
        _urlSignedInfoCacheService = urlSignedInfoCacheService; 
        _urlSignedInfoCacheCheckAsyncJob = urlSignedInfoCacheCheckAsyncJob;
        _urlSignedInfoCacheSyncAsyncJob = urlSignedInfoCacheSyncAsyncJob;
        _rateLimitedExecutor = rateLimitedExecutor;
        _httpRetryPolicy = httpRetryPolicy;
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

                if (reportBytes is null || reportBytes.Length == 0)
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
                _logger.Error(ex, "获取分析报告时发生严重错误！");
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
                _logger.Error(ex, "获取分析报告时发生严重错误！");
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("哎呀，出错了！", "执行分析的过程中遇到了一个意想不到的问题，请检查日志。"), ephemeral: false);
            }
        }
    }

    [SlashCommand("全扫描", "立即执行一次同步扫描（默认增量深挖）")]
    public async Task FullSyncScanAsync(
        [Summary("全量深挖", "true = 深挖全部帖子（约为增量的 6 倍耗时）；false = 只深挖脏集合、新增帖与曾软删的帖子")]
        bool full = false
    )
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("全扫描", Context)) return;
        bool success = false;
        // 按参数决定扫描模式: 全量深挖成功后会自动清空脏集合
        ScanMode mode = full ? ScanMode.Full : ScanMode.Incremental;
        string modeLabel = full ? "全量深挖" : "增量";
        // === 命令逻辑 ===
            var reporter = new ScanProgressReporter(Context, _logger, _rateLimitedExecutor, _httpRetryPolicy);
        try
        {
            
            await reporter.ReportStartAsync($"🚀 开始执行同步扫描任务（{modeLabel}）...", Color.Blue);

            // _listener.RemoveEventHandlers();

            // await reporter.ReportProgressAsync("✅ 暂停事件处理: 监听器事件处理方法已移除。");

            // 回复 Slash Command 避免超时 (Followup已经在reporter初始化中隐式处理，这里主要作为确认)
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("✅ 任务已调度", $"扫描模式: **{modeLabel}**\n正在后台执行同步操作，请查看进度卡片。"));

            // 执行扫描
            var result = await _scanner.SyncScanAsync(mode, reporter);

            success = true;
        }
        catch (Exception ex)
        {
            
            reporter.AppendLine($"同步扫描捕获异常: {ex.Message.Slice(1500)}");
            _logger.Error(ex, "扫描失败, 原因:");
            success = false;
        }
        finally
        {
            // _listener.AddEventHandlers();

            // await reporter.ReportProgressAsync("✅ 恢复事件处理: 监听器事件处理方法已添加。");

            if (success)
            {
                await reporter.ReportCompleteAsync(title: $"✅ 同步扫描任务成功（{modeLabel}）", color: Color.Green);
            }
            else
            {
                await reporter.ReportCompleteAsync(title: $"❌ 同步扫描任务失败（{modeLabel}）", color: Color.Red);
            }
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
            _logger.Error(ex, "重载配置命令失败, 原因:");
        }
    }


    [SlashCommand("立即检查url缓存更新", "立即执行一次缓存更新检查任务")]
    public async Task UrlCacheCheckAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("立即检查url缓存更新", Context)) return;
        // === 命令逻辑 ===
        try
        {
            // 执行扫描
            await _urlSignedInfoCacheCheckAsyncJob.ExecuteAsync();

            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("操作完成", "url缓存更新检查成功"), ephemeral: false);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("url缓存更新检查失败", ex.ToString()));
            _logger.Error(ex, "url缓存更新检查失败, 原因:");
        }
    }

    [SlashCommand("立即同步url缓存", "立即从数据库全量重建 URL 签名桶计划")]
    public async Task UrlCacheSyncAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("立即同步url缓存", Context)) return;
        // === 命令逻辑 ===
        try
        {
            await _urlSignedInfoCacheSyncAsyncJob.ExecuteAsync();

            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("操作完成", "url缓存全量同步成功"), ephemeral: false);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("url缓存全量同步失败", ex.ToString()));
            _logger.Error(ex, "url缓存全量同步失败, 原因:");
        }
    }

    [SlashCommand("立即同步用户与身份组信息", "立即执行一次同步用户信息和身份组信息")]
    public async Task SyncMembersAndRolesAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("立即同步用户与身份组信息", Context)) return;

        bool success = false;
        var reporter = new ScanProgressReporter(Context, _logger, _rateLimitedExecutor, _httpRetryPolicy);

        // === 命令逻辑 ===
        try
        {
            // 回复 Slash Command 避免超时 (Followup已经在reporter初始化中隐式处理，这里主要作为确认)
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("✅ 任务已调度", "正在后台执行同步操作，请查看进度卡片。"));

            // 执行扫描并获取统计结果
            var resultStats = await _membersScanner.SyncScanAsync(reporter);

            // 将统计结果添加到 reporter 的 buffer 中
            reporter.AppendLine(resultStats);
            success = true;
        }
        catch (Exception ex)
        {
            reporter.AppendLine($"❌ 同步失败: {ex.Message.Slice(500)}");
            _logger.Error(ex, "同步用户与身份组信息失败, 原因:");
            success = false;
        }
        finally
        {
            if (success)
            {
                await reporter.ReportCompleteAsync(title: "✅ 成员与身份组同步完成", color: Color.Green);
            }
            else
            {
                await reporter.ReportCompleteAsync(title: "❌ 成员与身份组同步失败", color: Color.Red);
            }
        }
    }
    
    [SlashCommand("导出url缓存", "导出当前内存中的所有URL签名缓存(JSONL)")]
    public async Task DumpUrlSignedInfoCacheAsync()
    {
        await DeferAsync(ephemeral: false);
        if (!await _authUtils.IsAdmin("导出url缓存", Context)) return;

        try
        {
            // 1. 获取快照
            var items = _urlSignedInfoCacheService.GetAllItemsSnapshot();
            
            if (items.Count == 0)
            {
                await FollowupAsync(embed: EmbedUtils.SuccessEmbed("缓存为空", "当前内存中没有任何 URL 签名信息。"));
                return;
            }

            // 2. 生成 JSONL
            // 使用 MemoryStream 避免大字符串占用过多 LOH 内存
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)); // 无BOM的UTF8

            var utc8 = TimeSpan.FromHours(8);
            
            foreach (HourlyBucketSerializedObject item in items)
            {
                // 写入一行 JSON
                await writer.WriteLineAsync(JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = false }));
            }
            
            await writer.FlushAsync();
            stream.Position = 0;

            // 3. 上传文件
            string fileName = $"UrlCache_Dump_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl";
            await FollowupWithFileAsync(
                stream, 
                fileName, 
                text: $"✅ 导出成功，共 {items.Count} 条数据。"
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "导出URL缓存失败");
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("导出失败", ex.Message));
        }
    }
}