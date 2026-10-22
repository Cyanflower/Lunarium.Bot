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
using Lunarium.Bot.Ticket.TicketService;

namespace Lunarium.Bot.Ticket.AppInteraction;

public sealed class TicketModalHandle : InteractionModuleBase<SocketInteractionContext>
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
    private readonly TicketManager _ticketManager;
    private readonly TicketCardService _ticketCardService;

    public TicketModalHandle(
        ILogger logger,
        ConfigLoader<TicketConfig> configLoader,
        TicketManager ticketManager,
        TicketCardService ticketCardService)
    {
        _logger = logger.ForContext("TicketModalHandle");
        _configLoader = configLoader;
        _ticketManager = ticketManager;
        _ticketCardService = ticketCardService;
    }

    [ModalInteraction(ComponentTemplate.ComplaintModal.ModalCustomId)]
    public async Task HandleComplaintModal(ComponentTemplate.ComplaintModal.InteractionModal modal)
    {
        await HandleModal(ActionType.Complaint, ComponentTemplate.ComplaintModal.ModalCustomId, modal);
    }

    [ModalInteraction(ComponentTemplate.ContactModal.ModalCustomId)]
    public async Task HandleContactModal(ComponentTemplate.ContactModal.InteractionModal modal)
    {
        await HandleModal(ActionType.Contact, ComponentTemplate.ContactModal.ModalCustomId, modal);
    }

    [ModalInteraction(ComponentTemplate.HelpModal.ModalCustomId)]
    public async Task HandleHelpModal(ComponentTemplate.HelpModal.InteractionModal modal)
    {
        await HandleModal(ActionType.Help, ComponentTemplate.HelpModal.ModalCustomId, modal);
    }

    [ModalInteraction(ComponentTemplate.ApplyForStaffModal.ModalCustomId)]
    public async Task HandleApplyForStaffModal(ComponentTemplate.ApplyForStaffModal.InteractionModal modal)
    {
        await HandleModal(ActionType.ApplyForStaff, ComponentTemplate.ApplyForStaffModal.ModalCustomId, modal);
    }

    private async Task HandleModal(ActionType actionType, string actionCustomId, IModal imodal)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (Context.Interaction is not SocketModal)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
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

            // 先点击按钮才能有模态框, 鉴权在按钮已经做过了
            // Defer
            await DeferAsync(ephemeral: true);

            // 四类模态窗只有参数不同，统一解析后走同一条建单路径
            TicketType ticketType;
            TicketCategory ticketCategory;
            string content;
            string? secondaryContent = null;
            int autoApproveHours;

            switch (actionType)
            {
                case ActionType.Complaint:
                    if (imodal is not ComponentTemplate.ComplaintModal.InteractionModal complaint)
                    {
                        await FollowupAsync(components: MessagePrefab.FunctionFail("模态窗提交类型转换失败"));
                        return;
                    }
                    ticketType = TicketType.Complaint;
                    ticketCategory = complaint.Type;
                    content = complaint.Detail;
                    autoApproveHours = config.AutoApproveHours;
                    break;

                case ActionType.Contact:
                    if (imodal is not ComponentTemplate.ContactModal.InteractionModal contact)
                    {
                        await FollowupAsync(components: MessagePrefab.FunctionFail("模态窗提交类型转换失败"));
                        return;
                    }
                    ticketType = TicketType.Contact;
                    ticketCategory = contact.Type;
                    content = contact.Detail;
                    autoApproveHours = config.AutoApproveHours;
                    break;

                case ActionType.Help:
                    if (imodal is not ComponentTemplate.HelpModal.InteractionModal help)
                    {
                        await FollowupAsync(components: MessagePrefab.FunctionFail("模态窗提交类型转换失败"));
                        return;
                    }
                    ticketType = TicketType.Help;
                    ticketCategory = help.Type;
                    content = help.Detail;
                    secondaryContent = help.Target;
                    autoApproveHours = config.AutoApproveHours;
                    break;

                case ActionType.ApplyForStaff:
                    if (imodal is not ComponentTemplate.ApplyForStaffModal.InteractionModal apply)
                    {
                        await FollowupAsync(components: MessagePrefab.FunctionFail("模态窗提交类型转换失败"));
                        return;
                    }
                    ticketType = TicketType.ApplyForStaff;
                    ticketCategory = apply.Type;
                    content = apply.Detail;
                    autoApproveHours = 0; // 申请管理类不自动通过
                    break;

                default:
                    track.Warning("AppInteraction: {Interaction} 收到未处理的 ActionType: ActionType={ActionType}, GuildId={GuildId}, UserId={UserId}",
                        actionCustomId, actionType, guild.Id, Context.User.Id);
                    return;
            }

            var created = await _ticketManager.CreateTicketAsync(
                ticketType,
                ticketCategory,
                new TicketCreator(Context.User.Id, Context.User.GlobalName, Context.User.Username),
                guild.Id,
                content,
                autoApproveHours,
                secondaryContent,
                cooldown: config.EnableTicketCd ? TimeSpan.FromHours(config.TicketCd) : null);

            if (!created.Success)
            {
                track.Info("工单提交冷却中: GuildId={GuildId}, UserId={UserId}, Remaining={Remaining}",
                    guild.Id, Context.User.Id, created.CooldownRemaining);

                await FollowupAsync(
                    components: BuildCooldownNotice(created.CooldownRemaining!.Value),
                    ephemeral: true);
                return;
            }

            var ticket = created.Ticket!;

            // 投递状态卡片（投诉记录）与审核卡片（办公厅）
            var publish = await _ticketCardService.PublishNewTicketAsync(guild, config, ticket);

            if (!publish.AllSent)
            {
                track.Warning("工单 {Ticket} 通知未全部送达: 状态卡片={StatusCardSent}, 审核卡片={AuditCardSent}",
                    ticket, publish.StatusCardSent, publish.AuditCardSent);
            }

            await FollowupAsync(components: BuildReceipt(ticket, publish), ephemeral: true);
        }
        catch (Exception ex)
        {
            track.Error(ex);

            // 已经 DeferAsync 过，必须给用户一个明确的结束态，避免交互一直转圈
            try
            {
                await FollowupAsync(
                    components: MessagePrefab.FunctionFail("提交工单时发生内部错误，请联系维护者"),
                    ephemeral: true);
            }
            catch (Exception notifyEx)
            {
                track.Warning(notifyEx, "向用户回报提交失败时再次失败");
            }
        }
    }

    /// <summary>冷却提示：把剩余时间渲染成"X 小时 Y 分钟"。</summary>
    private static MessageComponent BuildCooldownNotice(TimeSpan remaining)
    {
        var text = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分钟"
            : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟";

        return MessagePrefab.AnyNotifier(
            $"你在本服务器还有一张工单处于冷却期，请在 **{text}** 后再次提交。\n-# 如确需立即提交，请联系管理组。",
            "### ⏳ 提交过于频繁");
    }

    /// <summary>
    /// 提交成功的回执。按工单类型裁剪：只有投诉类提示"证据可在频道内补充"。
    /// </summary>
    private static MessageComponent BuildReceipt(TicketData ticket, TicketPublishResult publish)
    {
        var lines = new List<string>
        {
            $"### 🎫 工单编号：**{ticket}**",
            "管理员审核通过后会为你创建专属工单频道，审核结果将通过私信通知。",
        };

        if (ticket.TicketType is TicketType.Complaint)
        {
            lines.Add("> 📎 截图 / 文件证据可在工单频道创建后于频道内补充。");
        }

        if (ticket.AutoApproveTime is { } due)
        {
            lines.Add($"-# 若 <t:{due.ToUnixTimeSeconds()}:R> 前无人审核，将自动通过。");
        }

        if (!publish.AllSent)
        {
            lines.Add("-# ⚠️ 通知管理组的消息发送失败，请联系维护者。");
        }

        return MessagePrefab.AnySuccess(string.Join('\n', lines), "### ✅ 提交成功");
    }
}
