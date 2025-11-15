// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

[Group("屏蔽功能", "屏蔽功能相关命令")]
public class BlockCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;

    public BlockCommands(ILogger logger, AuthUtils authUtils, DatabaseService databaseService)
    {
        _logger = logger.ForContext("BlockCommands");
        _authUtils = authUtils;
        _databaseService = databaseService;
    }
    
    // --- 内部辅助方法 ---
    private async Task BlockAuthorAsync(IUser author)
    {
        if (author.Id == Context.User.Id)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", "不能屏蔽自己哦！"), ephemeral: true);
            return;
        }

        var success = await _databaseService.AddBlockedAuthorAsync(Context.User.Id, author.Id);
        if (success)
        {
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("屏蔽成功！", $"用户 **{author.Username}** 已被你屏蔽，之后在搜索结果中将不会再看到TA的帖子。"), ephemeral: true);
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.InfoEmbed("提示", $"用户 **{author.Username}** 已经在你的屏蔽列表中了。"), ephemeral: true);
        }
    }

    private async Task UnblockAuthorAsync(IUser author)
    {
        var success = await _databaseService.RemoveBlockedAuthorAsync(Context.User.Id, author.Id);
        if (success)
        {
            await FollowupAsync(embed: EmbedUtils.SuccessEmbed("取消屏蔽成功！", $"用户 **{author.Username}** 已从你的屏蔽列表中移除。"), ephemeral: true);
        }
        else
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作无效", $"用户 **{author.Username}** 本来就不在你的屏蔽列表中。"), ephemeral: true);
        }
    }

    // --- 斜杠命令 ---

    [SlashCommand("屏蔽本帖作者", "在搜索结果中屏蔽当前帖子的作者")]
    public async Task BlockCurrentThreadAuthor()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InScanChannelThread("屏蔽本帖作者", Context)) return;

        if (Context.Channel is IThreadChannel threadChannel)
        {
            var owner = await threadChannel.GetUserAsync(threadChannel.OwnerId);
            if(owner != null)
            {
                await BlockAuthorAsync(owner);
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "无法获取到该帖子的作者信息。"), ephemeral: true);
            }
        }
    }

    [SlashCommand("屏蔽指定作者", "在搜索结果中屏蔽一位指定的用户")]
    public async Task BlockSpecifiedAuthor([Summary("用户", "要屏蔽的用户")] IUser user)
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("屏蔽指定作者", Context)) return;
        
        await BlockAuthorAsync(user);
    }

    [SlashCommand("取消屏蔽本帖作者", "将当前帖子的作者从你的屏蔽列表中移除")]
    public async Task UnblockCurrentThreadAuthor()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InScanChannelThread("取消屏蔽本帖作者", Context)) return;

        if (Context.Channel is IThreadChannel threadChannel)
        {
            var owner = await threadChannel.GetUserAsync(threadChannel.OwnerId);
            if(owner != null)
            {
                await UnblockAuthorAsync(owner);
            }
            else
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("操作失败", "无法获取到该帖子的作者信息。"), ephemeral: true);
            }
        }
    }

    [SlashCommand("取消屏蔽指定作者", "将指定的用户从你的屏蔽列表中移除")]
    public async Task UnblockSpecifiedAuthor([Summary("用户", "要取消屏蔽的用户")] IUser user)
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("取消屏蔽指定作者", Context)) return;
        
        await UnblockAuthorAsync(user);
    }
    
    [SlashCommand("查看屏蔽名单", "查看你当前屏蔽的所有作者")]
    public async Task ViewBlockedAuthors()
    {
        await DeferAsync(ephemeral: true);
        if (!await _authUtils.InGuild("查看屏蔽名单", Context)) return;

        var blockedIds = await _databaseService.GetBlockedAuthorsAsync(Context.User.Id);

        if (!blockedIds.Any())
        {
            await FollowupAsync(embed: EmbedUtils.InfoEmbed("你的屏蔽列表是空的", "你没有屏蔽任何作者哦。"), ephemeral: true);
            return;
        }

        const int maxDescriptionLength = 4000; // Discord Embed 描述字段的最大长度略小于 4096，我们保守一点
        var lines = blockedIds.Select(id => $"- <@{id}>").ToList();

        var pages = new List<string>();
        var currentPageBuilder = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            // 检查加上新的一行后是否会超长
            if (currentPageBuilder.Length + line.Length + 1 > maxDescriptionLength)
            {
                pages.Add(currentPageBuilder.ToString());
                currentPageBuilder.Clear();
            }
            currentPageBuilder.AppendLine(line);
        }
        // 添加最后一页
        pages.Add(currentPageBuilder.ToString());

        // --- 发送分页消息 ---
        var firstPage = true;
        for (int i = 0; i < pages.Count; i++)
        {
            var embed = new EmbedBuilder()
                .WithTitle($"📝 你的屏蔽作者名单 (第 {i + 1}/{pages.Count} 页)")
                .WithDescription(pages[i])
                .WithColor(Color.Blue)
                .WithFooter($"共屏蔽了 {blockedIds.Count} 位作者")
                .Build();

            if (firstPage)
            {
                await FollowupAsync(embed: embed, ephemeral: true);
                firstPage = false;
            }
            else
            {
                // 对于后续页，我们需要使用 FollowupAsync 的另一个重载
                // 注意：因为原始交互是 ephemeral 的，所以后续的 Followup 也必须是 ephemeral
                await Context.Interaction.FollowupAsync(embed: embed, ephemeral: true);
            }
        }
    }
}