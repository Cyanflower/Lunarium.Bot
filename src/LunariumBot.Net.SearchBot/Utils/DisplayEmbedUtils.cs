// .NET核心库
using System.Security.Cryptography.X509Certificates;
using System.Text;

// NuGet 包依赖
using Discord;

// 解决方案内引用项目
using LunariumBot.Net.Extensions;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Model;


namespace LunariumBot.Net.SearchBot.Utils;

public static class DisplayEmbedBuilder
{
    public static (List<Embed> Embeds, List<PostDisplayModel> Posts) ToFavoriteEmbedList(List<PostDisplayModel> postList, string baselineVersion)
    {
        var allEmbeds = new List<Embed>();

        foreach (var post in postList)
        {
            // https://discord.com/channels/1291925535324110879/1410172300442992681/0
            // 处理为
            // https://discord.com/channels/1291925535324110879/1410172300442992681
            var shortJumpUrl = post.JumpUrl;
            if (shortJumpUrl != null)
            {
                var lastSlashIndex = shortJumpUrl.LastIndexOf('/');
                if (lastSlashIndex != -1)
                {
                    shortJumpUrl = shortJumpUrl.Substring(0, lastSlashIndex);
                }
            }

            if (post.IsFallbackData)
            {
                var embed = new EmbedBuilder()
                    .WithDescription($"""
                        ## [{post.Title}]({post.JumpUrl})
                        -# 点击标题可以尝试进行访问
                        =====================================
                        **作者**: <@{post.AuthorId}>
                        无详细数据
                        信息来源: 备用表
                        数据库主表未找到, 意味着该贴可能已经处于:
                        - 帖子已锁定
                        - 帖子已删除
                        - 首楼已删除
                        -# 对于首楼删除或锁定的帖子
                        -# 仍可以尝试点击标题或跳转链接进行访问
                        -# 若提示无法访问, 则该帖很可能已删除
                        =====================================
                        """)
                    .AddField("统计信息:", $"""
                        -# 收藏类别: {post.FavoriteAs ?? "未分类"} 收藏时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                        帖子可复制链接:
                        ```
                        {shortJumpUrl}
                        ```
                        """)
                    .WithFooter($"数据库基线版本: {baselineVersion}")
                    .WithThumbnailUrl(post.AttachmentUrl)
                    .Build();
                allEmbeds.Add(embed);
            }
            else
            {
                var updatedInfo = post.UpdatedAt != 0 ? $"\n-# 更新时间: <t:{post.UpdatedAt}:f> | <t:{post.UpdatedAt}:R>\n-# 最新作品链接: {post.UpdateMessageJumpUrl}" : "";
                var embed = new EmbedBuilder()
                    .WithDescription($"""
                        ## [{post.Title}]({post.JumpUrl})
                        **作者**: <@{post.AuthorId}>
                        -# {post.GuildName}.{post.ChannelName}
                        -# 帖子信息: 👍🏻: {post.ReactionCount} | 💬: {post.ReplyCount} | 🏷: {post.Tags}
                        -# 发帖时间: <t:{post.CreatedAt}:f> 最近活跃: <t:{post.LastActiveAt}:R>{updatedInfo}
                        =====================================
                        **首楼摘要**: [点此跳转帖子首页]({post.JumpUrl})
                        {(post.Content != null ? post.Content.Slice(75, "...") : "无首楼内容")}
                        =====================================
                        """)
                    .AddField("收藏信息:", $"""
                        -# 收藏类别: {post.FavoriteAs ?? "未分类"} 收藏时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                        帖子可复制链接:
                        ```
                        {shortJumpUrl}
                        ```
                        """)
                    .WithFooter($"数据库基线版本: {baselineVersion}")
                    .WithThumbnailUrl(post.AttachmentUrl)
                    .Build();
                allEmbeds.Add(embed);
            }

        }
        return (allEmbeds, postList);
    }

