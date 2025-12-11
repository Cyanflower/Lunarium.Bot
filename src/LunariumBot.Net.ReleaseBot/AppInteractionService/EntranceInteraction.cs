// .NET 核心库
using System.ComponentModel;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.ReleaseBot.AuthUtils;
using LunariumBot.Net.ReleaseBot.Models;
using Discord.WebSocket;
using LunariumBot.Net.ReleaseBot.InteractionsCache;
using LunariumBot.Net.ReleaseBot.Listener;

namespace LunariumBot.Net.ReleaseBot.AppInteractionService;

public class EntranceInteraction : InteractionModuleBase<SocketInteractionContext>
{
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly ComponentUtils _componentUtils;
    private readonly InteractionsCacheManager _interactionsCacheManager;
    private readonly ListenerService _listenerService;

    public EntranceInteraction(ILogger logger, AuthUtil authUtil, InteractionsCacheManager interactionsCacheManager, ListenerService listenerService)
    {
        _logger = logger.ForContext("EntranceInteraction");
        _authUtil = authUtil;
        _componentUtils = new(_logger);
        _interactionsCacheManager = interactionsCacheManager;
        _listenerService = listenerService;
    }

    // =============================================
    #region 业务方法
    // =============================================
    #endregion

    // =============================================
    #region  交互回调
    // =============================================
    [ComponentInteraction("ReleasePanel")]
    public async Task ReleasePanelAsync()
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [ReleasePanel:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var threadChannel = (IThreadChannel)interactionComponent.Channel;
            if (Context.User.Id != threadChannel.OwnerId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }

            var cache = _interactionsCacheManager.RegisterView<WorkData>(Context.User.Id, new WorkData());

            await RespondAsync(components: cache.Render(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ReleasePanel Exception");
        }
    }

    [ComponentInteraction("DisableAutoMessage")]
    public async Task DisableAutoMessageAsync()
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [DisableAutoMessage:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }
            var threadChannel = (IThreadChannel)interactionComponent.Channel;
            if (Context.User.Id != threadChannel.OwnerId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }

            _listenerService.DisableUser(Context.User.Id);

            var disableComponent = new ComponentBuilderV2().WithContainer(c => c
                .WithAccentColor(Color.Red)
                .WithTextDisplay("""
                    ### ✅ 已关闭自动提示
                    之后将不再在您的帖子内自动弹出本消息
                    如需改用 BOT 发布作品, 请使用命令:
                    > /发布作品
                    如需再次修改, 请使用命令:
                    > /启用自动提示
                    > /关闭自动提示
                    """, 1001)
                );
            await RespondAsync(components: disableComponent.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DisableAutoMessage Exception");
        }
    }

    [ComponentInteraction("DeleteSelf")]
    public async Task DeleteSelflAsync()
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [DeleteSelf:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }
            var threadChannel = (IThreadChannel)interactionComponent.Channel;
            if (Context.User.Id != threadChannel.OwnerId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }
            await interactionComponent.Message.DeleteAsync();
            var disableComponent = new ComponentBuilderV2().WithContainer(c => c
                .WithAccentColor(Color.Red)
                .WithTextDisplay("### ✅ 移除成功", 1000)
            );
            await RespondAsync(components: disableComponent.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DeleteSelflAsync Exception");
        }
    }
    #endregion

    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("启用自动提示", "启用发帖时自动弹出发布作品提示面板")]
    public async Task EnableAutoMessageAsync()
    {
        try
        {
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }

            if (Context.Channel is IThreadChannel threadChannel)
            {
                if (Context.User.Id != threadChannel.OwnerId)
                {
                    var component = new ComponentBuilderV2().WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                        .WithTextDisplay("该命令仅供本帖作者使用", 1001)
                        );
                    await RespondAsync(components: component.Build(), ephemeral: true);
                    return;
                }

                _listenerService.EnableUser(Context.User.Id);

                var enableComponent = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ✅ 启用成功", 1000)
                );
                await RespondAsync(components: enableComponent.Build(), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "EnableAutoMessageAsync Exception");
        }
    }
    
    [SlashCommand("关闭自动提示", "启用发帖时自动弹出发布作品提示面板")]
    public async Task DisableEnableAutoMessageAsync()
    {
        try
        {
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }

            if (Context.Channel is IThreadChannel threadChannel)
            {
                if (Context.User.Id != threadChannel.OwnerId)
                {
                    var component = new ComponentBuilderV2().WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                        .WithTextDisplay("该命令仅供本帖作者使用", 1001)
                        );
                    await RespondAsync(components: component.Build(), ephemeral: true);
                    return;
                }

                _listenerService.DisableUser(Context.User.Id);

                var enableComponent = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ✅ 关闭成功", 1000)
                );
                await RespondAsync(components: enableComponent.Build(), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DisableEnableAutoMessageAsync Exception");
        }
    }
    #endregion
}
