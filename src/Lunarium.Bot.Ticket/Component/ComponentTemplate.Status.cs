using Discord;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Model;

namespace Lunarium.Bot.Ticket.Component;

public static partial class ComponentTemplate
{
    /// <summary>投诉记录频道里的工单状态卡片（随状态变化原地刷新）。</summary>
    public static class TicketStatusMessage
    {
        public static Embed ToEmbed(TicketData ticket)
        {
            var embed = new EmbedBuilder()
                .WithColor(GetColor(ticket.Status))
                .WithTitle($"{TicketData.FormatId(ticket.Id)}丨{FormatTicketCategory(ticket.TicketType, ticket.TicketCategory)}")
                .WithDescription($"""
                来自 <@{ticket.TicketCreator.UserId}> 的工单:
                {ticket.Content.Slice(150)}{(ticket.SecondaryContent is not null ? $"\n> 额外内容: {ticket.SecondaryContent.Slice(150)}" : "")}
                """)
                .WithFields(GetFields(ticket))
                // .WithFooter($"最后变动: {ticket.LastUpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}")
                .WithFooter("最后变动")
                .WithTimestamp(ticket.LastUpdatedAt)
                .Build();

            return embed;
        }

        private static Color GetColor(TicketStatus status)
        {
            switch (status)
            {
                case TicketStatus.Pending:
                    return Color.Orange;
                case TicketStatus.Open:
                    return Color.Green;
                case TicketStatus.Closed:
                    return Color.LightGrey;
                case TicketStatus.Archived:
                    return Color.DarkerGrey;
                case TicketStatus.Rejected:
                    return Color.Red;
            }
            return Color.Default;
        }

        private static string GetStatusString(TicketStatus status)
        {
            switch (status)
            {
                case TicketStatus.Pending:
                    return "🔄 待审核";
                case TicketStatus.Open:
                    return "🟢 已开启";
                case TicketStatus.Closed:
                    return "📂 已关闭";
                case TicketStatus.Archived:
                    return "📁 已归档";
                case TicketStatus.Rejected:
                    return "⛔️ 已拒绝";
            }
            return "❓ 未知";
        }

        private static IEnumerable<EmbedFieldBuilder> GetFields(TicketData ticket)
        {
            var list = new List<EmbedFieldBuilder>();
            list.Add(new EmbedFieldBuilder()
                .WithName("状态")
                .WithValue(GetStatusString(ticket.Status))
                .WithIsInline(true)
            );
            // Pending 期工单频道尚未创建，不要渲染无效的频道提及
            if (ticket.ChannelId != 0)
            {
                list.Add(new EmbedFieldBuilder()
                    .WithName("频道")
                    .WithValue($"<#{ticket.ChannelId}>")
                    .WithIsInline(true)
                );
            }
            if (ticket.Status is TicketStatus.Open && ticket.ApproveInfo is not null)
            {
                list.Add(new EmbedFieldBuilder()
                    .WithName("审批人")
                    .WithValue(ticket.IsAutoApprove ? "系统（自动通过）" : $"<@{ticket.ApproveInfo.UserId}>")
                    .WithIsInline(true)
                );
            }
            if (ticket.Status is TicketStatus.Closed && ticket.CloseInfo is not null)
            {
                list.Add(new EmbedFieldBuilder()
                    .WithName("关闭人")
                    .WithValue($"<@{ticket.CloseInfo.UserId}>")
                    .WithIsInline(true)
                );
            }
            if (ticket.Status is TicketStatus.Archived && ticket.ArchiveInfo is not null)
            {
                list.Add(new EmbedFieldBuilder()
                    .WithName("归档人")
                    .WithValue($"<@{ticket.ArchiveInfo.UserId}>")
                    .WithIsInline(true)
                );
            }
            if (ticket.Status is TicketStatus.Rejected && ticket.RejectInfo is not null)
            {
                list.Add(new EmbedFieldBuilder()
                    .WithName("拒绝原因")
                    .WithValue($"{ticket.RejectInfo.Reason}\n-# 操作人: <@{ticket.RejectInfo.UserId}>")
                    .WithIsInline(false)
                );
            }
            return list;
        }
    }
}
