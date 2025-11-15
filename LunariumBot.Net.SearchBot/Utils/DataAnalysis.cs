// .NET 核心库
using System.Text;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot.Utils;

/// <summary>
/// 数据分析服务，负责生成各服务器的数据分析报告
/// </summary>
public class DataAnalysis
{
    private readonly DatabaseService _databaseService;
    private readonly ConfigLoader _configLoader;
    private readonly ILogger _logger;

    public DataAnalysis(DatabaseService databaseService, ConfigLoader configLoader, ILogger logger)
    {
        _databaseService = databaseService;
        _configLoader = configLoader;
        _logger = logger.ForContext("DataAnalysis");
    }

    /// <summary>
    /// 为所有配置的服务器生成数据分析报告
    /// </summary>
    /// <returns>
    /// Key: 服务器ID (GuildId)
    /// Value: 该服务器的分析报告列表（包含总概览和各分类）
    /// </returns>
    public async Task<Dictionary<ulong, List<ForumInfo>>> GenerateAllGuildReportsAsync()
    {
        _logger.Info("🚀 开始为所有服务器生成数据分析报告...");
        var allReports = new Dictionary<ulong, List<ForumInfo>>();

        try
        {
            // 遍历每个配置的服务器
            foreach (var (guildId, guildConfig) in _configLoader.Config.DeployConfig.GuildAnalysisConfigs)
            {
                _logger.Info("📊 正在为服务器 {GuildId} 生成报告...", guildId);

                try
                {
                    var guildReports = await GenerateGuildReportAsync(guildId, guildConfig);
                    allReports[guildId] = guildReports;

                    _logger.Info("✅ 服务器 {GuildId} 的报告生成完成，共 {Count} 个分析项",
                        guildId, guildReports.Count);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 为服务器 {GuildId} 生成报告时发生错误", guildId);
                    // 继续处理其他服务器
                }
            }

            _logger.Info("🎉 所有服务器的数据分析报告生成完成！共处理 {Count} 个服务器", allReports.Count);
            return allReports;
        }
        catch (Exception ex)
        {
            _logger.Critical(ex, "💥 生成数据分析报告时发生严重错误");
            return allReports; // 返回已完成的部分数据
        }
    }

    /// <summary>
    /// 为单个服务器生成完整的数据分析报告
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="guildConfig">该服务器的分析配置</param>
    /// <returns>该服务器的分析报告列表</returns>
    private async Task<List<ForumInfo>> GenerateGuildReportAsync(
        ulong guildId,
        GuildAnalysisConfig guildConfig)
    {
        var reports = new List<ForumInfo>();

        try
        {
            // 1. 生成总概览（该服务器全部频道）
            _logger.Info("  📈 生成服务器 {GuildId} 的总概览...", guildId);
            var overallReport = await AnalyzeCategoryAsync(guildId, "总概览", channelIds: null);
            reports.Add(overallReport);

            // 2. 生成各分类报告
            foreach (var (categoryName, channelIds) in guildConfig.CategoryChannels)
            {
                _logger.Info("  📂 生成服务器 {GuildId} 的分类 '{CategoryName}'...", guildId, categoryName);
                var categoryReport = await AnalyzeCategoryAsync(guildId, categoryName, channelIds);
                reports.Add(categoryReport);
            }

            return reports;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 为服务器 {GuildId} 生成报告时发生错误", guildId);
            throw;
        }
    }

