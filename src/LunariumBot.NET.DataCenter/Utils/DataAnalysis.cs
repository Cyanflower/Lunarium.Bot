// .NET 核心库
using System.Text;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;


namespace LunariumBot.Net.DataCenter.Utils;

public class DataAnalysis
{
    private readonly DatabaseService _databaseService;
    private readonly ConfigLoader _configLoader;
    private readonly ILogger _logger;

    public DataAnalysis(DatabaseService databaseService, ConfigLoader configLoader, ILogger logger)
    {
        _databaseService = databaseService;
        _configLoader = configLoader;
        _logger = logger;
    }

    public async Task<List<ForumInfo>> StartAnalysis()
    {
        var dataList = new List<ForumInfo>();

        try
        {
            // 先执行总概览分析
            _logger.Info("开始执行总概览数据分析...");
            var overallData = await Analysis(null);
            dataList.Add(overallData);

            // 根据配置的各个分类进行分析
            foreach (var category in _configLoader.Config.GuildsConfig.ScanChannelIDS.Keys)
            {
                _logger.Info("开始执行分类 '{category}' 的数据分析...", category);
                var categoryData = await Analysis(category);
                dataList.Add(categoryData);
            }

            _logger.Info("数据分析完成，共生成 {dataList.Count} 个分析报告", dataList.Count);
            return dataList;
        }
        catch (Exception ex)
        {
            _logger.Error("执行数据分析时发生错误", ex);
            return dataList; // 返回已完成的部分数据
        }
    }

    private async Task<ForumInfo> Analysis(string? channelName)
    {
        var data = new ForumInfo();
        List<ulong>? channelIds = null;

        try
        {
            data.ChannelName = channelName ?? "总概览";

            if (channelName != null)
            {
                if (_configLoader.Config.GuildsConfig.ScanChannelIDS.TryGetValue(channelName, out var ids))
                {
                    channelIds = ids;
                }
                else
                {
                    _logger.Warning("配置中未找到分类 '{channelName}' 对应的频道ID", channelName);
                    return data;
                }
            }

            // 计算日期字符串，东八区午夜4点作为新一天的开始
            var today = DateTimeOffset.UtcNow.AddHours(4).ToString("yyyy-MM-dd");
            var oneDayAgoDate = DateTimeOffset.UtcNow.AddHours(4).AddDays(-1).ToString("yyyy-MM-dd");
            var sevenDaysAgoDate = DateTimeOffset.UtcNow.AddHours(4).AddDays(-7).ToString("yyyy-MM-dd");
            var sevenDaysAgoTimestamp = DateTimeOffset.UtcNow.AddHours(4).AddDays(-7).ToUnixTimeSeconds();


            // === 帖子统计 ===
            data.TotalPost = await _databaseService.GetTotalPostCountAsync(channelIds); 
            data.PostAdd24h = await _databaseService.GetPostCountSinceDateAsync(today, channelIds); // 改为查询今天
            data.PostAdd7d = await _databaseService.GetPostCountSinceDateAsync(sevenDaysAgoDate, channelIds);;

            // === 回复统计 ===
            data.TotalReply = await _databaseService.GetTotalReplyCountFromPostsAsync( channelIds);
            data.ReplyAdd24h = await _databaseService.GetTotalReplyCountFromStatsAsync(today, channelIds);
            data.ReplyAdd7d = await _databaseService.GetTotalReplyCountFromStatsAsync(sevenDaysAgoDate, channelIds);

            // === 标签概览 (7天内热门前5) ===
            data.TagData = await _databaseService.GetTopTagsAsync(5, sevenDaysAgoTimestamp, channelIds);
            // 获取历史热门标签
            // 注意：调用同一个方法，但 sinceTimestamp 参数不传 (即为 null)
            data.TagDataHistory = await _databaseService.GetTopTagsAsync(5, null, channelIds);

            // === 焦点帖子 ===
            data.ReactionTop7d = await _databaseService.GetTopPostsByMetricAsync("reaction_count", 3, sevenDaysAgoTimestamp, channelIds);
            data.ReplyTop7d = await _databaseService.GetTopPostsByMetricAsync("reply_count", 3, sevenDaysAgoTimestamp, channelIds);
            data.ReactionTopHistory = await _databaseService.GetTopPostsByMetricAsync("reaction_count", 3, sinceTimestamp: null, channelIds);
            data.ReplyTopHistory = await _databaseService.GetTopPostsByMetricAsync("reply_count", 3, null, channelIds);

            _logger.Info("'{data.ChannelName}' 数据分析完成 - 总帖数: {data.TotalPost}, 7天新增: {data.PostAdd7d}", data.ChannelName, data.TotalPost, data.PostAdd7d);

            return data;
        }
        catch (Exception ex)
        {
            _logger.Error("分析 '{data.ChannelName}' 数据时发生错误", ex, data.ChannelName);
            return data;
        }
    }

