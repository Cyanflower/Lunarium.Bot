using System.ComponentModel;
using Discord;
using Lunarium.Bot.Extensions;

namespace Lunarium.Bot.VoteBot.VoteServices;

public enum SubmissionStatus
{
    [Description("草稿")]
    Draft = 0,
    // [Description("待审核")]
    // PendingAudit = 10,
    [Description("已通过")]
    Approved = 20,
    [Description("需修改")]
    ChangesRequested = 30,
    [Description("已取消资格")]
    Disqualified = 40,
    [Description("用户已撤销")]
    CancelledByUser = 50
}

public enum PreferenceType
{
    [Description("未选择")]
    None = 0,
    [Description("男性向")]
    MaleOriented = 10,
    [Description("女性向")]
    FemaleOriented = 20,
    [Description("全性向")]
    AllInclusive = 30,
    [Description("小众向")]
    LGBTQ = 40,
    [Description("深渊区")]
    R18G = 50
}

public enum Choice
{
    [Description("未选择")]
    None = 0,
    [Description("资源区管理")]
    ResourcesAdmin = 10,
    [Description("聊天区管理")]
    ChatsAdmin = 20,
    [Description("记者")]
    Press = 30,
    [Description("赛事委员")]
    CompetitionAdmin = 40
}

public class SubmitData
{
    // MetaData
    public ulong PublicMessageId { get; set; }
    public ulong AuditMessageId { get; set; }
    public string UserAvatarsUrl { get; set; } = "";

    // ==== 用户侧 ====
    // 竞选内容提交者
    public ulong SubmitterId { get; init; }
    // 自我介绍
    public string AboutMe { get; set; } = "";
    // 偏好方向
    public HashSet<PreferenceType> Preferences { get; set; } = new();
    // 第一志愿
    public Choice FirstChoice { get; set; } = Choice.None;
    // 第二志愿
    public Choice SecondChoice { get; set; } = Choice.None;
    // 竞选宣言
    public string Manifesto { get; set; } = "";
    // 新闻稿(如果是记者)
    public string PressRelease { get; set; } = "";

    // ==== Admin侧 ====
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public string RefuseReason { get; set; } = "";

    public void Cancel()
    {
        PublicMessageId = 0;
        AuditMessageId = 0;
        AboutMe = "";
        Preferences = new();
        FirstChoice = Choice.None;
        SecondChoice = Choice.None;
        Manifesto = "";
        PressRelease = "";
        Status = SubmissionStatus.CancelledByUser;
    }

    public void Submit(SubmitData submitData)
    {
        PublicMessageId = submitData.PublicMessageId;
        AuditMessageId = submitData.AuditMessageId;
        AboutMe = submitData.AboutMe;
        Preferences = submitData.Preferences;
        FirstChoice = submitData.FirstChoice;
        SecondChoice = submitData.SecondChoice;
        Manifesto = submitData.Manifesto;
        PressRelease = submitData.PressRelease;
        Status = SubmissionStatus.Approved;
    }

    public void Audit(SubmissionStatus status, string refuseReason)
    {
        Status = status;
        RefuseReason = refuseReason;
    }

    public MessageComponent ToPublicMessageComponent()
    {
        return GetComponentBuilder().Build();
    }

    public MessageComponent ToAuditMessageComponent()
    {
        var component = GetComponentBuilder();
        component.WithContainer(container => container
            .WithAccentColor(Color.Blue)
            // .WithTextDisplay("""
            //     ### 审核操作
            //     - ❌ 拒绝
            //     > 拒绝本次申请, 并禁止继续在本次普选中进行投递
            //     - ⚠️ 需修改
            //     > 拒绝本次申请, 申请人可重新投递
            //     - ✅ 通过
            //     > 通过本次申请
            //     """)
            .WithTextDisplay("""
                ### 审核操作
                - ❌ 拒绝
                > 拒绝本次申请, 并禁止继续在本次普选中进行投递
                - ⚠️ 需修改
                > 拒绝本次申请, 申请人可重新投递
                """)
            .WithActionRow(row => row
                .WithButton(
                    label: "拒绝",
                    customId: $"Audit:Disqualified:{SubmitterId}",
                    style: ButtonStyle.Danger,
                    disabled: !(Status == SubmissionStatus.Approved)
                )
                .WithButton(
                    label: "需修改",
                    customId: $"Audit:ChangesRequested:{SubmitterId}",
                    style: ButtonStyle.Primary,
                    disabled: !(Status == SubmissionStatus.Approved)
                )
                // .WithButton(
                //     label: "通过",
                //     customId: $"Audit:Approved:{SubmitterId}",
                //     style: ButtonStyle.Success,
                //     disabled: !(Status == SubmissionStatus.PendingAudit)
                // )
            )
        );
        return component.Build();
    }
    
