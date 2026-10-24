using Discord;

using Lunarium.Bot.Ticket.Model;

namespace Lunarium.Bot.Ticket.Component;

public static partial class ComponentTemplate
{
    /// <summary>
    /// 自动安抚发送后，在通知频道投递的固定提示卡片。
    /// <para>
    /// 只展示客观事实（工单、提交人、频道、自动通过时间），不包含任何来自模型的自由文本。
    /// </para>
    /// </summary>
    public static class SootheNoticeMessage
    {
        public static Embed ToEmbed(TicketData ticket)
        {
            var fields = new List<EmbedFieldBuilder>
            {
                new EmbedFieldBuilder()
                    .WithName("提交人")
                    .WithValue($"<@{ticket.TicketCreator.UserId}>")
                    .WithIsInline(true),
                new EmbedFieldBuilder()
                    .WithName("工单频道")
                    .WithValue(ticket.ChannelId != 0 ? $"<#{ticket.ChannelId}>" : "—")
                    .WithIsInline(true),
            };

            if (ticket.ApproveInfo is { } approve)
            {
                fields.Add(new EmbedFieldBuilder()
                    .WithName("自动通过于")
                    .WithValue($"<t:{approve.ApprovedAt.ToUnixTimeSeconds()}:f>")
                    .WithIsInline(true));
            }

            var waited = ticket.SootheInfo is { } soothe && ticket.ApproveInfo is { } approved
                ? Math.Max(0, (int)(soothe.CheckedAt - approved.ApprovedAt).TotalMinutes)
                : (int?)null;

            var situation = waited is { } minutes
                ? $"自动通过后已超过 {minutes} 分钟没有管理组发言，"
                : "自动通过后长时间没有管理组发言，";

            return new EmbedBuilder()
                .WithColor(Color.Teal)
                .WithTitle("📨 已发送自动安抚")
                .WithDescription($"""
                工单 **{ticket}** {situation}
                Bot 已向提交人发送一条安抚消息。
                """)
                .WithFields(fields)
                .WithFooter("系统自动通过 · 自动安抚")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }
    }
}
