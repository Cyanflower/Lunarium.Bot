
// using System.Diagnostics;
// using System.Runtime.InteropServices;

// NuGet 包依赖
using Discord;
using Discord.Interactions;
using Dapper;
using Microsoft.Data.Sqlite;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.ThreadFetcher;
using LunariumBot.Net.SearchBot.Scheduler;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

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
                await _analysisJob.Invoke();
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
            var scanResult = _scanner.FullSync();
            var description = $"""
                扫描结果:
                "总帖子数量: {scanResult.ScanAllResult}
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
            // 回复
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed(description, "扫描更新成功"), ephemeral: false);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("扫描失败", ex.ToString()));
            _logger.Error("扫描失败, 原因:", ex);
        }
    }

    [SlashCommand("创建管理面板", "在本频道内创建一个用于快捷搜索与查看收藏和订阅的面板")]
    public async Task CreateManagerEmbed()
    {
        await DeferAsync(ephemeral: false);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.IsAdmin("创建管理面板", Context)) return;

        try
        {
            var embed = DisplayEmbedBuilder.DirectSearchEmbed();

            var component = AppInteractionUitls.DirectSearchMenuComponent(_configLoader);

            await FollowupAsync(embed: embed, components: component, ephemeral: false);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("创建管理面板失败", ex.ToString()), ephemeral: false);
            _logger.Error("创建管理面板失败", ex);
        }
    }

//     [SlashCommand("force-cleanup", "强制释放所有可回收内存以观察真实占用。")]
//     public async Task ForceCleanup()
//     {
//         await DeferAsync();

//         var process = Process.GetCurrentProcess();
//         process.Refresh();
//         var memoryBefore = process.PrivateMemorySize64 / (1024.0 * 1024.0);

//         var embed = new EmbedBuilder()
//             .WithTitle("🧹 内存深度清理报告")
//             .WithColor(Color.Blue)
//             .WithDescription("正在执行多层内存清理操作，请稍候...")
//             .AddField("清理前", $"**{memoryBefore:F2} MB**", true);

//         await FollowupAsync(embed: embed.Build());

//         // --- 开始清理 ---

//         // 1. SQLite 瘦身
//         string sqliteResult = "跳过 (未使用)";
//         try
//         {
//             using var connection = new SqliteConnection("Data Source=SearchBotDB;Mode=Memory;Cache=Shared");
//             await connection.ExecuteAsync("PRAGMA shrink_memory;");
//             sqliteResult = "✅ 已执行";
//         }
//         catch (Exception ex)
//         {
//             sqliteResult = $"❌ 失败: {ex.Message}";
//         }

//         // 2. 强制 .NET GC
//         GC.Collect(2, GCCollectionMode.Forced, true, true);
//         GC.WaitForPendingFinalizers();

//         // 3. 强制 glibc 归还内存 (核心)
//         string mallocTrimResult;
//         try
//         {
//             int result = NativeMethods.malloc_trim(0);
//             mallocTrimResult = result == 1 ? "✅ 成功释放" : "ℹ️ 执行了，但无可释放";
//         }
//         catch (DllNotFoundException)
//         {
//             mallocTrimResult = "❌ 失败 (非 Linux/glibc 环境)";
//         }
//         catch (Exception ex)
//         {
//             mallocTrimResult = $"❌ 异常: {ex.Message}";
//         }

//         // --- 清理结束 ---

//         // 等待一下，让操作系统更新进程的内存统计
//         await Task.Delay(1000);

//         process.Refresh();
//         var memoryAfter = process.PrivateMemorySize64 / (1024.0 * 1024.0);

//         embed.Fields.Clear(); // 清空旧字段
//         embed.Color = Color.Green;
//         embed.Description = "内存深度清理操作已完成！";
//         embed.AddField("清理前", $"**{memoryBefore:F2} MB**", true);
//         embed.AddField("清理后 ✨", $"**{memoryAfter:F2} MB**", true);
//         embed.AddField("释放量", $"**{(memoryBefore - memoryAfter):F2} MB**", true);
//         embed.AddField("SQLite `shrink_memory`", sqliteResult, false);
//         embed.AddField(".NET `GC.Collect`", "✅ 已执行", false);
//         embed.AddField("Linux `malloc_trim`", mallocTrimResult, false);

//         await ModifyOriginalResponseAsync(p => p.Embed = embed.Build());
//     }
}

// public static class NativeMethods
// {
//     // 导入 C 库 (libc) 中的 malloc_trim 函数
//     [DllImport("libc", SetLastError = true)]
//     public static extern int malloc_trim(int pad);
// }