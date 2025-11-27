// .NET 核心库
using System.Text.Json;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Extensions;
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Scheduler;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Paginator;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public partial class GeneralInteraction : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly DatabaseService _databaseService;
    private readonly PaginatorManager _paginatorManager;
    private readonly UserCooldown _userCooldown;
    private readonly ConfigLoader _configLoader;
    private readonly MessageComponent? _directSearchMenuComponent;
    private readonly Embed _directSearchMenuEmbed;

    public GeneralInteraction(
        ILogger logger,
        DatabaseService databaseService,
        PaginatorManager paginatorManager,
        UserCooldown userCooldown,
        ConfigLoader configLoader
    )
    {
        _userCooldown = userCooldown;
        _logger = logger.ForContext("GeneralInteraction");
        _databaseService = databaseService;
        _paginatorManager = paginatorManager;
        _configLoader = configLoader;

        _directSearchMenuEmbed = DisplayEmbedBuilder.DirectSearchEmbed();

        _directSearchMenuComponent = AppInteractionUitls.DirectSearchMenuComponent(_configLoader);
    }

    /// <summary>
    /// 通用取消按钮回调
    /// </summary>
    /// <returns></returns>
    [ComponentInteraction("general_cancel")]
    public async Task GeneralCancelAsync()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {

                await DeferAsync(ephemeral: true);
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
            _logger.Error(ex, "GeneralCancelButton {ex}");
        }
    }

    /// <summary>
    /// 通用取消按钮回调
    /// </summary>
    /// <returns></returns>
    [ComponentInteraction("GeneralCancelV2")]
    public async Task HandleGeneralCancelV2Async()
    {
        // 检查 Context.Interaction 是否是 SocketMessageComponent 类型
        // 并将其转换为一个新变量 interaction
        try
        {
            if (Context.Interaction is SocketMessageComponent interaction)
            {
                await DeferAsync(ephemeral: true);
                var textPart = interaction.Message.Components.FindComponentById<TextDisplayComponent>(1000);

                var componentV2 = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay(textPart.Content)
                        .WithSeparator()
                        .WithTextDisplay("## 操作已取消")
                    )
                    .Build();

                await interaction.UpdateAsync(props =>
                {
                    props.Components = componentV2;
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
            _logger.Error(ex, "HandleGeneralCancelV2Button Exception");
        }
    }

    [ComponentInteraction("pg:*:*")]
    public async Task HandlePaginationActionAsync(string action, string id)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {

                string errorId = CorrelationIdGenerator.Generate();
                var interactionTypeComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 交互失败失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                await RespondAsync(components: interactionTypeComponent, ephemeral: true);
                _logger.Warning(" 错误ID: {errorId} | 交互回调: [] Context.Interaction is Not SocketMessageComponent", errorId);
                return;
            }

            var view = _paginatorManager.GetView(id);

            if (view is null)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("## 该交互已过期\n请使用命令发起新的交互")
                    )
                    .Build();
                await FollowupAsync(components: component, ephemeral: true);
                return;
            }

            switch (action)
            {
                case "first":
                    view.FirstPage();
                    await interactionComponent.UpdateAsync(msg => msg.Components = view.Render());
                    return;
                case "prev":
                    view.PrevPage();
                    await interactionComponent.UpdateAsync(msg => msg.Components = view.Render());
                    return;
                case "show_page_modal":
                    // 弹出模态框
                    var mb = new ModalBuilder()
                        .WithTitle("跳至页面")
                        .WithCustomId($"pg_modal:{id}")
                        .AddTextInput("目标页面", "target_page", placeholder: "请输入要跳转到的页数", maxLength: 4, minLength: 1);
                    await RespondWithModalAsync(mb.Build());
                    return;
                case "next":
                    view.NextPage();
                    await interactionComponent.UpdateAsync(msg => msg.Components = view.Render());
                    return;
                case "last":
                    view.LastPage();
                    await interactionComponent.UpdateAsync(msg => msg.Components = view.Render());
                    return;
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Handle Exception");
        }
    }

    [ComponentInteraction("pg_modal:*")]
    public async Task Handle()
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {

                string errorId = CorrelationIdGenerator.Generate();
                var interactionTypeComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## 交互失败失败", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
                await RespondAsync(components: interactionTypeComponent, ephemeral: true);
                _logger.Warning(" 错误ID: {errorId} | 交互回调: [] Context.Interaction is Not SocketMessageComponent", errorId);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Handle Exception");
        }
    }
}
