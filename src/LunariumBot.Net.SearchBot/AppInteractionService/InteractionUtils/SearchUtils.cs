// NuGet 包依赖
using Discord;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Scheduler;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public static class SearchUtils
{
    /// <summary>
    /// 执行搜索并显示结果的通用方法
    /// </summary>
    public static async Task ExecuteAndDisplaySearchAsync(ILogger logger, DatabaseService databaseService, PaginatorManager paginatorManager,SearchCriteria criteria, IDiscordInteraction interaction)
    {
        // logger.LogDebug("ExecuteAndDisplaySearchAsync 开始调用搜索方法");
        var searchResult = await databaseService.SearchPostsAsync(criteria);

        if (searchResult == null || !searchResult.Any())
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle("大海捞针")
                .WithDescription("没有找到符合条件的帖子哦，试试换个关键词吧！")
                .WithColor(Color.Blue)
                .Build();
            await interaction.FollowupAsync(embed: emptyEmbed, ephemeral: true);
            logger.Warning("用户 {User} 的搜索没有结果", interaction.User.Username);
            return;
        }
        // logger.LogDebug("ExecuteAndDisplaySearchAsync 搜索方法完毕");

        var postPages = searchResult.Chunk(6).Select(chunk => chunk.ToList()).ToList();


        var embeds = DisplayEmbedBuilder.ToSearchResultEmbedList(postPages, databaseService.BaselineVersion);

        // logger.LogDebug("ExecuteAndDisplaySearchAsync 数据模型转为embed列表完成");

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var uuid = $"{interaction.User.Id}_{currentTime}_search";
        var currentNum = 1;
        var totalPages = embeds.Count;

        paginatorManager.Register(uuid, embeds);

        logger.Debug("ExecuteAndDisplaySearchAsync 注册分页完成");

        bool isLastPage = totalPages == 1;

        var button = new ComponentBuilder()
            .WithButton("首页", $"P_first_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton("上一页", $"P_prev_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: true)
            .WithButton($"{currentNum}/{totalPages}", $"P_jump_{totalPages}_{uuid}", style: ButtonStyle.Secondary)
            .WithButton("下一页", $"P_next_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .WithButton("尾页", $"P_last_{currentNum}_{totalPages}_{uuid}", style: ButtonStyle.Primary, disabled: isLastPage)
            .Build();

        // logger.LogDebug("ExecuteAndDisplaySearchAsync 构建交互组件完成");
        try
        {
            await interaction.FollowupAsync(embeds: embeds[0].ToArray(), components: button, ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.Error("发送FollowupAsync回复失败",ex);
        }
        logger.Info($"为用户 {interaction.User.Username} 找到了 {searchResult.Count} 条结果并已发送");
    }
}