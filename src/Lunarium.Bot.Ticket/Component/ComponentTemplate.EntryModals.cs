using Discord;
using Discord.Interactions;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Model;

namespace Lunarium.Bot.Ticket.Component;

public static partial class ComponentTemplate
{
    /// <summary>我要投诉。</summary>
    public static class ComplaintModal
    {
        public const string ModalCustomId = "modal:complaint:fixed";

        /// <summary>
        /// 二级分类(我要投诉)。分类名已简化，细则对照社区守则自行判断。
        /// 归档统计参考：侵权 183 / 商业化 218 / 攻击与引战 175 / 其他违规 146。
        /// </summary>
        public static List<SelectOption<TicketCategory>> Options = [
            new("侵权盗用", TicketCategory.CopyrightInfringement),
            new("商业化与贩售", TicketCategory.Commercialization),
            new("人身攻击与引战", TicketCategory.HarassmentAndFlaming),
            new("其他违规", TicketCategory.OtherViolation),
        ];

        public static Modal ToModal()
        {
            return new ModalBuilder()
                .WithTitle("我要投诉")
                .WithCustomId(ModalCustomId)
                .AddSelectMenu(
                    label: "投诉类型",
                    customId: "type",
                    options: ToSelectOptions(Options),
                    placeholder: "请选择最接近的一类",
                    description: "选不准可改用「联系管理组」")
                .AddTextInput(
                    label: "情况说明",
                    customId: "detail",
                    style: TextInputStyle.Paragraph,
                    placeholder: "发生了什么、什么时候、什么地方、涉及哪些内容",
                    minLength: 5,
                    maxLength: 2000,
                    description: "文件/图片证据可在投诉频道创建后在频道内发布")
                .Build();
        }

        public sealed class InteractionModal : IModal
        {
            public string Title => "我要投诉";

            [ModalSelectMenu("type")]
            public TicketCategory Type { get; set; }

            [ModalTextInput("detail")]
            public string Detail { get; set; } = string.Empty;
        }
    }

    /// <summary>联系管理组。</summary>
    public static class ContactModal
    {
        public const string ModalCustomId = "modal:contact";

        /// <summary>
        /// 二级分类(联系管理组)。
        /// 归档统计参考：处罚申诉 20 / 执法争议 40 / 建议与反馈 47 / 申请办理 67 / 个人纠纷 5。
        /// </summary>
        public static List<SelectOption<TicketCategory>> Options = [
            new("处罚申诉(对处罚有异议 / 诉求解封)", TicketCategory.PunishmentAppeal),
            new("规则与执法争议(判罚标准 / 双标 / 程序问题)", TicketCategory.ModerationDispute),
            new("社区建议与反馈(分区 / 活动 / 规则改进)", TicketCategory.CommunitySuggestion),
            new("申请频道或身份组(作者频道 / 鉴赏家 / 志愿者)", TicketCategory.ChannelOrRoleApplication),
            new("其他(找不到适合的类别? 选这个)", TicketCategory.Other),
        ];

        public static Modal ToModal()
        {
            return new ModalBuilder()
                .WithTitle("联系管理组")
                .WithCustomId(ModalCustomId)
                .AddSelectMenu(
                    label: "事项类型",
                    customId: "type",
                    options: ToSelectOptions(Options),
                    placeholder: "请选择最接近的一类",
                    description: "需要裁定、解释或办理的事项都选这里")
                .AddTextInput(
                    label: "诉求说明",
                    customId: "detail",
                    style: TextInputStyle.Paragraph,
                    placeholder: "请说明事情经过与希望管理组做什么",
                    minLength: 5,
                    maxLength: 2000)
                .Build();
        }

        public sealed class InteractionModal : IModal
        {
            public string Title => "联系管理组";

            [ModalSelectMenu("type")]
            public TicketCategory Type { get; set; }

            [ModalTextInput("detail")]
            public string Detail { get; set; } = string.Empty;
        }
    }

    /// <summary>帮助与支持。</summary>
    public static class HelpModal
    {
        public const string ModalCustomId = "modal:help";

        /// <summary>
        /// 二级分类(帮助/支持)。
        /// 归档统计参考：帖子与权限 47 / Bot 与功能异常 83 / 账号问题少量。
        /// </summary>
        public static List<SelectOption<TicketCategory>> Options = [
            new("帖子与权限(解锁 / 删除 / 恢复 / 只读)", TicketCategory.PostAndPermission),
            new("Bot与功能异常(Bot 报错 / 搜索失败)", TicketCategory.BotAndFeatureIssue),
            new("账号与身份组异常(账号异常 / 身份组未发放)", TicketCategory.AccountAndRoleIssue)
        ];

        public static Modal ToModal()
        {
            return new ModalBuilder()
                .WithTitle("帮助与支持")
                .WithCustomId(ModalCustomId)
                .AddSelectMenu(
                    label: "问题类型",
                    customId: "type",
                    options: ToSelectOptions(Options),
                    placeholder: "请选择最接近的一类",
                    description: "举报他人违规请改用「我要投诉」")
                .AddTextInput(
                    label: "帖子或频道链接(可选)",
                    customId: "target",
                    placeholder: "出问题的帖子 / 频道链接",
                    maxLength: 500,
                    required: false)
                .AddTextInput(
                    label: "问题描述",
                    customId: "detail",
                    style: TextInputStyle.Paragraph,
                    placeholder: "报错内容、操作步骤、发生时间或任何对于问题的描述",
                    minLength: 5,
                    maxLength: 2000)
                .Build();
        }

        public sealed class InteractionModal : IModal
        {
            public string Title => "帮助与支持";

            [ModalSelectMenu("type")]
            public TicketCategory Type { get; set; }

            [ModalTextInput("target")]
            public string Target { get; set; } = string.Empty;

            [ModalTextInput("detail")]
            public string Detail { get; set; } = string.Empty;
        }
    }

    /// <summary>申请管理组。</summary>
    public static class ApplyForStaffModal
    {
        public const string ModalCustomId = "modal:applyForStaff";

        /// <summary>二级分类(职务)。</summary>
        public static List<SelectOption<TicketCategory>> Options = [
            new("风纪组: 日常管理", TicketCategory.DisciplineTeam),
            new("事务组: 记者", TicketCategory.Reporter),
            new("事务组: 赛事组", TicketCategory.TournamentTeam),
            new("事务组: 维护员", TicketCategory.Maintainer),
        ];

        public static Modal ToModal()
        {
            return new ModalBuilder()
                .WithTitle("申请管理组")
                .WithCustomId(ModalCustomId)
                .AddSelectMenu(
                    label: "申请职务",
                    customId: "type",
                    options: ToSelectOptions(Options),
                    placeholder: "请选择希望加入的职务",
                    description: "请选择最接近的一个方向")
                .AddTextInput(
                    label: "申请说明",
                    customId: "detail",
                    style: TextInputStyle.Paragraph,
                    placeholder: "请说明申请动机、可投入的时间与相关经验",
                    minLength: 5,
                    maxLength: 2000,
                    description: "申请通过后将在工单频道内与您沟通")
                .Build();
        }

        public sealed class InteractionModal : IModal
        {
            public string Title => "申请管理组";

            [ModalSelectMenu("type")]
            public TicketCategory Type { get; set; }

            [ModalTextInput("detail")]
            public string Detail { get; set; } = string.Empty;
        }
    }
}
