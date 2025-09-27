using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;

using LunariumBot.Net.Archive.Archiver;
using LunariumBot.Net.Archive.Model;
using LunariumBot.Net.Archive.Utils;
using LunariumBot.Net.Logger;

namespace LunariumBot.Net.Archive.AppInteractionService.Commands;

/// <summary>
/// 优先归档相关命令
/// </summary>
[Group("优先归档", "优先归档相关命令")]
public class PriorityArchiveCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ArchiverService _archiverService;
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;

    // 使用依赖注入来获取服务实例
    public PriorityArchiveCommands(ArchiverService archiverService, AuthUtils authUtils, ILogger logger)
    {
        _archiverService = archiverService;
        _authUtils = authUtils;
        _logger = logger.ForContext("PriorityArchiveCommands");
    }

    /// <summary>
    /// 添加帖子到优先归档列表
    /// </summary>
    [SlashCommand("添加", "添加帖子到优先归档列表")]
    public async Task AddPriorityArchive(
        [Summary("链接", "需要添加的帖子链接")] string threadUrl,
        [Summary("原因", "添加原因")] string reason,
        [Summary("权重", "权重值 (越高越优先归档, 推荐 1-65535)")] ushort priority
    )
    {
        // 延迟响应，防止3秒超时
        await DeferAsync(ephemeral: true);

        // 鉴权：必须是管理员
        if (!await _authUtils.IsAdmin("优先归档 添加", Context))
        {
            return;
        }

        // 权重校验
        if (priority <= 0 || priority > 65535)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("无法添加", "权重必须大于0且小于65535"), ephemeral: true);
        }

        // 解析帖子链接
        if (!TryParseThreadUrl(threadUrl, out var threadId, out var guildId))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("无法添加", "无效的 Discord 帖子链接格式。"), ephemeral: true);
            return;
        }
        
        // 验证服务器ID是否匹配
        if (guildId != Context.Guild.Id)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("无法添加", "不能添加其他服务器的帖子。"), ephemeral: true);
            return;
        }

        // 获取帖子对象
        var thread = await Context.Client.GetChannelAsync(threadId) as IThreadChannel;
        if (thread == null)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("无法添加", "找不到该帖子，请检查链接是否正确或帖子是否已被删除。"), ephemeral: true);
            return;
        }

        // 构建要添加的数据模型
        var priorityInfo = new PriorityThreadInfo
        {
            ThreadId = thread.Id,
            Priority = priority,
            Reason = reason,
            ThreadJumpUrl = threadUrl,
            AuthorId = thread.OwnerId,
            Adder = Context.User.Id
        };

        // 调用服务执行添加操作
        var (result, message) = _archiverService.AddPriorityArchive(priorityInfo);
        
        // 根据结果返回不同的反馈
        if (result)
        {
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed(message), ephemeral: true);
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed(message), ephemeral: true);
        }
    }

    /// <summary>
    /// 从优先归档列表移除帖子
    /// </summary>
    [SlashCommand("移除", "从优先归档列表移除帖子")]
    public async Task RemovePriorityArchive(
        [Summary("链接", "需要移除的帖子链接")] string threadUrl
    )
    {
        await DeferAsync(ephemeral: true);

        // 鉴权：必须是管理员
        if (!await _authUtils.IsAdmin("优先归档 移除", Context))
        {
            return;
        }
        
        // 解析帖子链接
        if (!TryParseThreadUrl(threadUrl, out var threadId, out _))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("无法移除", "无效的 Discord 帖子链接格式。"), ephemeral: true);
            return;
        }

        // 构建一个仅包含ID的对象用于移除
        var priorityInfo = new PriorityThreadInfo { ThreadId = threadId };
        
        var (result, message) = _archiverService.RemovePriorityArchive(priorityInfo);

        if (result)
        {
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed(message), ephemeral: true);
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("移除失败", message), ephemeral: true);
        }
    }

    /// <summary>
    /// 查看当前优先归档帖子列表
    /// </summary>
    [SlashCommand("查看列表", "查看当前优先归档帖子列表")]
    public async Task ListPriorityArchives()
    {
        await DeferAsync(ephemeral: true);
        
        // 鉴权：必须是管理员
        if (!await _authUtils.IsAdmin("优先归档 查看列表", Context))
        {
            return;
        }
        
        var priorityList = _archiverService.GetPriorityArchive();

        if (priorityList.Count == 0)
        {
            await FollowupAsync(embed: EmbedUtils.InfoEmbed("列表为空", "当前没有帖子在优先归档列表中。"), ephemeral: true);
            return;
        }

        var sb = new StringBuilder();
        // 按权重从高到低排序，更直观
        var sortedList = priorityList.Values.OrderByDescending(p => p.Priority);

        var page = 1;
        foreach (var item in sortedList)
        {
            var entry = $"""
                - 帖子: {item.ThreadId} | 链接: {item.ThreadJumpUrl}
                > 作者: <@{item.AuthorId}> | 权重: {item.Priority} 
                > 原因: {item.Reason} | 添加人: <@{item.Adder}>
                """;

            // Discord消息长度限制为2000，我们设置一个保守的阈值
            if (sb.Length + entry.Length > 3800)
            {
                page++;
                var embed = new EmbedBuilder()
                    .WithTitle($"📜 优先归档列表 (共 {priorityList.Count} 条)")
                    .WithColor(Color.Blue)
                    .WithDescription(sb.ToString())
                    .WithFooter($"第 {page} 页")
                    .Build();
                // 发送当前拼接好的消息，然后清空
                await FollowupAsync(embed:embed, ephemeral: true);
                sb.Clear();
            }
            sb.Append(entry);
        }

        // 发送最后剩余的部分
        if (sb.Length > 0)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"📜 优先归档列表 (共 {priorityList.Count} 条)")
                .WithColor(Color.Blue)
                .WithDescription(sb.ToString())
                .WithFooter($"第 {page} 页")
                .Build();
            await FollowupAsync(embed:embed, ephemeral: true);
        }
    }
    
    /// <summary>
    /// 查看目标帖子是否在优先帖子列表
    /// </summary>
    [SlashCommand("检查目标帖子", "查看目标帖子是否在优先帖子列表")]
    public async Task CheckPriorityArchive(
        [Summary("链接", "需要查询的帖子链接")] string threadUrl
    )
    {
        await DeferAsync(ephemeral: true);
        
        // 鉴权：必须是管理员
        if (!await _authUtils.IsAdmin("优先归档 添加", Context))
        {
            return;
        }
        
        if (!TryParseThreadUrl(threadUrl, out var threadId, out _))
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("查询失败", "无效的 Discord 帖子链接格式。"), ephemeral: true);
            return;
        }
        
        var list = _archiverService.GetPriorityArchive();
        
        if (list.TryGetValue(threadId, out var info))
        {
            var embed = new EmbedBuilder()
                .WithTitle("✅ 在优先归档列表中")
                .WithDescription($"帖子 <#{threadId}> 存在于优先归档列表中。")
                .AddField("权重", $"`{info.Priority}`", inline: true)
                .AddField("添加人", $"<@{info.Adder}>", inline: true)
                .AddField("原因", info.Reason)
                .AddField("帖子链接", info.ThreadJumpUrl)
                .AddField("作者", $"<@{info.AuthorId}>")
                .WithColor(Color.Green)
                .Build();
            await FollowupAsync(embed: embed, ephemeral: true);
        }
        else
        {
            var embed = EmbedUtils.InfoEmbed("ℹ️ 查询结果", $"帖子 <#{threadId}> **不**在优先归档列表中。");
            await FollowupAsync(embed: embed, ephemeral: true);
        }
    }

    /// <summary>
    /// 辅助方法：解析Discord链接获取ID
    /// </summary>
    private bool TryParseThreadUrl(string url, out ulong threadId, out ulong guildId)
    {
        threadId = 0;
        guildId = 0;
        // 匹配 https://discord.com/channels/GUILD_ID/THREAD_ID/...
        var match = Regex.Match(url, @"/channels/(\d+)/(\d+)");
        if (match.Success && ulong.TryParse(match.Groups[1].Value, out guildId) && ulong.TryParse(match.Groups[2].Value, out threadId))
        {
            return true;
        }
        return false;
    }
}