    /// <summary>
    /// 运行一次完整的数据分析，并将格式化后的结果作为字节数组返回，用于上传。
    /// </summary>
    /// <param name="includeTags">是否在报告中包含热门标签部分。</param>
    /// <param name="includeFocusPosts">是否在报告中包含焦点帖子部分。</param>
    /// <returns>包含报告内容的字节数组。</returns>
    public async Task<byte[]?> GenerateAnalysisReportAsync(bool includeTags, bool includeFocusPosts)
    {
        _logger.Info("🚀 启动数据分析并生成报告 (includeTags: {includeTags}, includeFocusPosts: {includeFocusPosts})...", includeTags, includeFocusPosts);

        // 1. 调用核心分析方法
        var analysisResults = await StartAnalysis();

        if (analysisResults == null || !analysisResults.Any())
        {
            _logger.Warning("分析未能生成任何数据，已跳过报告生成。");
            return null;
        }

        // 2. 使用 StringBuilder 高效地构建报告字符串
        var reportBuilder = new StringBuilder();
        var reportTime = DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8)); // 使用东八区时间

        // --- 报告头部 ---
        reportBuilder.AppendLine("========================================");
        reportBuilder.AppendLine("📊      数 据 分 析 报 告      📊");
        reportBuilder.AppendLine("========================================");
        reportBuilder.AppendLine($"生成时间: {reportTime:yyyy-MM-dd HH:mm:ss (UTC+8)}");
        reportBuilder.AppendLine();

        // 3. 遍历每一个分析结果 (总览, 男性向, 女性向...)
        foreach (var info in analysisResults)
        {
            reportBuilder.AppendLine($"--- 📄 分析项: {info.ChannelName} ---");
            reportBuilder.AppendLine();

            // --- 概览统计 ---
            reportBuilder.AppendLine("📈 **帖子与回复概览**");
            reportBuilder.AppendLine($"- 总帖子数: {info.TotalPost:N0}");
            reportBuilder.AppendLine($"- 7日内新增: {info.PostAdd7d:N0}");
            reportBuilder.AppendLine($"- 24小时内新增: {info.PostAdd24h:N0}");
            reportBuilder.AppendLine();
            reportBuilder.AppendLine($"- 累计总回复: {info.TotalReply:N0}");
            reportBuilder.AppendLine($"- 7日内新增回复: {info.ReplyAdd7d:N0}");
            reportBuilder.AppendLine($"- 24小时内新增回复: {info.ReplyAdd24h:N0}");
            reportBuilder.AppendLine();

            // --- 热门标签 (根据参数决定是否显示) ---
            if (includeTags)
            {
                reportBuilder.Append(FormatTopTagsSection("🔥 **热门标签 (近7日)**", info.TagData));
                reportBuilder.Append(FormatTopTagsSection("📜 **热门标签 (历史)**", info.TagDataHistory));
                reportBuilder.AppendLine();
            }

            // --- 焦点帖子 (根据参数决定是否显示) ---
            if (includeFocusPosts)
            {
                reportBuilder.AppendLine("🏆 **焦点帖子**");
                reportBuilder.Append(FormatTopPostsSection("  - 🌟 近期热门 (按回应)", info.ReactionTop7d));
                reportBuilder.Append(FormatTopPostsSection("  - 💬 近期热门 (按回复)", info.ReplyTop7d));
                reportBuilder.Append(FormatTopPostsSection("  - 💎 历史热门 (按回应)", info.ReactionTopHistory));
                reportBuilder.Append(FormatTopPostsSection("  - 📜 历史热门 (按回复)", info.ReplyTopHistory));
            }
            
            reportBuilder.AppendLine("----------------------------------------");
            reportBuilder.AppendLine();
        }

        // 4. 将报告字符串转换为字节数组
        try
        {
            string reportContent = reportBuilder.ToString();
            byte[] reportBytes = Encoding.UTF8.GetBytes(reportContent);
            _logger.Info("✅ 数据分析报告内容已成功生成！");
            return reportBytes;
        }
        catch (Exception ex)
        {
            _logger.Error("❌ 生成数据分析报告时发生严重错误！", ex);
            return null;
        }
    }

    /// <summary>
    /// 一个私有的辅助方法，用于格式化焦点帖子列表，让代码更整洁！
    /// </summary>
    private string FormatTopPostsSection(string title, List<PostDisplayModel> posts)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        if (posts != null && posts.Any())
        {
            int rank = 1;
            foreach (var post in posts)
            {
                // 使用Markdown格式创建可点击的链接
                sb.AppendLine($"    {rank++}. [{post.Title}]({post.JumpUrl}) (回应: {post.ReactionCount:N0}, 回复: {post.ReplyCount:N0})");
            }
        }
        else
        {
            sb.AppendLine("    - 暂无符合条件的帖子。");
        }
        return sb.ToString();
    }
    private string FormatTopTagsSection(string title, Dictionary<TagInfo, long> tags)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        if (tags != null && tags.Any())
        {
            int rank = 1;
            // 确保字典是按值降序排列的
            foreach (var tag in tags.OrderByDescending(kv => kv.Value))
            {
                sb.AppendLine($"{rank++}. #{tag.Key.ChannelName}.{tag.Key.TagName} (使用次数: {tag.Value:N0})");
            }
        }
        else
        {
            sb.AppendLine("- 暂无热门标签。");
        }
        return sb.ToString();
    }
}
