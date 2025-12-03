
// using System.Diagnostics;
// using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.DataFetcher;
using LunariumBot.Net.DataCenter.Scheduler;
using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.DataCenter.Models;
using System.Text;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.DataCenter.UrlSignedInfoCache;

namespace LunariumBot.Net.DataCenter.AppInteractionService;

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
        UrlSignedInfoCacheSyncAsyncJob urlSignedInfoCacheSyncAsyncJob
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
        bool success = false;
        // === 命令逻辑 ===
            var reporter = new ScanProgressReporter(Context, _logger);
        try
        {
            
            await reporter.ReportStartAsync("🚀 开始执行同步扫描任务...", Color.Blue);

            _listener.RemoveEventHandlers();

            await reporter.ReportProgressAsync("✅ 暂停事件处理: 监听器事件处理方法已移除。");

            // 回复 Slash Command 避免超时 (Followup已经在reporter初始化中隐式处理，这里主要作为确认)
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("✅ 任务已调度", "正在后台执行同步操作，请查看进度卡片。"));

            // 执行扫描
            var result = await _scanner.SyncScanAsync(reporter);

            success = true;
        }
        catch (Exception ex)
        {
            
            reporter.AppendLine($"同步扫描捕获异常: {ex.Message.Slice(1500)}");
            _logger.Error("扫描失败, 原因:", ex);
            success = false;
        }
        finally
        {
            _listener.AddEventHandlers();

            await reporter.ReportProgressAsync("✅ 恢复事件处理: 监听器事件处理方法已添加。");

            if (success)
            {
                await reporter.ReportCompleteAsync(title: "✅ 同步扫描任务成功", color: Color.Green);
            }
            else
            {
                await reporter.ReportCompleteAsync(title: "❌ 同步扫描任务失败", color: Color.Red);
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
            _logger.Error("重载配置命令失败, 原因:", ex);
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
            _logger.Error("url缓存更新检查失败, 原因:", ex);
        }
    }

    [SlashCommand("立即同步用户与身份组信息", "立即执行一次同步用户信息和身份组信息")]
    public async Task SyncMembersAndRolesAsync()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("立即同步用户与身份组信息", Context)) return;

        bool success = false;
        var reporter = new ScanProgressReporter(Context, _logger);

        // === 命令逻辑 ===
        try
        {
            // 报告任务开始
            await reporter.ReportStartAsync("🚀 开始执行成员与身份组同步任务...", Color.Blue);

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
            _logger.Error("同步用户与身份组信息失败, 原因:", ex);
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
            
            foreach (var item in items)
            {
                var exportObj = new
                {
                    item.Url,
                    item.ChannelId,
                    item.MessageId,
                    item.ExpireTimestamp,
                    // 额外字段: UTC+8 ISO8601
                    ExpireDateIso = DateTimeOffset.FromUnixTimeSeconds(item.ExpireTimestamp)
                                        .ToOffset(utc8)
                                        .ToString("yyyy-MM-dd HH:mm:ss zzz")
                };

                // 写入一行 JSON
                await writer.WriteLineAsync(JsonSerializer.Serialize(exportObj, new JsonSerializerOptions { WriteIndented = false }));
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

    [SlashCommand("调试url缓存查询", "根据URL查询缓存状态")]
    public async Task InspectUrlSignedInfoAsync([Summary("url", "Discord附件URL")] string inputUrl)
    {
        await DeferAsync(ephemeral: false);
        if (!await _authUtils.IsAdmin("调试url缓存查询", Context)) return;

        var sb = new StringBuilder();
        var utc8 = TimeSpan.FromHours(8);

        try
        {
            sb.AppendLine("**🔎 URL 调试分析报告**");
            sb.AppendLine("----------------------------------------");

            // 1. 解析输入 URL
            Uri uri;
            try 
            {
                uri = new Uri(inputUrl);
            }
            catch (UriFormatException)
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("格式错误", "提供的字符串不是一个有效的 URL。"));
                return;
            }

            // 尝试提取 ChannelId 和 MessageId (标准 Discord 附件格式: /attachments/{channelId}/{messageId}/{filename})
            ulong channelId = 0;
            ulong messageId = 0;
            bool idsFound = false;

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // segments 通常是 ["attachments", "1417750224088338473", "1444732908681302138", "image.png"]
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i] == "attachments" && i + 2 < segments.Length)
                {
                    if (ulong.TryParse(segments[i+1], out channelId) && ulong.TryParse(segments[i+2], out messageId))
                    {
                        idsFound = true;
                        break;
                    }
                }
            }

            // 解析 ex 参数
            var query = HttpUtility.ParseQueryString(uri.Query);
            string exHex = query["ex"] ?? "null";
            long inputExpireTs = 0;
            bool exParsed = long.TryParse(exHex, System.Globalization.NumberStyles.HexNumber, null, out inputExpireTs);

            // === 报告：输入解析部分 ===
            sb.AppendLine("📥 **输入解析**");
            sb.AppendLine($"🔗 URL: `{inputUrl.Slice}...`"); // 截断防止过长
            
            if (idsFound)
            {
                sb.AppendLine($"🆔 ChannelId: `{channelId}`");
            }
            else
            {
                sb.AppendLine("⚠️ **警告**: 无法从 URL 路径中提取 ChannelId，仅通过 URL 字符串匹配可能不准确。");
            }

            sb.AppendLine($"🕰️ ex (Hex): `{exHex}`");
            if (exParsed)
            {
                var inputTime = DateTimeOffset.FromUnixTimeSeconds(inputExpireTs).ToOffset(utc8);
                sb.AppendLine($"📅 解析时间 (Unix): `{inputExpireTs}`");
                sb.AppendLine($"📅 解析时间 (ISO): `{inputTime:yyyy-MM-dd HH:mm:ss zzz}`");
                
                if (inputTime < DateTimeOffset.Now)
                    sb.AppendLine("❌ **状态**: URL 已过期");
                else
                    sb.AppendLine("✅ **状态**: URL 有效");
            }
            else
            {
                sb.AppendLine("❌ **错误**: 无法解析 `ex` 参数，URL 可能无效。");
            }
            sb.AppendLine("----------------------------------------");

            // 2. 查询缓存
            sb.AppendLine("💾 **缓存查询结果**");
            
            UrlSignedInfo? cachedItem = null;

            if (idsFound)
            {
                // 优先使用 ID 精确查询
                cachedItem = _urlSignedInfoCacheService.FindByIds(channelId);
            }
            
            // 如果 ID 没查到或者没解析出 ID，但缓存里可能存了该 URL (兜底，虽然 UrlSignedInfo 设计上依赖 ID)
            // 这里我们主要依赖 ID 查询，因为题目要求 "根据 ... channelId 查询缓存"
            
            if (cachedItem != null)
            {
                sb.AppendLine("✅ **命中缓存**: ✅ 存在");
                sb.AppendLine($"🔗 缓存 URL: `{cachedItem.Url}...`");
                
                // 解析缓存中的 ex (为了验证)
                var cachedUri = new Uri(cachedItem.Url);
                var cachedEx = HttpUtility.ParseQueryString(cachedUri.Query)["ex"];
                
                sb.AppendLine($"🕰️ 缓存 ex (Hex): `{cachedEx}`");
                sb.AppendLine($"📅 缓存 ExpireTimestamp: `{cachedItem.ExpireTimestamp}`");
                sb.AppendLine($"📅 缓存 ExpireISO: `{DateTimeOffset.FromUnixTimeSeconds(cachedItem.ExpireTimestamp).ToOffset(utc8):yyyy-MM-dd HH:mm:ss zzz}`");

                // 对比
                if (exParsed && cachedItem.ExpireTimestamp == inputExpireTs)
                {
                    sb.AppendLine("⚖️ **一致性检查**: ✅ 时间戳完全一致");
                }
                else if (exParsed)
                {
                    sb.AppendLine($"⚠️ **一致性检查**: ❌ 不一致 (差异 {cachedItem.ExpireTimestamp - inputExpireTs} 秒)");
                    sb.AppendLine("   (可能由于 URL 签名已刷新但缓存未更新，或反之)");
                }
            }
            else
            {
                sb.AppendLine("❌ **命中缓存**: ❌ 不存在");
                sb.AppendLine("   (请确认该频道的图片是否已被扫描入库，或是否已被过滤)");
            }

            // 发送结果
            var embed = new EmbedBuilder()
                .WithTitle("URL 缓存调试分析")
                .WithDescription(sb.ToString())
                .WithColor(cachedItem != null ? Color.Green : Color.Orange)
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "URL缓存调试命令执行失败");
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("执行出错", ex.Message));
        }
    }
}