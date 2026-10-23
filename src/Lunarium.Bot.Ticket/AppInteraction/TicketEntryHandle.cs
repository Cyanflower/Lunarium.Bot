using Discord.Interactions;
using Discord;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Bot.Ticket.InteractionSession;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Component;
using Lunarium.Common.UID;

namespace Lunarium.Bot.Ticket.AppInteraction;

public sealed class TicketEntryHandle : InteractionModuleBase<SocketInteractionContext>
{
    private enum ActionType
    {
        Complaint,
        Contact,
        Help,
        ApplyForStaff
    }
    private readonly ILogger _logger;
    private readonly ConfigLoader<TicketConfig> _configLoader;

    public TicketEntryHandle(ILogger logger, ConfigLoader<TicketConfig> configLoader)
    {
        _logger = logger.ForContext("AppInteraction.TicketEntryHandle");
        _configLoader = configLoader;
    }

    [ComponentInteraction(ComponentTemplate.DashboardMessage.ComplaintCustomId)]
    public async Task HandleTicketEntry()
    {
        await HandleEntry(ActionType.Complaint, ComponentTemplate.DashboardMessage.ComplaintCustomId);
    }

    [ComponentInteraction(ComponentTemplate.DashboardMessage.ContactStaffCustomId)]
    public async Task HandleContactEntry()
    {
        await HandleEntry(ActionType.Contact, ComponentTemplate.DashboardMessage.ContactStaffCustomId);
    }

    [ComponentInteraction(ComponentTemplate.DashboardMessage.HelpCustomId)]
    public async Task HandleHelpEntry()
    {
        await HandleEntry(ActionType.Help, ComponentTemplate.DashboardMessage.HelpCustomId);
    }

    [ComponentInteraction(ComponentTemplate.DashboardMessage.ApplyForStaffCustomId)]
    public async Task HandleAllpyForStaffEntry()
    {
        await HandleEntry(ActionType.ApplyForStaff, ComponentTemplate.DashboardMessage.ApplyForStaffCustomId);
    }
    private async Task HandleEntry(ActionType actionType, string actionCustomId)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            track.Info("AppInteraction: {Interaction} 收到入口交互请求: ActionType={ActionType}, GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}",
                actionCustomId, actionType, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id);

            if (Context.Interaction is not SocketMessageComponent)
            {
                track.Warning("AppInteraction: {Interaction} 当前交互不是消息组件交互: InteractionType={InteractionType}, UserId={UserId}",
                    actionCustomId, Context.Interaction.Type, Context.User.Id);
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是消息组件交互"), ephemeral: true);
                return;
            }

            var guild = Context.Guild;

            if (guild is null)
            {
                track.Warning("AppInteraction: {Interaction} 解析服务器信息失败: UserId={UserId}", actionCustomId, Context.User.Id);
                await RespondAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"), ephemeral: true);
                return;
            }

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
            {
                track.Warning("AppInteraction: {Interaction} 当前配置中并无本服务器配置: GuildId={GuildId}, UserId={UserId}",
                    actionCustomId, guild.Id, Context.User.Id);
                await RespondAsync(components: MessagePrefab.ConfigFail("当前配置中并无本服务器配置"), ephemeral: true);
                return;
            }

            var validationResult = await RuleEngine.ValidateAsync(
                Context,
                new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
                new BaseRuleSet.RequireRole(_configLoader.Value.DeployGuildConfig[guild.Id].AllowTicketRoleIds.ToHashSet()));

            if (validationResult is not null)
            {
                track.Warning("AppInteraction: {Interaction} 权限校验未通过: GuildId={GuildId}, UserId={UserId}", actionCustomId, guild.Id, Context.User.Id);
                await RespondAsync(components: validationResult, ephemeral: true);
                return;
            }

            track.Info("AppInteraction: {Interaction} 权限校验通过: GuildId={GuildId}, UserId={UserId}", actionCustomId, guild.Id, Context.User.Id);

            switch (actionType)
            {
                case ActionType.Complaint:
                    track.Info("AppInteraction: {Interaction} 打开投诉模态窗: GuildId={GuildId}, UserId={UserId}", actionCustomId, guild.Id, Context.User.Id);
                    await RespondWithModalAsync(ComponentTemplate.ComplaintModal.ToModal());
                    return;
                case ActionType.Contact:
                    track.Info("AppInteraction: {Interaction} 打开联系工作人员模态窗: GuildId={GuildId}, UserId={UserId}", actionCustomId, guild.Id, Context.User.Id);
                    await RespondWithModalAsync(ComponentTemplate.ContactModal.ToModal());
                    return;
                case ActionType.Help:
                    track.Info("AppInteraction: {Interaction} 打开帮助模态窗: GuildId={GuildId}, UserId={UserId}", actionCustomId, guild.Id, Context.User.Id);
                    await RespondWithModalAsync(ComponentTemplate.HelpModal.ToModal());
                    return;
                case ActionType.ApplyForStaff:
                    track.Info("AppInteraction: {Interaction} 打开申请管理模态窗: GuildId={GuildId}, UserId={UserId}", actionCustomId, guild.Id, Context.User.Id);
                    await RespondWithModalAsync(ComponentTemplate.ApplyForStaffModal.ToModal());
                    return;
                default:
                    track.Warning("AppInteraction: {Interaction} 收到未处理的 ActionType: ActionType={ActionType}, GuildId={GuildId}, UserId={UserId}",
                        actionCustomId, actionType, guild.Id, Context.User.Id);
                    return;
            }
            
        }
        catch (Exception ex)
        {
            track.Error(ex, "AppInteraction: {Interaction} 处理入口交互时发生异常: ActionType={ActionType}, GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}",
                actionCustomId, actionType, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id);
        }
    }
}