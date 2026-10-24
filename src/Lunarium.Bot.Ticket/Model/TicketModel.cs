using System.Text.Json.Serialization;
using System.ComponentModel;

namespace Lunarium.Bot.Ticket.Model;

public sealed class TicketData
{
    /// <summary>本地 JSON 的数据结构版本，用于将来的本地迁移。</summary>
    public int SchemaVersion { get; init; } = TicketSchema.CurrentVersion;

    /// <summary>全局唯一的工单号（跨服务器不重复）。</summary>
    public int Id { get; init; }

    /// <summary>工单所属服务器ID。工单不跨服务器：所有写操作都应先校验该值与当前上下文一致。</summary>
    public required ulong GuildId { get; init; }

    /// <summary>工单频道ID（Tick-0001）。为 0 表示尚未创建，启动对账时会按频道名认领或重建。</summary>
    public ulong ChannelId { get; set; }

    /// <summary>状态消息所在频道ID（投诉记录频道，建单时快照，防配置变更后找不回旧消息）。</summary>
    public ulong StatusChannelId { get; set; }

    /// <summary>状态消息ID（投诉记录频道内的那一张状态卡片）。</summary>
    public ulong StatusMessageId { get; set; }

    /// <summary>审核消息所在频道ID（办公厅，建单时快照）。</summary>
    public ulong AuditChannelId { get; set; }

    /// <summary>审核消息ID（办公厅内的审核卡片，通过/拒绝后原地 edit 成终态）。</summary>
    public ulong AuditMessageId { get; set; }

    public required TicketType TicketType { get; init; }
    public required TicketCategory TicketCategory { get; init; }

    public required TicketCreator TicketCreator { get; init; }
    public required string Content { get; init; }
    public string? SecondaryContent { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>最后一次状态变更时间（状态消息 footer 用）。</summary>
    public DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>
    /// 待审核自动通过的时刻，由建单时的 <see cref="GuildConfig.AutoApproveHours"/> 推算。
    /// <see langword="null"/> 表示不自动通过（对应配置值 0），此类工单必须人工审核。
    /// </summary>
    public DateTimeOffset? AutoApproveTime { get; init; }
    public bool IsAutoApprove { get; set; } = false;

    public ApproveInfo? ApproveInfo { get; set; } = null;
    public RejectInfo? RejectInfo { get; set; } = null;

    public CloseInfo? CloseInfo { get; set; } = null;
    public ArchiveInfo? ArchiveInfo { get; set; } = null;
    public List<TicketMessage>? TicketMessages { get; set; } = null;
    public LLMSuggestion? LLMSuggestion { get; set; } = null;

    /// <summary>
    /// 自动安抚的处理记录。为 <see langword="null"/> 表示尚未检查；
    /// 写入后该工单不再重复扫描（重启幂等依赖此字段）。
    /// </summary>
    public SootheInfo? SootheInfo { get; set; } = null;

    [JsonIgnore]
    public TicketStatus Status => GetStatus();

    private TicketStatus GetStatus()
    {
        // 时序是无法颠倒的
        if (RejectInfo is not null)
            return TicketStatus.Rejected;
        else if (ApproveInfo is null)
            return TicketStatus.Pending;
        else if (CloseInfo is null)
            return TicketStatus.Open;
        else if (ArchiveInfo is null)
            return TicketStatus.Closed;
        return TicketStatus.Archived;
    }

    /// <summary>工单号展示格式：5 位补零（<c>Ticket-00001</c>）。</summary>
    public static string FormatId(int id) => $"Ticket-{id:D5}";

    public override string ToString() => FormatId(Id);
}

/// <summary>一级分类的中文名称（面向人的展示）。</summary>
public static class TicketTypeExtensions
{
    public static string Describe(this TicketType type) => type switch
    {
        TicketType.Complaint => "投诉",
        TicketType.Contact => "联系管理组",
        TicketType.Help => "帮助/支持",
        TicketType.ApplyForStaff => "申请管理组",
        _ => type.ToString(),
    };
}

/// <summary>分类的中文名称（面向人的展示，归档 HTML / 日志可用）。</summary>
public static class TicketCategoryExtensions
{
    public static string Describe(this TicketCategory category) => category switch
    {
        TicketCategory.Unknown => "未知",
        TicketCategory.CopyrightInfringement => "侵权盗用",
        TicketCategory.Commercialization => "商业化与贩售",
        TicketCategory.HarassmentAndFlaming => "人身攻击与引战",
        TicketCategory.OtherViolation => "其他违规",
        TicketCategory.PunishmentAppeal => "处罚申诉",
        TicketCategory.ModerationDispute => "规则与执法争议",
        TicketCategory.CommunitySuggestion => "社区建议与反馈",
        TicketCategory.ChannelOrRoleApplication => "申请频道或身份组",
        TicketCategory.Other => "其他",
        TicketCategory.PostAndPermission => "帖子与权限",
        TicketCategory.BotAndFeatureIssue => "Bot 与功能异常",
        TicketCategory.AccountAndRoleIssue => "账号与身份组异常",
        TicketCategory.DisciplineTeam => "风纪组：日常管理",
        TicketCategory.Reporter => "事务组：记者",
        TicketCategory.TournamentTeam => "事务组：赛事组",
        TicketCategory.Maintainer => "事务组：维护员",
        _ => category.ToString(),
    };
}

/// <summary>本地 JSON 的数据结构版本号。</summary>
public static class TicketSchema
{
    /// <summary>当前版本。改动 <see cref="TicketData"/> 的落盘结构时递增，并在读取侧做迁移。</summary>
    public const int CurrentVersion = 1;
}

public sealed record TicketCreator(ulong UserId, string DisplayName, string UserName);

/// <summary>
/// 自动安抚的处理记录（系统自动通过的工单专用）。
/// </summary>
public sealed class SootheInfo
{
    /// <summary>本次检查的时间（无论最终是否发送）。</summary>
    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>
    /// 工单频道内的安抚消息ID。<c>0</c> 表示检查时已有管理组发言，因此没有发送安抚。
    /// </summary>
    public ulong MessageId { get; set; }

