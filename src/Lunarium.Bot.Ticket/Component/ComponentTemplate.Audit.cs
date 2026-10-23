using Discord;
using Discord.Interactions;

using Lunarium.Bot.Ticket.Model;

namespace Lunarium.Bot.Ticket.Component;

public static partial class ComponentTemplate
{
    /// <summary>待审核卡片（办公厅），带通过 / 拒绝按钮。</summary>
    public static class PendingAuditMessage
    {
        public const string Separator = ":";
        public const string ApprovePrefix = "audit:approve";
        public const string RejectPrefix = "audit:reject";
        public const string FastRejectPrefix = "audit:fastReject";

        public static MessageComponent ToComponent(int ticketId, ulong ticketCreatorId, string content, DateTimeOffset? autoApproveTime, LLMSuggestion? suggestion)
        {
            return new ComponentBuilderV2().WithContainer(container =>
            {
                container.WithAccentColor(Color.Blue);
                container.WithTextDisplay($"## 📋 社区服务中心 | {TicketData.FormatId(ticketId)}");
                container.WithSeparator();
                container.WithTextDisplay("""
                ### 🔄 待审核工单
                @here
                """);
                container.WithSeparator();
                container.WithTextDisplay($"""
                ### 📝 提交内容：
                {content}
                """);
                container.WithSeparator();
                container.WithTextDisplay($"""
                提交人：<@{ticketCreatorId}>
                """);
                container.WithSeparator();
                if (suggestion is null)
                {
                    container.WithTextDisplay($"""
                    ### AI建议: ⚠️
                    > LLM 配置未启用 或 LLM 建议生成失败
                    """);
                }
                else if (string.IsNullOrWhiteSpace(suggestion.RejectReason))
                {
                    container.WithTextDisplay($"""
                    ### AI建议: {suggestion.Emoji}
                    {QuoteLines(suggestion.Suggestion)}
                    """);
                }
                else
                {
                    container.WithTextDisplay($"""
                    ### AI建议: {suggestion.Emoji}
                    {QuoteLines(suggestion.Suggestion)}
                    ### 拒绝理由
                    ```
                    {suggestion.RejectReason}
                    ```
                    """);
                }
                container.WithSeparator();
                container.WithTextDisplay(FormatAutoApproveLine(autoApproveTime));
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "通过",
                        customId: $"{ApprovePrefix}{Separator}{ticketId}",
                        style: ButtonStyle.Success
                    );
                    row.WithButton(
                        label: "拒绝",
                        customId: $"{RejectPrefix}{Separator}{ticketId}",
                        style: ButtonStyle.Danger
                    );
                    if (suggestion is not null && suggestion.RejectReason is not null)
                    {
                        row.WithButton(
                            label: "拒绝(使用LLM理由)",
                            customId: $"{FastRejectPrefix}{Separator}{ticketId}",
                            style: ButtonStyle.Danger
                        );
                    }
                });
            })
            .Build();
        }
    }

    /// <summary>审核通过后的卡片（原地替换待审核卡片）。</summary>
    public static class ApprovedAuditMessage
    {
        public static MessageComponent ToComponent(int ticketId, ulong ticketCreatorId, string content, ulong approverId, DateTimeOffset? autoApproveTime, LLMSuggestion? suggestion, bool isAutoApprove)
        {
            return new ComponentBuilderV2().WithContainer(container =>
            {
                container.WithAccentColor(Color.Green);
                container.WithTextDisplay($"## 📋 社区服务中心 | {TicketData.FormatId(ticketId)}");
                container.WithSeparator();
                container.WithTextDisplay("""
                ### ✅ 已通过审核
                @here
                """);
                container.WithSeparator();
                container.WithTextDisplay($"""
                ### 提交人：<@{ticketCreatorId}>
                ### 📝 提交内容：
                {content}
                """);
                container.WithSeparator();
                if (suggestion is null)
                {
                    container.WithTextDisplay("""
                    ### AI建议
                    > 未启用或生成失败
                    """);
                }
                else if (string.IsNullOrWhiteSpace(suggestion.RejectReason))
                {
                    container.WithTextDisplay($"""
                    ### AI建议
                    {QuoteLines(suggestion.Suggestion)}
                    """);
                }
                else
                {
                    container.WithTextDisplay($"""
                    ### AI建议
                    {QuoteLines(suggestion.Suggestion)}
                    ### 拒绝理由
                    ```
                    {suggestion.RejectReason}
                    ```
                    """);
                }
                container.WithSeparator();
                container.WithTextDisplay($"""
                审批人：{(isAutoApprove ? "系统（自动通过）" : $"<@{approverId}>")}
                """);
                container.WithSeparator();
                container.WithTextDisplay(FormatAutoApproveLine(autoApproveTime));
            })
            .Build();
        }
    }

    /// <summary>审核拒绝后的卡片（原地替换待审核卡片）。</summary>
    public static class RejectedAuditMessage
    {
        public static MessageComponent ToComponent(int ticketId, ulong ticketCreatorId, string content, ulong approverId, string reason, LLMSuggestion? suggestion)
        {
            return new ComponentBuilderV2().WithContainer(container =>
            {
                container.WithAccentColor(Color.Red);
                container.WithTextDisplay($"## 📋 社区服务中心 | {TicketData.FormatId(ticketId)}");
                container.WithSeparator();
                container.WithTextDisplay("""
                ### ⛔️ 已拒绝工单
                @here
                """);
                container.WithSeparator();
                container.WithTextDisplay($"""
                ### 提交人：<@{ticketCreatorId}>
                ### 📝 提交内容：
                {content}
                """);
                container.WithSeparator();
                if (suggestion is null)
                {
                    container.WithTextDisplay("""
                    ### AI建议
                    > 未启用或生成失败
                    """);
                }
                else if (string.IsNullOrWhiteSpace(suggestion.RejectReason))
                {
                    container.WithTextDisplay($"""
                    ### AI建议
                    {QuoteLines(suggestion.Suggestion)}
                    """);
                }
                else
                {
                    container.WithTextDisplay($"""
                    ### AI建议
                    {QuoteLines(suggestion.Suggestion)}
                    ### 拒绝理由
                    ```
                    {suggestion.RejectReason}
                    ```
                    """);
                }
                container.WithSeparator();
                container.WithTextDisplay($"""
                审批人：<@{approverId}>
                """);
                container.WithTextDisplay($"""
                原因：{reason}
                """);
                container.WithSeparator();
            })
            .Build();
        }
    }

    /// <summary>拒绝工单的模态窗（customId 带工单号）。</summary>
    public static class AuditModal
    {
        /// <summary>模态窗 customId 前缀；实际 ID 为 <c>modal:audit:reject:{ticketId}</c>。</summary>
        public const string ModalCustomIdPrefix = "modal:audit:reject";

        public static Modal ToModal(int ticketId)
        {
            return new ModalBuilder()
                .WithTitle("拒绝")
                .WithCustomId($"{ModalCustomIdPrefix}:{ticketId}")
                .AddTextInput(
                    label: "原因",
                    customId: "reason",
                    placeholder: "拒绝的原因",
                    style: TextInputStyle.Paragraph,
                    minLength: 5,
                    maxLength: 2000,
                    required: true)
                .Build();
        }

        public sealed class InteractionModal : IModal
        {
            public string Title => "拒绝工单";

            [ModalTextInput("reason")]
            public string Reason { get; set; } = string.Empty;
        }
    }
}
