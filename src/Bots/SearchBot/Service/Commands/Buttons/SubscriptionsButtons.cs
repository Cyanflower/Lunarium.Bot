using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.Scheduler;
using SearchBot.Model;

namespace SearchBot.AppInteractionService;

public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// 确认订阅按钮的回调
    /// </summary>
    /// <param name="userId">发起命令的用户ID</param>
    [ComponentInteraction("add_ST_*")]
    public async Task AddSubscriptionConfirmButton(ulong userId)
    {
        if (Context.Interaction is not SocketMessageComponent interaction) return;

        if (interaction.Channel is IThreadChannel threadChannel)
        {
            // 执行订阅
            await _databaseService.InsertUserSubscriptionPostAsync(interaction.User, threadChannel);

            var embed = new EmbedBuilder()
                .WithTitle("✅ 订阅成功")
                .WithDescription($"成功订阅 **{threadChannel.Name}**，后续更新可以通过`/查看收藏`命令或搜索频道的按钮查看")
                .WithColor(Color.Green)
                .Build();

            await interaction.UpdateAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build(); // 清空按钮
            });
        }
        else
        {
            // 理论上不会发生
            await interaction.UpdateAsync(props =>
            {
                props.Content = "❌ 糟糕，出现了一个错误：此操作必须在帖子频道中进行。";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
    }

    /// <summary>
    /// 确认取消订阅按钮的回调
    /// </summary>
    /// <param name="userId">发起命令的用户ID</param>
    [ComponentInteraction("remove_ST_*")]
    public async Task RemoveSubscriptionConfirmButton(ulong userId)
    {
        if (Context.Interaction is not SocketMessageComponent interaction) return;

        if (interaction.Channel is IThreadChannel threadChannel)
        {
            // 执行取消订阅
            await _databaseService.RemoveSubscriptionAsync(userId, threadChannel.Id);

            var embed = new EmbedBuilder()
                .WithTitle("🗑️ 已取消订阅")
                .WithDescription($"你已成功取消对帖子 **{threadChannel.Name}** 的订阅。")
                .WithColor(Color.Default)
                .Build();

            await interaction.UpdateAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build(); // 清空按钮
            });
        }
        else
        {
            // 理论上不会发生
            await interaction.UpdateAsync(props =>
            {
                props.Content = "❌ 糟糕，出现了一个错误：此操作必须在帖子频道中进行。";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
    }

    /// <summary>
    /// 确认取消订阅按钮的回调 (通过帖子链接)
    /// </summary>
    /// <param name="userId">发起命令的用户ID</param>
    /// <param name="threadId">要取消订阅的帖子ID</param>
    [ComponentInteraction("remove_ST_id_*_*")]
    public async Task RemoveSubscriptionByIdConfirmButton(ulong userId, ulong threadId)
    {
        if (Context.Interaction is not SocketMessageComponent interaction) return;

        // 鉴权
        if (interaction.User.Id != userId)
        {
            await interaction.RespondAsync("这个是别人的按钮，不可以点哦！", ephemeral: true);
            return;
        }

        // 执行取消订阅
        await _databaseService.RemoveSubscriptionAsync(userId, threadId);

        // 为了显示帖子名称，尝试从原始消息的Embed中获取
        var originalEmbed = interaction.Message.Embeds.FirstOrDefault();
        string postInfo = $"ID为 {threadId} 的帖子"; // 备用文案
        if (originalEmbed != null && !string.IsNullOrEmpty(originalEmbed.Description))
        {
            // 尝试从 "您真的要从收藏中移除帖子 **帖子名称** 吗？" 中提取名称
            var match = Regex.Match(originalEmbed.Description, @"\*\*(.*?)\*\*");
            if (match.Success)
            {
                string postName = match.Groups[1].Value; 
                postInfo = $"帖子 **{postName}**";
            }
        }

        var embed = new EmbedBuilder()
            .WithTitle("🗑️ 已取消订阅")
            .WithDescription($"成功取消了对{postInfo}的订阅。")
            .WithColor(Color.Default)
            .Build();

        await interaction.UpdateAsync(props =>
        {
            props.Embed = embed;
            props.Components = new ComponentBuilder().Build(); // 清空按钮
        });
    }

    [ComponentInteraction("clear_push_list_*", true)]
    public async Task HandleClearPushListButton(ulong userId)
    {

        var affectedRows = await _databaseService.ClearPushListAsync(userId);

        var embed = new EmbedBuilder()
            .WithTitle("🗑️ 已清空推送消息列表")
            .WithDescription($"操作完成！已为您清空 {affectedRows} 条更新推送记录。")
            .WithColor(Color.Default)
            .Build();

        // 修改原始消息，并移除按钮
        await (Context.Interaction as SocketMessageComponent)!.UpdateAsync(props =>
        {
            props.Embed = embed;
            props.Components = new ComponentBuilder().Build();
        });
    }
}