    /// <summary>通知频道内的提示卡片消息ID；<c>0</c> 表示尚未发出。</summary>
    public ulong NotifyMessageId { get; set; }

    /// <summary>通知（卡片 + @here）完成的时间；<see langword="null"/> 表示尚未完成，重启后会补发。</summary>
    public DateTimeOffset? NotifiedAt { get; set; }
}

public sealed record CloseInfo(ulong UserId, string DisplayName, string UserName, DateTimeOffset ClosedAt);
public sealed record ArchiveInfo(ulong UserId, string DisplayName, string UserName, DateTimeOffset ArchivedAt);
public sealed record ApproveInfo(ulong UserId, string DisplayName, string UserName, DateTimeOffset ApprovedAt);
public sealed record RejectInfo(ulong UserId, string DisplayName, string UserName, DateTimeOffset RejectedAt, string Reason);

/// <summary>
/// 工单状态枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TicketStatus>))]
public enum TicketStatus
{
    /// <summary>
    /// 待审核(已创建, 尚未被处理)
    /// </summary>
    Pending,

    /// <summary>
    /// 进行中(已通过审核, 尚未关闭)
    /// </summary>
    Open,

    /// <summary>
    /// 已关闭(已关闭, 尚未归档)
    /// </summary>
    Closed,

    /// <summary>
    /// 已归档(已归档, 流程结束)
    /// </summary>
    Archived,

    /// <summary>
    /// 已拒绝(审核未通过)
    /// </summary>
    Rejected
}

/// <summary>
/// 工单类型枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TicketType>))]
public enum TicketType
{
    /// <summary>
    /// 投诉(侵权 / 违规行为等)
    /// </summary>
    Complaint,

    /// <summary>
    /// 联系管理组
    /// </summary>
    Contact,

    /// <summary>
    /// 帮助(求助与支持)
    /// </summary>
    Help,

    /// <summary>
    /// 申请管理
    /// </summary>
    ApplyForStaff
}


/// <summary>
/// 工单二级分类枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TicketCategory>))]
public enum TicketCategory
{
    Unknown = 0,

    #region 我要投诉 (Complaint)

    /// <summary>
    /// 侵权盗用（含二传 / 搬运 / 抄袭 / 盗图）
    /// </summary>
    CopyrightInfringement = 101,

    /// <summary>
    /// 商业化与贩售(收费定制 / 代打 / 卖 API / 资源贩售)
    /// </summary>
    Commercialization = 102,

    /// <summary>
    /// 人身攻击与引战(辱骂 / 挂人 / 骚扰 / 恶意点踩)
    /// </summary>
    HarassmentAndFlaming = 103,

    /// <summary>
    /// 其他违规（广告拉群 / 涉未成年 / 现实映射 / 分区违规 / 小号规避等未归入以上三类的违规）
    /// </summary>
    OtherViolation = 104,

    #endregion

    #region 联系管理组 (Contact)

    /// <summary>
    /// 处罚申诉(对处罚有异议 / 诉求解封)
    /// </summary>
    PunishmentAppeal = 201,

    /// <summary>
    /// 规则与执法争议(判罚标准 / 双标 / 程序问题)
    /// </summary>
    ModerationDispute = 202,

    /// <summary>
    /// 社区建议与反馈(分区 / 活动 / 规则改进)
    /// </summary>
    CommunitySuggestion = 203,

    /// <summary>
    /// 申请频道或身份组(作者频道 / 鉴赏家 / 志愿者)
    /// </summary>
    ChannelOrRoleApplication = 204,

    /// <summary>
    /// 其他
    /// </summary>
    Other = 206,

    #endregion

    #region 帮助与支持 (Help)

    /// <summary>
    /// 帖子与权限(解锁 / 删除 / 恢复 / 只读)
    /// </summary>
    PostAndPermission = 301,

    /// <summary>
    /// Bot与功能异常(Bot 报错 / 搜索失败)
    /// </summary>
    BotAndFeatureIssue = 302,

    /// <summary>
    /// 账号与身份组异常（账号异常 / 身份组未发放）
    /// </summary>
    AccountAndRoleIssue = 303,

    #endregion

    #region 申请管理组 (ApplyForStaff)

    /// <summary>
    /// 风纪组：日常管理
    /// </summary>
    DisciplineTeam = 401,

    /// <summary>
    /// 事务组：记者
    /// </summary>
    Reporter = 402,

    /// <summary>
    /// 事务组：赛事组
    /// </summary>
    TournamentTeam = 403,

    /// <summary>
    /// 事务组：维护员
    /// </summary>
    Maintainer = 404

    #endregion
}