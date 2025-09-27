// .NET 核心库
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;
// 项目内命名空间
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
{

    /// <summary>
    /// 处理收藏下拉菜单的选择
    /// </summary>
    [ComponentInteraction("favorite_select_*", true)]
    public async Task HandleFavoriteSelect(ulong userId, string[] selectedValues)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interaction) return;
            var selectedValue = selectedValues.First();
            var threadChannel = interaction.Channel as IThreadChannel;
            if (threadChannel == null)
            {
                await RespondAsync("错误：此交互必须在帖子频道中进行。", ephemeral: true);
                return;
            }
    
            switch (selectedValue)
            {
                // 用户选择 "创建新收藏夹"
                case "__CREATE_NEW__":
                    // 弹出模态框让用户输入新名称
                    await interaction.RespondWithModalAsync<CreateFavoriteModal>($"favorite_create_modal_{userId}");
                    break;
    
                // 用户选择 "未分类"
                case "__UNCLASSIFIED__":
                    await DeferAsync(ephemeral: true);
                    await _databaseService.InsertUserFavoritePostAsync(userId, threadChannel, null);
                    var unclassifiedEmbed = EmbedUtils.SuccessEmbed($"收藏成功！已将帖子 **{threadChannel.Name}** 添加到 **未分类**。");
                    await interaction.ModifyOriginalResponseAsync(props => {
                        props.Embed = unclassifiedEmbed;
                        props.Components = new ComponentBuilder().Build(); // 清空组件
                    });
                    break;
                
                // 用户选择了一个已存在的收藏夹
                default:
                    await DeferAsync(ephemeral: true);
                    await _databaseService.InsertUserFavoritePostAsync(userId, threadChannel, selectedValue);
                    var existingEmbed = EmbedUtils.SuccessEmbed($"收藏成功！已将帖子 **{threadChannel.Name}** 添加到收藏夹 **{selectedValue}**。");
                    await interaction.ModifyOriginalResponseAsync(props => {
                        props.Embed = existingEmbed;
                        props.Components = new ComponentBuilder().Build();
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("HandleFavoriteSelect", ex);
        }
    }

    /// <summary>
    /// 处理创建新收藏夹的模态框提交
    /// </summary>
    [ModalInteraction("favorite_create_modal_*", true)]
    public async Task HandleFavoriteCreateModal(ulong userId, CreateFavoriteModal modal)
    {
        try
        {
            if (Context.Interaction is not SocketModal modalInteraction) return;
            var newCategoryName = modal.CategoryName.Trim();
    
            // --- 参数校验 ---
            if (string.IsNullOrWhiteSpace(newCategoryName))
            {
                await modalInteraction.RespondAsync("收藏夹名称不能为空哦！", ephemeral: true);
                return;
            }
            
            var favoriteTypes = await _databaseService.GetUserFavoriteTypeAsync(userId);
            if (favoriteTypes.Contains(newCategoryName))
            {
                await modalInteraction.RespondAsync($"你已经有一个叫 **{newCategoryName}** 的收藏夹啦，换个名字吧！", ephemeral: true);
                return;
            }
    
            // 关键一步：从模态框交互中获取原始消息所在的频道
            if (modalInteraction.Message.Channel is not IThreadChannel threadChannel)
            {
                 await modalInteraction.UpdateAsync(props => {
                    props.Embed = EmbedUtils.FailedEmbed("发生错误", "无法定位到原始帖子，请重试。");
                    props.Components = new ComponentBuilder().Build();
                });
                return;
            }

            await DeferAsync();

            // 执行数据库操作
            await _databaseService.InsertUserFavoritePostAsync(userId, threadChannel, newCategoryName);
            await _databaseService.AddUserFavoriteTypeAsync(userId, newCategoryName); // 确保新类别被记录
    
            var embed = EmbedUtils.SuccessEmbed($"创建并收藏成功！\n已将帖子 **{threadChannel.Name}** 添加到新收藏夹 **{newCategoryName}**。");
    
            // 用 UpdateAsync 来响应模态框，这样会直接编辑原始的带有下拉菜单的消息
            await modalInteraction.ModifyOriginalResponseAsync(props => {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            _logger.Error("HandleFavoriteCreateModal", ex);
        }
    }

    /// <summary>
    /// 新收藏夹确认按钮回调
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="favorite"></param>
    /// <returns></returns>
    // [ComponentInteraction("create_F_*_*")]
    // public async Task CreateFavoriteConfirmButton(ulong userId, string favorite)
    // {
    //     try
    //     {
    //         if (Context.Interaction is SocketMessageComponent interaction)
    //         {
    //             await DeferAsync(ephemeral: true);
    //             // 从交互发生的频道获取帖子信息
    //             var channel = interaction.Channel as IThreadChannel;

    //             if (channel != null)
    //             {
    //                 // 添加收藏
    //                 await _databaseService.InsertUserFavoritePostAsync(userId, channel, favorite);

    //                 await _databaseService.AddUserFavoriteTypeAsync(userId, favorite);

    //                 var embed = new EmbedBuilder()
    //                     .WithTitle("✅ 创建并收藏成功")
    //                     .WithDescription($"成功创建收藏夹 **{favorite}** 并将 **{interaction.Channel.Name}** 添加进去啦！")
    //                     .WithColor(Color.Green)
    //                     .Build();

    //                 await interaction.UpdateAsync(props =>
    //                 {
    //                     props.Embed = embed;
    //                     props.Components = new ComponentBuilder().Build(); // 清空按钮
    //                 });
    //             }
    //             else
    //             {
    //                 // 理论上，因为命令只能在帖子里用，所以这里不会执行
    //                 var embed = new EmbedBuilder()
    //                     .WithTitle("❌ 收藏失败")
    //                     .WithDescription("错误：这个操作必须在一个帖子频道中进行确认。")
    //                     .WithColor(Color.Red)
    //                     .Build();
    //                 await interaction.UpdateAsync(props =>
    //                 {
    //                     props.Embed = embed;
    //                     props.Components = new ComponentBuilder().Build();
    //                 });
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error("CreateFavoriteConfirmButton", ex);
    //     }
    // }
    /// <summary>
    /// 添加到现有收藏夹的确认按钮回调
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="favorite"></param>
    /// <returns></returns>
    // [ComponentInteraction("add_F_*_*")]
    // public async Task AddFavoriteConfirmButton(ulong userId, string favorite)
    // {
    //     try
    //     {
    //         if (Context.Interaction is SocketMessageComponent interaction)
    //         {
    //             await DeferAsync(ephemeral: true);
    //             _logger.Debug("1");
    //             // 关键：从交互发生的频道获取帖子信息！
    //             var channel = interaction.Channel as IThreadChannel;

    //             if (channel != null)
    //             {
    //                 _logger.Debug("2");
    //                 string? favoriteCategory = favorite == "null" ? null : favorite;
    //                 // 添加收藏
    //                 _logger.Debug("3");
    //                 await _databaseService.InsertUserFavoritePostAsync(userId, channel, favoriteCategory);
    //                 _logger.Debug("4");
    //                 var embed = new EmbedBuilder()
    //                     .WithTitle("✅ 收藏成功")
    //                     .WithDescription($"成功将 **{interaction.Channel.Name}** 添加到收藏夹 **{(favorite == "null" ? "未分类" : favorite)}** 中！")
    //                     .WithColor(Color.Green)
    //                     .Build();
    //                 _logger.Debug("5");
    //                 await interaction.UpdateAsync(props =>
    //                 {
    //                     props.Embed = embed;
    //                     props.Components = new ComponentBuilder().Build(); // 清空按钮
    //                 });
    //                 _logger.Debug("6");
    //             }
    //             else
    //             {
    //                 _logger.Debug("7");
    //                 // 理论上，因为命令只能在帖子里用，所以这里不会执行
    //                 var embed = new EmbedBuilder()
    //                     .WithTitle("❌ 收藏失败")
    //                     .WithDescription("错误：这个操作必须在一个帖子频道中进行确认。")
    //                     .WithColor(Color.Red)
    //                     .Build();
    //                 await interaction.UpdateAsync(props =>
    //                 {
    //                     props.Embed = embed;
    //                     props.Components = new ComponentBuilder().Build();
    //                 });
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error("AddFavoriteConfirmButton", ex);
    //     }
    // }
    /// <summary>
    /// 取消收藏的按钮回调
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    /// 
    [ComponentInteraction("remove_F_*", true)]
    public async Task RemoveFavoriteConfirmButton(ulong userId)
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);
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

                    await interaction.ModifyOriginalResponseAsync(props =>
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
                    await interaction.ModifyOriginalResponseAsync(props =>
                    {
                        props.Embed = embed;
                        props.Components = new ComponentBuilder().Build();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("RemoveFavoriteConfirmButton {ex}", ex);
        }
    }
    /// <summary>
    /// 取消收藏的按钮回调 (通过帖子链接)
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="threadId"></param>
    /// <returns></returns>
    [ComponentInteraction("remove_F_id_*_*", true)]
    public async Task RemoveFavoriteByIdConfirmButton(ulong userId, ulong threadId)
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);
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

                await interaction.ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = embed;
                    props.Components = new ComponentBuilder().Build();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error("RemoveFavoriteByIdConfirmButton {ex}", ex);
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
            await DeferAsync(ephemeral: true);

            bool deletePosts = action == "remove";
            await _databaseService.RemoveFavoriteCategoryAsync(userId, categoryName, deletePosts);

            var embed = EmbedUtils.SuccessEmbed($"操作成功！收藏夹 **{categoryName}** 已经被删除了哦。");
            await interaction.ModifyOriginalResponseAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            _logger.Error("HandleDeleteFavoriteCategory {ex}", ex);
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
            await DeferAsync(ephemeral: true);

            await _databaseService.RenameFavoriteCategoryAsync(userId, oldName, newName);

            var embed = EmbedUtils.SuccessEmbed($"操作成功！收藏夹 **{oldName}** 已经成功改名为 **{newName}**。");
            await interaction.ModifyOriginalResponseAsync(props =>
            {
                props.Embed = embed;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            _logger.Error("HandleRenameFavoriteCategory {ex}", ex);
        }
    }
}

public class CreateFavoriteModal : IModal
{
    public string Title => "创建新收藏夹";

    [InputLabel("收藏夹名称")]
    [ModalTextInput("fav_name_input", TextInputStyle.Short, "请输入新收藏夹的名称", maxLength: 50)]
    public string CategoryName { get; set; } = null!;
}