    public static (List<Embed> Embeds, List<PostDisplayModel> Posts) ToSubscriptionsEmbedList(List<PostDisplayModel> postList, string baselineVersion)
    {
        var allEmbeds = new List<Embed>();

        foreach (var post in postList)
        {
            // https://discord.com/channels/1291925535324110879/1410172300442992681/0
            // 处理为
            // https://discord.com/channels/1291925535324110879/1410172300442992681
            var shortJumpUrl = post.JumpUrl;
            if (shortJumpUrl != null)
            {
                var lastSlashIndex = shortJumpUrl.LastIndexOf('/');
                if (lastSlashIndex != -1)
                {
                    shortJumpUrl = shortJumpUrl.Substring(0, lastSlashIndex);
                }
            }

            if (post.IsFallbackData)
            {
                var embed = new EmbedBuilder()
                    .WithDescription($"""
                        ## [{post.Title}]({post.JumpUrl})
                        -# 点击标题可以尝试进行访问
                        =====================================
                        **作者**: <@{post.AuthorId}>
                        无详细数据
                        信息来源: 备用表
                        数据库主表未找到, 意味着该贴可能已经处于:
                        - 帖子已锁定
                        - 帖子已删除
                        - 首楼已删除
                        -# 对于首楼删除或锁定的帖子
                        -# 仍可以尝试点击标题或跳转链接进行访问
                        -# 若提示无法访问, 则该帖很可能已删除
                        =====================================
                        """)
                    .AddField("统计信息:", $"""
                        订阅时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                        帖子可复制链接:
                        ```
                        {shortJumpUrl}
                        ```
                        -# 数据库基线版本: <t:{baselineVersion}:d>
                        """)
                    .WithFooter($"数据库基线版本: {baselineVersion}")
                    .WithThumbnailUrl(post.AttachmentUrl)
                    .Build();
                allEmbeds.Add(embed);
            }
            else
            {
                var updatedInfo = post.UpdatedAt != 0 ? $"\n-# 更新时间: <t:{post.UpdatedAt}:f> | <t:{post.UpdatedAt}:R>\n-# 最新作品链接: {post.UpdateMessageJumpUrl}" : "";
                var embed = new EmbedBuilder()
                    .WithDescription($"""
                        ## [{post.Title}]({post.JumpUrl})
                        **作者**: <@{post.AuthorId}>
                        -# {post.GuildName}.{post.ChannelName}
                        -# 帖子信息: 👍🏻: {post.ReactionCount} | 💬: {post.ReplyCount} | 🏷: {post.Tags}
                        -# 发帖时间: <t:{post.CreatedAt}:f> 最近活跃: <t:{post.LastActiveAt}:R>{updatedInfo}
                        =====================================
                        **首楼摘要**: [点此跳转帖子首页]({post.JumpUrl})
                        {(post.Content != null ? post.Content.Slice(75, "...") : "无首楼内容")}
                        =====================================
                        """)
                    .AddField("订阅信息:", $"""
                        -# 订阅时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                        帖子可复制链接:
                        ```
                        {shortJumpUrl}
                        ```
                        """)
                    .WithFooter($"数据库基线版本: {baselineVersion}")
                    .WithThumbnailUrl(post.AttachmentUrl)
                    .Build();
                allEmbeds.Add(embed);
            }
        }
        return (allEmbeds, postList);
    }

    public static List<List<Embed>> ToSubPushListEmbedList(List<List<PostDisplayModel>> postList, string baselineVersion)
    {
        var embeds = new List<List<Embed>>();
        var totalPosts = postList.Sum(chunk => chunk.Count);
        var totalPages = postList.Count;
        var PageNum = 0;
        var postNum = 0;
        foreach (var postChunk in postList)
        {
            PageNum++;
            var embedsChunk = new List<Embed>();
            foreach (var post in postChunk)
            {
                // https://discord.com/channels/1291925535324110879/1410172300442992681/0
                // 处理为
                // https://discord.com/channels/1291925535324110879/1410172300442992681
                var shortJumpUrl = post.JumpUrl;
                if (shortJumpUrl != null)
                {
                    var lastSlashIndex = shortJumpUrl.LastIndexOf('/');
                    if (lastSlashIndex != -1)
                    {
                        shortJumpUrl = shortJumpUrl.Substring(0, lastSlashIndex);
                    }
                }
                if (post.IsFallbackData)
                {
                    postNum++;
                    var embed = new EmbedBuilder()
                        .WithDescription($"""
                            ## [{post.Title}]({post.JumpUrl})
                            -# 点击标题可以尝试进行访问
                            =====================================
                            **作者**: <@{post.AuthorId}>
                            无详细数据
                            信息来源: 备用表
                            数据库主表未找到, 意味着该贴可能已经处于:
                            - 帖子已锁定
                            - 帖子已删除
                            - 首楼已删除
                            -# 对于首楼删除或锁定的帖子
                            -# 仍可以尝试点击标题或跳转链接进行访问
                            -# 若提示无法访问, 则该帖很可能已删除
                            =====================================
                            """)
                        .AddField("统计信息:", $"""
                            -# 订阅时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                            帖子可复制链接:
                            ```
                            {shortJumpUrl}
                            ```
                            """)
                        .WithFooter($"结果 {postNum}/{totalPosts} | 第 {PageNum} 页，共 {totalPages} 页 | 数据库基线版本: {baselineVersion}")
                        .WithThumbnailUrl(post.AttachmentUrl)
                        .Build();
                    embedsChunk.Add(embed);
                }
                else
                {
                    postNum++;
                    var embed = new EmbedBuilder()
                        .WithDescription($"""
                            ## [{post.Title}]({post.JumpUrl})
                            **作者**: <@{post.AuthorId}>
                            -# {post.GuildName}.{post.ChannelName}
                            -# 帖子信息: 👍🏻: {post.ReactionCount} | 💬: {post.ReplyCount} | 🏷: {post.Tags}
                            -# 发帖时间: <t:{post.CreatedAt}:f> 最近活跃: <t:{post.LastActiveAt}:R>
                            该贴子有作品更新:
                            -# 更新时间: <t:{post.UpdatedAt}:f> 最新作品链接: {post.UpdateMessageJumpUrl}
                            =====================================
                            **首楼摘要**: [点此跳转帖子首页]({post.JumpUrl})
                            {(post.Content != null ? post.Content.Slice(75, "...") : "无首楼内容")}
                            =====================================
                            """)
                        .AddField("订阅信息:", $"""
                            -# 订阅时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                            """)
                        .WithFooter($"结果 {postNum}/{totalPosts} | 第 {PageNum} 页，共 {totalPages} 页 | 数据库基线版本: {baselineVersion}")
                        .WithThumbnailUrl(post.AttachmentUrl)
                        .Build();
                    embedsChunk.Add(embed);
                }
            }
            embeds.Add(embedsChunk);
        }
        return embeds;
    }

