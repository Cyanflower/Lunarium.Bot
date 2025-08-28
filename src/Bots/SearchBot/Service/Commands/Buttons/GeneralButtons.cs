using Microsoft.Extensions.Logging;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.Scheduler;
using SearchBot.Model;

namespace SearchBot.AppInteractionService;

// 本项目 customId 命名约定:
// {操作?}_{功能类别?}_*_*...
// 操作类型 + 功能类别 + 通配符传参
// 如: add_F_{user id}_{target favorite}

// 功能列表:
// F: 收藏夹功能(favorite)
// S: 搜索功能(search)
// P: 分页功能(paginator)
// ST: 订阅帖子功能(sub thread)
// SA: 订阅作者功能(sub author)

// === --- === 交互按钮 === --- ===
public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger<Buttons> _logger;
    private readonly DatabaseService _databaseService;
    private readonly PaginatorManager _paginatorManager;


    public Buttons(ILogger<Buttons> logger, DatabaseService databaseService, PaginatorManager paginatorManager)
    {
        _logger = logger;
        _databaseService = databaseService;
        _paginatorManager = paginatorManager;
    }

    /// <summary>
    /// 通用取消按钮回调
    /// </summary>
    /// <returns></returns>
    [ComponentInteraction("general_cancel")]
    public async Task GeneralCancelButton()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError("GeneralCancelButton {ex}", ex);
        }
    }
}
