// .NET核心库
using System.Text;

// NuGet 包依赖
using Discord;

// 解决方案内引用项目
using Lunarium.Bot.Extensions;

// 项目内命名空间
using Lunarium.Bot.SearchBot.Models;


namespace Lunarium.Bot.SearchBot.Utils;

public static class DisplayEmbedBuilder
{
    public static Dictionary<string, Embed> ForumInfoToEmbed(List<ForumData> forumInfos)
    {
        var reportTime = $"报告生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}";
        var embeds = new Dictionary<string, Embed>();
        foreach (var info in forumInfos)
        {
            var descriptionBuilder = new StringBuilder();

            // --- 1. 帖子与回复概览 ---
            descriptionBuilder.AppendLine("📈 **帖子与回复概览**");
            descriptionBuilder.AppendLine($"- 总帖子数: {info.TotalPost:N0}");
            descriptionBuilder.AppendLine($"- 7日内新增: {info.PostAdd7d:N0}");
            descriptionBuilder.AppendLine($"- 24小时内新增: {info.PostAdd24h:N0}");
            descriptionBuilder.AppendLine();
            descriptionBuilder.AppendLine($"- 累计总回复: {info.TotalReply:N0}");
            descriptionBuilder.AppendLine($"- 7日内新增回复: {info.ReplyAdd7d:N0}");
            descriptionBuilder.AppendLine($"- 24小时内新增回复: {info.ReplyAdd24h:N0}");
            descriptionBuilder.AppendLine();

            // --- 2. 热门标签 ---
            descriptionBuilder.Append(FormatTopTagsSection("🔥 **热门标签 (近7日)**", info.TagData));
            descriptionBuilder.Append(FormatTopTagsSection("📜 **热门标签 (历史)**", info.TagDataHistory));
            descriptionBuilder.AppendLine();

            // --- 3. 焦点帖子 ---
            descriptionBuilder.AppendLine("🏆 **焦点帖子**");
            descriptionBuilder.Append(FormatTopPostsSection("🌟 **近期热门 (按回应)**", info.ReactionTop7d));
            descriptionBuilder.Append(FormatTopPostsSection("💬 **近期热门 (按回复)**", info.ReplyTop7d));
            descriptionBuilder.Append(FormatTopPostsSection("💎 **历史热门 (按回应)**", info.ReactionTopHistory));
            descriptionBuilder.Append(FormatTopPostsSection("📜 **历史热门 (按回复)**", info.ReplyTopHistory));

            var description = descriptionBuilder.ToString();
            if (description.Length > 4096)
            {
                description = description.Slice(4090, "...");
            }

            var embed = new EmbedBuilder()
                .WithTitle($"{info.ChannelName}")
                .WithDescription(description)
                .WithColor(Color.Blue)
                .WithFooter(reportTime)
                .Build();

            embeds.Add(info.ChannelName ?? "未知分类", embed);
        }
        return embeds;
    }

    /// <summary>
    /// 格式化焦点帖子列表
    /// </summary>
    private static string FormatTopPostsSection(string title, List<PostDisplayModel> posts)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        if (posts is not null && posts.Any())
        {
            int rank = 1;
            foreach (var post in posts)
            {
                // 为了防止标题过长导致embed超限，这里做一个截断
                var postTitle = post.Title is not null ? post.Title.Slice(50, "...") : "未知标题";
                postTitle = postTitle.Replace("[", "").Replace("]", "");
                sb.AppendLine($"{rank++}. [{postTitle}]({post.JumpUrl})\n  👍🏻: {post.ReactionCount:N0} / 💬: {post.ReplyCount:N0}");
            }
        }
        else
        {
            sb.AppendLine("- 暂无符合条件的帖子。");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 格式化热门标签列表。
    /// </summary>
    private static string FormatTopTagsSection(string title, Dictionary<TagData, int> tags)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        if (tags is not null && tags.Any())
        {
            int rank = 1;
            // 确保字典按使用次数降序排列
            foreach (var tag in tags.OrderByDescending(kv => kv.Value))
            {
                // 在Embed里，# 不会创建链接，但这样显示很直观
                sb.AppendLine($"{rank++}. `#{tag.Key.TagName}` (使用: {tag.Value:N0})");
            }
        }
        else
        {
            sb.AppendLine("- 暂无热门标签。");
        }
        return sb.ToString();
    }
}