    public static List<List<Embed>> ToSearchResultEmbedList(List<List<PostDisplayModel>> postList, string baselineVersion)
    {
        var embeds = new List<List<Embed>>();
        var totalPosts = postList.Sum(chunk => chunk.Count);
        var totalPages = postList.Count;
        var PageNum = 0;
        var postNum = 0;
        foreach (var postChunk in postList)
        {
            PageNum++;
            var embedsChunk = new List<Embed>();
            foreach (var post in postChunk)
            {
                postNum++;
                var updatedInfo = post.UpdatedAt != 0 ? $"\n-# 更新时间: <t:{post.UpdatedAt}:f> | <t:{post.UpdatedAt}:R>\n-# 最新作品链接: {post.UpdateMessageJumpUrl}" : "";
                var embed = new EmbedBuilder()
                        .WithDescription($"""
                            ## [{post.Title}]({post.JumpUrl})
                            **作者**: <@{post.AuthorId}>
                            -# {post.GuildName}.{post.ChannelName}
                            -# 帖子信息: 👍🏻: {post.ReactionCount} | 💬: {post.ReplyCount} | 🏷: {post.Tags}
                            -# 发帖时间: <t:{post.CreatedAt}:f> 最近活跃: <t:{post.LastActiveAt}:R>{updatedInfo}
                            =====================================
                            **首楼摘要**: [点此跳转帖子首页]({post.JumpUrl})
                            {(post.Content != null ? post.Content.Slice(75, "...") : "无首楼内容")}
                            =====================================
                            """)
                    .WithFooter($"结果 {postNum}/{totalPosts} | 第 {PageNum} 页，共 {totalPages} 页 | 数据库基线版本: {baselineVersion}")
                    .WithThumbnailUrl(post.AttachmentUrl)
                    .Build();
                embedsChunk.Add(embed);
            }
            embeds.Add(embedsChunk);
        }
        return embeds;
    }

    public static Dictionary<string, Embed> ForumInfoToEmbed(List<ForumInfo> forumInfos)
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
    private static string FormatTopTagsSection(string title, Dictionary<TagInfo, int> tags)
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

    public static Embed DirectSearchEmbed()
    {
        var embed = new EmbedBuilder()
                .WithDescription("""
                    ## 本BOT斜杠命令简介:
                    **搜索与快捷面板**
                    > /搜索: 根据作者、关键词、标签等多种条件筛选和搜索帖子。
                    > /收藏订阅面板: 创建一个只有你自己能看的快捷操作面板, 方便搜索查看。
                    > /添加快捷搜索: 保存一套复杂的搜索条件, 方便以后一键搜索。
                    > /快捷搜索: 执行你保存过的快捷搜索命令。
                    > /删除快捷搜索: 删除一个不再需要的快捷搜索命令。
                    **收藏功能**
                    > /收藏: (在帖子里使用) 将当前帖子添加到你的收藏夹。
                    > /取消收藏: (在帖子里使用) 将当前帖子从你的收藏夹中移除。
                    > /查看收藏: 查看你收藏的所有帖子, 可以按收藏夹分类查看。
                    > /取消收藏-链接: 通过粘贴帖子的链接来取消收藏, 无需进入帖子。
                    > /重命名收藏夹: 给你的收藏夹换个新名字。
                    > /删除收藏夹: 删除指定的收藏夹, 并选择如何处理里面的帖子。
                    **订阅功能**
                    > /订阅: (在指定分区的帖子里使用) 订阅帖子的新作品更新！
                    > /取消订阅: (在帖子里使用) 取消对该帖子的更新通知。
                    > /查看订阅: 列出你所有订阅的帖子。
                    > /取消订阅-链接: 通过粘贴帖子链接来取消订阅。
                    > /查看更新: 查看你订阅的帖子里有哪些新作品的推送。
                    > /清空更新: 清空所有已收到的作品更新通知
                    **快捷使用**
                    > 部分功能亦可通过在服务器界面内右键 -> Apps 快捷使用
                    > 也可通过在服务器内任意频道使用/收藏订阅面板快速创建本面板
                    """)
                .Build();
        return embed;
    }
}