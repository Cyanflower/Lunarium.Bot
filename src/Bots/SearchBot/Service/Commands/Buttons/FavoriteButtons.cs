using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.Scheduler;
using SearchBot.Model;
using SearchBot.Utils;

namespace SearchBot.AppInteractionService;

public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// 新收藏夹确认按钮回调
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="favorite"></param>
    /// <returns></returns>
    [ComponentInteraction("create_F_*_*")]
    public async Task CreateFavoriteConfirmButton(ulong userId, string favorite)
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                // 从交互发生的频道获取帖子信息
                var channel = interaction.Channel as IThreadChannel;

                if (channel != null)
                {
                    // 添加收藏
                    await _databaseService.InsertUserFavoritePostAsync(userId, channel, favorite);

                    await _databaseService.AddUserFavoriteTypeAsync(userId, favorite);

                    var embed = new EmbedBuilder()
                        .WithTitle("✅ 创建并收藏成功")
                        .WithDescription($"成功创建收藏夹 **{favorite}** 并将 **{interaction.Channel.Name}** 添加进去啦！")
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
                    // 理论上，因为命令只能在帖子里用，所以这里不会执行
                    var embed = new EmbedBuilder()
                        .WithTitle("❌ 收藏失败")
                        .WithDescription("错误：这个操作必须在一个帖子频道中进行确认。")
                        .WithColor(Color.Red)
                        .Build();
                    await interaction.UpdateAsync(props =>
                    {
                        props.Embed = embed;
                        props.Components = new ComponentBuilder().Build();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("CreateFavoriteConfirmButton {ex}", ex);
        }
    }
    /// <summary>
    /// 添加到现有收藏夹的确认按钮回调
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="favorite"></param>
    /// <returns></returns>
    [ComponentInteraction("add_F_*_*")]
    public async Task AddFavoriteConfirmButton(ulong userId, string favorite)
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                _logger.LogDebug("1");
                // 关键：从交互发生的频道获取帖子信息！
                var channel = interaction.Channel as IThreadChannel;

                if (channel != null)
                {
                    _logger.LogDebug("2");
                    string? favoriteCategory = favorite == "null" ? null : favorite;
                    // 添加收藏
                    _logger.LogDebug("3");
                    await _databaseService.InsertUserFavoritePostAsync(userId, channel, favoriteCategory);
                    _logger.LogDebug("4");
                    var embed = new EmbedBuilder()
                        .WithTitle("✅ 收藏成功")
                        .WithDescription($"成功将 **{interaction.Channel.Name}** 添加到收藏夹 **{(favorite == "null" ? "未分类" : favorite)}** 中！")
                        .WithColor(Color.Green)
                        .Build();
                    _logger.LogDebug("5");
                    await interaction.UpdateAsync(props =>
                    {
                        props.Embed = embed;
                        props.Components = new ComponentBuilder().Build(); // 清空按钮
                    });
                    _logger.LogDebug("6");
                }
                else
                {
                    _logger.LogDebug("7");
                    // 理论上，因为命令只能在帖子里用，所以这里不会执行
                    var embed = new EmbedBuilder()
                        .WithTitle("❌ 收藏失败")
                        .WithDescription("错误：这个操作必须在一个帖子频道中进行确认。")
                        .WithColor(Color.Red)
                        .Build();
                    await interaction.UpdateAsync(props =>
                    {
                        props.Embed = embed;
                        props.Components = new ComponentBuilder().Build();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("AddFavoriteConfirmButton {ex}", ex);
        }
    }
    /// <summary>
    /// 取消收藏的按钮回调
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    [ComponentInteraction("remove_F_*")]
    public async Task RemoveFavoriteConfirmButton(ulong userId)
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                var channel = interaction.Channel as IThreadChannel;

                if (channel != null)
                {
                    // 执行取消收藏的数据库操作
                    await _databaseService.RemoveFavoriteAsync(userId, channel.Id);

                    var embed = new EmbedBuilder()
                        .WithTitle("🗑️ 已取消收藏")
                        .WithDescription($"成功将帖子 **{channel.Name}** 从您的收藏中移除。")
                        .WithColor(Color.Default) // 使用默认颜色或灰色
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
                    var embed = new EmbedBuilder()
                        .WithTitle("❌ 操作失败")
                        .WithDescription("错误：此操作必须在帖子频道中进行确认。")
                        .WithColor(Color.Red)
                        .Build();
                    await interaction.UpdateAsync(props =>
                    {
                        props.Embed = embed;
                        props.Components = new ComponentBuilder().Build();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("RemoveFavoriteConfirmButton {ex}", ex);
        }
    }
    /// <summary>
    /// 取消收藏的按钮回调 (通过帖子链接)
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="threadId"></param>
    /// <returns></returns>
    [ComponentInteraction("remove_F_id_*_*")]
    public async Task RemoveFavoriteByIdConfirmButton(ulong userId, ulong threadId)
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                // 直接使用从 customId 解析出的 threadId 进行操作
                await _databaseService.RemoveFavoriteAsync(userId, threadId);

                // 为了显示帖子名称，尝试从原始消息的Embed中获取
                var originalEmbed = interaction.Message.Embeds.FirstOrDefault();
                string postInfo = $"ID为 {threadId} 的帖子"; // 备用描述
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
                    .WithTitle("🗑️ 已取消收藏")
                    .WithDescription($"成功将{postInfo}从您的收藏中移除。")
                    .WithColor(Color.Default)
                    .Build();

                await interaction.UpdateAsync(props =>
                {
                    props.Embed = embed;
                    props.Components = new ComponentBuilder().Build();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("RemoveFavoriteByIdConfirmButton {ex}", ex);
        }
    }
    /// <summary>
    /// 删除收藏夹的确认按钮回调
    /// </summary>
    [ComponentInteraction("delete_F_cat_*_*_*", true)]
    public async Task HandleDeleteFavoriteCategory(ulong userId, string action, string categoryName)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interaction) return;
            if (interaction.User.Id != userId)
            {
                await interaction.RespondAsync("这是别人的按钮哦！", ephemeral: true);
                return;
            }
    
            bool deletePosts = action == "remove";
            await _databaseService.RemoveFavoriteCategoryAsync(userId, categoryName, deletePosts);
    
            var embed = EmbedUtils.SuccessEmbed($"操作成功！收藏夹 **{categoryName}** 已经被删除了哦。");
            await interaction.UpdateAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("HandleDeleteFavoriteCategory {ex}", ex);
        }
    }

    /// <summary>
    /// 重命名收藏夹的确认按钮回调
    /// </summary>
    [ComponentInteraction("rename_F_cat_*_*_*", true)]
    public async Task HandleRenameFavoriteCategory(ulong userId, string oldName, string newName)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interaction) return;
            if (interaction.User.Id != userId)
            {
                await interaction.RespondAsync("这是别人的按钮哦！", ephemeral: true);
                return;
            }
    
            await _databaseService.RenameFavoriteCategoryAsync(userId, oldName, newName);
    
            var embed = EmbedUtils.SuccessEmbed($"操作成功！收藏夹 **{oldName}** 已经成功改名为 **{newName}**。");
            await interaction.UpdateAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("HandleRenameFavoriteCategory {ex}", ex);
        }
    }
}