    private ComponentBuilderV2 GetComponentBuilder()
    {
        return new ComponentBuilderV2()
            .WithContainer(container =>
            {
                container.Id = 1000;
                // 根据状态决定颜色
                var accentColor = Status switch
                {
                    // ✅ 成功/通过 -> 绿色
                    SubmissionStatus.Approved => Color.Green,

                    // ⚠️ 需要修改/草稿 -> 橙色/金色 (提醒用户注意)
                    SubmissionStatus.ChangesRequested => Color.Orange,
                    SubmissionStatus.Draft => Color.Gold,

                    // ❌ 拒绝/取消资格 -> 红色
                    SubmissionStatus.Disqualified => Color.Red,
                    SubmissionStatus.CancelledByUser => Color.DarkGrey, // 已撤销可以用灰色

                    // ℹ️ 待审核 -> 蓝色 (标准色)
                    // SubmissionStatus.PendingAudit => Color.Blue,

                    // 兜底策略
                    _ => Color.Blue
                };

                container.WithAccentColor(accentColor);
                // if (Status >= SubmissionStatus.ChangesRequested) container.WithAccentColor(Color.Blue);
                // else container.WithAccentColor(Color.Red);

                container.WithTextDisplay("### 旅程 2026 第一季度管理组换届普选");

                container.WithSeparator();

                container.WithSection(section => section
                    .WithTextDisplay($"""
                        - **候选人:**
                        <@{SubmitterId}>
                        - **第一志愿**
                        > {FirstChoice.ToDescription()}
                        - **第二志愿**
                        > {SecondChoice.ToDescription()}
                        """)
                    .WithAccessory(new ThumbnailBuilder().WithMedia(UserAvatarsUrl))
                );

                container.WithSeparator();

                container.WithTextDisplay($"""
                    **自我介绍:**
                    > {AboutMe}
                    """);

                container.WithSeparator();

                container.WithTextDisplay($"""
                    **竞选宣言:**
                    > {Manifesto}
                    """);

                if ((FirstChoice == Choice.Press || SecondChoice == Choice.Press) && !string.IsNullOrWhiteSpace(PressRelease))
                {
                    container.WithTextDisplay($"""
                        **新闻试稿:**
                        > {PressRelease}
                        """);
                }

                switch (Status)
                {
                    case SubmissionStatus.Approved:
                        break;
                    // case SubmissionStatus.PendingAudit:
                    //     break;
                    case SubmissionStatus.ChangesRequested:
                        container.WithSeparator();
                        container.WithTextDisplay($"""
                            ### ⚠️ 需修改:
                            **原因:**
                            > {RefuseReason}
                            """);
                        break;
                    case SubmissionStatus.Disqualified:
                        container.WithSeparator();
                        container.WithTextDisplay($"""
                            ### ❌ 已拒绝:
                            **原因:**
                            > {RefuseReason}
                            """);
                        break;
                    case SubmissionStatus.CancelledByUser:
                        container.WithSeparator();
                        container.WithTextDisplay($"""
                            ### ❌ 用户已撤回报名申请
                            """);
                        break;
                }
            });
    }
}


public class SubmitterData
{
    public string Name { get; set; } = "";
    public ulong SubmitterId { get; set; }
    public int Number { get; set; }
    public Choice FirstChoice { get; set; } = Choice.None;
    public Choice SecondChoice { get; set; } = Choice.None;
}

public class AllSubmitterDatas
{
    public List<SubmitterData> ResourcesAdmin { get; set; } = new();
    public List<SubmitterData> ChatsAdmin { get; set; } = new();
    public List<SubmitterData> Press { get; set; } = new();
    public List<SubmitterData> CompetitionAdmin { get; set; } = new();
}

public class VoteData
{
    // 投票者
    public ulong Voter { get; set; }
    // 资源区管理中投了谁?
    public HashSet<ulong> ResourcesAdmin { get; set; } = new();
    // 资源区管理中投了谁?
    public HashSet<ulong> ChatsAdmin { get; set; } = new();
    // 资源区管理中投了谁?
    public HashSet<ulong> Press { get; set; } = new();
    // 资源区管理中投了谁?
    public HashSet<ulong> CompetitionAdmin { get; set; } = new();
}