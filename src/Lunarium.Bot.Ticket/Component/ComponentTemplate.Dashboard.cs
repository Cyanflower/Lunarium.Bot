using Discord;

namespace Lunarium.Bot.Ticket.Component;

public static partial class ComponentTemplate
{
    /// <summary>入口面板（联系管理频道里的常驻消息）。</summary>
    public static class DashboardMessage
    {
        public const string ComplaintCustomId = "entry:complaint:fixed";
        public const string ContactStaffCustomId = "entry:contact:fixed";
        public const string HelpCustomId = "entry:help:fixed";
        public const string ApplyForStaffCustomId = "entry:applyForStaff:fixed";

        public static MessageComponent ToComponent()
        {
            return new ComponentBuilderV2().WithContainer(container =>
            {
                container.WithAccentColor(Color.Blue);
                container.WithTextDisplay("## 📋 社区服务中心");
                container.WithSeparator();
                container.WithTextDisplay("""
                ### 如果您在社区中:
                > - 遇到任何问题或需要举报不当行为
                > - 需要联系管理组
                > - 需要其他帮助
                ### 请点击下方按钮提交诉求。
                """);
                container.WithSeparator();
                container.WithTextDisplay("""
                ### 分类导引
                - 📝 **我要投诉** —— 举报违规的用户或内容
                > 侵权盗用、商业化与贩售、人身攻击与引战、其他违规
                - ✉️ **联系管理组** —— 需要管理组裁定、解释或办理
                > 处罚申诉、规则与执法争议、社区建议、申请频道 / 身份组
                - 🛠 **帮助/支持** —— 帖子与功能异常
                > 解锁 / 删除 / 恢复帖子、账号与身份组异常、Bot 报错
                - 🛡 **申请管理组** —— 加入社区管理团队
                > 管理组招募、自荐与推荐、职责与考核咨询

                -# Tips: 拿不准选哪个？ 或没有预期的分类？ 可直接选「联系管理组」
                """);
                container.WithSeparator();
                container.WithTextDisplay("""
                > ⚠️ 投诉将会创建一个私密频道，仅您和管理员可见。
                > 💡 请详细描述问题，以便我们更好地为您处理。
                > 🤝 我们致力于维护一个健康友好的社区环境。
                > 📜 [[Privacy Policy](https://gist.github.com/Cyanflower/07715d2ff9aeeea93f9ccb4a427f7f57)]
                """);
                container.WithSeparator();
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "我要投诉",
                        customId: ComplaintCustomId,
                        style: ButtonStyle.Danger,
                        emote: new Emoji("📝")
                    );
                    row.WithButton(
                        label: "联系管理组",
                        customId: ContactStaffCustomId,
                        style: ButtonStyle.Primary,
                        emote: new Emoji("✉️")
                    );
                    row.WithButton(
                        label: "帮助/支持",
                        customId: HelpCustomId,
                        style: ButtonStyle.Primary,
                        emote: new Emoji("🛠")
                    );
                    row.WithButton(
                        label: "申请管理组",
                        customId: ApplyForStaffCustomId,
                        style: ButtonStyle.Primary,
                        emote: new Emoji("📋")
                    );
                });
            })
            .Build();
        }
    }
}
