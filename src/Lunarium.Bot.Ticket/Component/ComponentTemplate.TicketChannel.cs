using Discord;

using Lunarium.Bot.Ticket.Model;

namespace Lunarium.Bot.Ticket.Component;

public static partial class ComponentTemplate
{
    /// <summary>工单频道创建时发送的开场消息（之后不再变化）。</summary>
    public static class StartMessage
    {
        public static MessageComponent ToComponent(int ticketId, ulong ticketCreatorId, string content)
        {
            return new ComponentBuilderV2().WithContainer(container =>
            {
                container.WithAccentColor(Color.Blue);
                container.WithTextDisplay($"## 📋 社区服务中心 | {TicketData.FormatId(ticketId)}");
                container.WithSeparator();
                container.WithTextDisplay("""
                ### 👋 欢迎来到您的工单频道！
                """);
                container.WithSeparator();
                container.WithTextDisplay("""
                📌 **重要信息：**
                > - 这是一个私密频道，只有您和管理员可以看到
                > - 请详细描述您遇到的问题
                > - 管理员会尽快回复并处理您的诉求
                > - 在问题解决前，请保持耐心
                💬 **如何使用：**
                > - 您可以在此频道中与管理员自由交流
                > - 可以发送文字、图片和文件
                > - 如有更多问题，请随时补充说明
                🔒 **隐私保护：**
                > - 您的诉求内容会严格保密
                > - 仅授权的管理员可以查看
                > - 处理完成后会进行归档保存
                """);
                container.WithSeparator();
                container.WithTextDisplay($"""
                ### 📝 工单内容：
                {content}
                """);
                container.WithSeparator();
                container.WithTextDisplay($"""
                提交人：<@{ticketCreatorId}>
                """);
            })
            .Build();
        }
    }
}
