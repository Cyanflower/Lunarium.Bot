using Discord;

using SearchBot.Model;
using LunariumExtensionMethod;

namespace SearchBot.Utils;

public static class DisplayEmbedBuilder
{
    public static List<List<Embed>> ToFavoriteEmbedList(List<List<PostDisplayModel>> postList, string baselineVersion)
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
                            -# 收藏类别: {post.FavoriteAS ?? "未分类"} 收藏时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
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
                        var updatedInfo = post.UpdatedAt != 0 ? $"\n-# 更新时间: <t:{post.UpdatedAt}:f> | <t:{post.UpdatedAt}:R>\n-# 最新作品链接: {post.MessageJumpUrl}" : "";
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
                            -# 收藏类别: {post.FavoriteAS ?? "未分类"} 收藏时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
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
            }
            embeds.Add(embedsChunk);
        }
        return embeds;
    }

    public static List<List<Embed>> ToSubscriptionsEmbedList(List<List<PostDisplayModel>> postList, string baselineVersion)
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
                            订阅时间: <t:{post.AddedAt}:f>|<t:{post.AddedAt}:R>
                            帖子可复制链接:
                            ```
                            {shortJumpUrl}
                            ```
                            -# 数据库基线版本: <t:{baselineVersion}:d>
                            """)
                        .WithFooter($"结果 {postNum}/{totalPosts} | 第 {PageNum} 页，共 {totalPages} 页 | 数据库基线版本: {baselineVersion}")
                        .WithThumbnailUrl(post.AttachmentUrl)
                        .Build();
                    embedsChunk.Add(embed);
                }
                else
                {
                    postNum++;
                    var updatedInfo = post.UpdatedAt != 0 ? $"\n-# 更新时间: <t:{post.UpdatedAt}:f> | <t:{post.UpdatedAt}:R>\n-# 最新作品链接: {post.MessageJumpUrl}" : "";
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
                            -# 更新时间: <t:{post.UpdatedAt}:f> 最新作品链接: {post.MessageJumpUrl}
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
                var updatedInfo = post.UpdatedAt != 0 ? $"\n-# 更新时间: <t:{post.UpdatedAt}:f> | <t:{post.UpdatedAt}:R>\n-# 最新作品链接: {post.MessageJumpUrl}" : "";
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
}