    /// <summary>
    /// 分析指定服务器的某个分类（或总览）
    /// </summary>
    /// <param name="guildId">服务器ID</param>
    /// <param name="categoryName">分类名称（例如："总概览"、"男性向"）</param>
    /// <param name="channelIds">可选：该分类包含的频道ID列表。为 null 时统计整个服务器</param>
    /// <returns>该分类的分析数据</returns>
    private async Task<ForumInfo> AnalyzeCategoryAsync(
        ulong guildId,
        string categoryName,
        List<ulong>? channelIds)
    {
        var info = new ForumInfo { ChannelName = categoryName };

        try
        {
            // 计算时间窗口（简化版：直接以当前 UTC 时间计算）
            var now = DateTimeOffset.UtcNow;
            var start24h = now.AddHours(-24);
            var start7d = now.AddDays(-7);

            // === 帖子统计 ===
            _logger.Debug("    正在统计帖子数据...");
            info.TotalPost = await _databaseService.GetPostCountAsync(guildId, channelIds);
            info.PostAdd24h = await _databaseService.GetPostCountInTimeRangeAsync(
                guildId, start24h, now, channelIds);
            info.PostAdd7d = await _databaseService.GetPostCountInTimeRangeAsync(
                guildId, start7d, now, channelIds);

            // === 回复统计 ===
            _logger.Debug("    正在统计回复数据...");
            info.TotalReply = await _databaseService.GetTotalReplyCountAsync(guildId, channelIds);
            info.ReplyAdd24h = await _databaseService.GetReplyCountInTimeRangeAsync(
                guildId, start24h, now, channelIds);
            info.ReplyAdd7d = await _databaseService.GetReplyCountInTimeRangeAsync(
                guildId, start7d, now, channelIds);

            // === 标签统计 ===
            _logger.Debug("    正在统计热门标签...");
            info.TagData = await _databaseService.GetTopTagsAsync(
                guildId, topN: 5, sinceTime: start7d, channelIds);
            info.TagDataHistory = await _databaseService.GetTopTagsAsync(
                guildId, topN: 5, sinceTime: null, channelIds);

            // === 焦点帖子 ===
            _logger.Debug("    正在统计焦点帖子...");
            info.ReactionTop7d = await _databaseService.GetTopPostsByReactionAsync(
                guildId, topN: 5, sinceTime: start7d, channelIds);
            info.ReplyTop7d = await _databaseService.GetTopPostsByReplyAsync(
                guildId, topN: 5, sinceTime: start7d, channelIds);
            info.ReactionTopHistory = await _databaseService.GetTopPostsByReactionAsync(
                guildId, topN: 5, sinceTime: null, channelIds);
            info.ReplyTopHistory = await _databaseService.GetTopPostsByReplyAsync(
                guildId, topN: 5, sinceTime: null, channelIds);

            _logger.Debug("    ✅ '{CategoryName}' 分析完成 - 总帖: {TotalPost}, 7天新增: {PostAdd7d}",
                categoryName, info.TotalPost, info.PostAdd7d);

            return info;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 分析分类 '{CategoryName}' (服务器: {GuildId}) 时发生错误",
                categoryName, guildId);
            throw;
        }
    }

    /// <summary>
    /// 生成文本格式的数据分析报告（用于上传为附件）
    /// </summary>
    /// <param name="includeTags">是否包含热门标签部分</param>
    /// <param name="includeFocusPosts">是否包含焦点帖子部分</param>
    /// <returns>UTF-8 编码的报告字节数组，如果生成失败则返回 null</returns>
    public async Task<byte[]?> GenerateAnalysisReportAsync(bool includeTags, bool includeFocusPosts)
    {
        _logger.Info("🚀 启动数据分析并生成文本报告 (includeTags: {IncludeTags}, includeFocusPosts: {IncludeFocusPosts})...",
            includeTags, includeFocusPosts);

        try
        {
            // 1. 生成所有服务器的分析数据
            var allReports = await GenerateAllGuildReportsAsync();

            if (!allReports.Any())
            {
                _logger.Warning("分析未生成任何数据，已跳过报告生成。");
                return null;
            }

            // 2. 使用 StringBuilder 构建报告
            var reportBuilder = new StringBuilder();
            var reportTime = DateTimeOffset.UtcNow.AddHours(8); // UTC+8 时间

            // --- 报告头部 ---
            reportBuilder.AppendLine("========================================");
            reportBuilder.AppendLine("📊      数 据 分 析 报 告      📊");
            reportBuilder.AppendLine("========================================");
            reportBuilder.AppendLine($"生成时间: {reportTime:yyyy-MM-dd HH:mm:ss (UTC+8)}");
            reportBuilder.AppendLine();

            // 3. 遍历每个服务器的分析结果
            foreach (var (guildId, reports) in allReports)
            {
                reportBuilder.AppendLine($"🏰 服务器 ID: {guildId}");
                reportBuilder.AppendLine("========================================");
                reportBuilder.AppendLine();

                // 遍历该服务器的每个分析项（总概览、男性向、女性向...）
                foreach (var info in reports)
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
            }

            // 4. 将报告字符串转换为字节数组
            string reportContent = reportBuilder.ToString();
            byte[] reportBytes = Encoding.UTF8.GetBytes(reportContent);

            _logger.Info("✅ 数据分析报告内容已成功生成！总大小: {Size} 字节", reportBytes.Length);
            return reportBytes;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 生成数据分析报告时发生严重错误！");
            return null;
        }
    }

    /// <summary>
    /// 格式化焦点帖子列表
    /// </summary>
    private string FormatTopPostsSection(string title, List<PostDisplayModel> posts)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);

        if (posts is not null && posts.Any())
        {
            int rank = 1;
            foreach (var post in posts)
            {
                // 使用 Markdown 格式创建可点击的链接
                sb.AppendLine($"    {rank++}. [{post.Title}]({post.JumpUrl}) (回应: {post.ReactionCount:N0}, 回复: {post.ReplyCount:N0})");
            }
        }
        else
        {
            sb.AppendLine("    - 暂无符合条件的帖子。");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 格式化热门标签列表
    /// </summary>
    private string FormatTopTagsSection(string title, Dictionary<TagInfo, int> tags)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);

        if (tags is not null && tags.Any())
        {
            int rank = 1;
            // 确保字典按值降序排列
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
