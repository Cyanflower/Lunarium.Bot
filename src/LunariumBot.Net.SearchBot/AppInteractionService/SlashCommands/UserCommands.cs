// NuGet 包依赖
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public partial class UserCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtils _authUtils;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;

    public UserCommands(
        ILogger logger,
        AuthUtils authUtils,
        ConfigLoader configLoader
    )
    {
        _logger = logger.ForContext("UserCommands");
        _authUtils = authUtils;
        _configLoader = configLoader;
    }


    [SlashCommand("收藏订阅面板", "在本频道内创建一个用于快捷搜索与查看收藏和订阅的面板")]
    public async Task UserCreateManagerEmbed()
    {
        await DeferAsync(ephemeral: true);
        // 鉴权: 如果不是admin, 则返回
        if (!await _authUtils.InGuild("收藏订阅面板", Context)) return;

        try
        {
            var embed = DisplayEmbedBuilder.DirectSearchEmbed();

            var component = AppInteractionUitls.DirectSearchMenuComponent(_configLoader);
    
            await FollowupAsync(embed: embed, components: component, ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("创建管理面板失败",ex.ToString()), ephemeral: true);
            _logger.Error("创建收藏订阅面板失败", ex);
        }
    }

}