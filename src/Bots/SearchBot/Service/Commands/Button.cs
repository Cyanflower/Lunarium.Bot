using Microsoft.Extensions.Logging;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using SearchBot.Database;

namespace SearchBot.AppInteractionService;
// 通用按钮
public class GeneralButton : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("general_cancel")]
    public async Task GeneralCancelButton()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        if (Context.Interaction is SocketMessageComponent interaction)
        {
            var oldEmbed = interaction.Message.Embeds.FirstOrDefault();
            var cancelledEmbed = new EmbedBuilder();

            if (oldEmbed != null)
            {
                cancelledEmbed
                    .WithTitle("操作已取消")
                    .WithDescription(oldEmbed.Description)
                    .WithColor(Color.Red);
            }
            else
            {
                cancelledEmbed
                    .WithTitle("操作已取消")
                    .WithColor(Color.Red);
            }
            var embed = cancelledEmbed.Build();

            await interaction.UpdateAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build();
            });
        }
        else
        {
            // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
            // 可以在这里处理错误，虽然理论上不应该发生。
            await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
        }
    }
}


//新收藏夹确认按钮
public class FavoriteButton : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _databaseService;

    public FavoriteButton(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    [ComponentInteraction("create_*_*")]
    public async Task GeneralCancelButton(ulong userId, string favorite)
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        if (Context.Interaction is SocketMessageComponent interaction)
        {
            // 获取原始消息的 Embeds 集合中的第一个
            var oldEmbed = interaction.Message.Embeds.FirstOrDefault();
            
            // 获取交互所在的 Channel，并将其转换为 IThreadChannel
            var channel = interaction.Channel as IThreadChannel;

            if (channel != null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("收藏成功")
                    .WithDescription($"成功将 {interaction.Channel.Name} 添加到收藏夹 {favorite} 中")
                    .WithColor(Color.Green)
                    .Build();
                // 添加收藏
                await _databaseService.InsertUserFavoritePostAsync(userId, channel, favorite);
                // 更新原始消息的嵌入式信息
                await interaction.UpdateAsync(props =>
                {
                    props.Embed = embed;
                    props.Components = new ComponentBuilder().Build();
                });
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithTitle("收藏失败")
                    .WithDescription("未知错误\n原因:\n```threadChannel.Id is null\n```")
                    .WithColor(Color.Red)
                    .Build();
                // 更新原始消息的嵌入式信息
                await interaction.UpdateAsync(props =>
                {
                    props.Embed = embed;
                    props.Components = new ComponentBuilder().Build();
                });
            }
        }
        else
        {
            // 如果因为某些奇怪的原因，这个交互不是消息组件交互，
            // 可以在这里处理错误，虽然理论上不应该发生。
            await RespondAsync("这是一个无效的交互类型。", ephemeral: true);
        }
    }
}
