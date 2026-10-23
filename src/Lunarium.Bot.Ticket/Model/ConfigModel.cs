using System.ComponentModel.DataAnnotations;

using Lunarium.Bot.Framework.Configuration;

namespace Lunarium.Bot.Ticket.Model;

/// <summary>
/// Ticket 机器人的完整配置(对应 appsettings.json)。
/// <para>
/// 字段规则直接写在模型上：简单规则用特性（如 <see cref="SnowflakeIdAttribute"/>），
/// 条件/跨字段规则写在各类的 <see cref="IValidatableObject.Validate"/> 里。
/// 嵌套配置由容器类显式调用 <see cref="ConfigValidation.Check"/> 递归校验。
/// </para>
/// </summary>
public sealed class TicketConfig : IValidatableObject
{
    /// <summary>维护者配置。</summary>
    public DeployerConfig DeployerConfig { get; set; } = new();

    /// <summary>部署服务器配置。键为服务器ID，值为该服务器的专属配置。</summary>
    public Dictionary<ulong, GuildConfig> DeployGuildConfig { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConfigValidation.Check(DeployerConfig, nameof(DeployerConfig)))
        {
            yield return result;
        }

        // if (DeployGuildConfig is null || DeployGuildConfig.Count == 0)
        // {
        //     yield return new ValidationResult("不能为空，至少需要 1 项", [nameof(DeployGuildConfig)]);
        //     yield break;
        // }

        if (DeployGuildConfig is null)
        {
            yield return new ValidationResult("不能为空", [nameof(DeployGuildConfig)]);
            yield break;
        }

        foreach (var (guildId, guildConfig) in DeployGuildConfig)
        {
            if (guildId == 0)
            {
                yield return new ValidationResult("中存在为 0 的服务器ID", [nameof(DeployGuildConfig)]);
            }

            foreach (var result in ConfigValidation.Check(guildConfig, $"{nameof(DeployGuildConfig)}[{guildId}]"))
            {
                yield return result;
            }
        }
    }
}

/// <summary>
/// 部署者鉴权配置(全局唯一，不区分服务器——部署者身份是运维层面的，与具体服务器无关)。
/// </summary>
public sealed class DeployerConfig : IValidatableObject
{
    /// <summary>
    /// 部署者用户ID（鉴权用；具体管控范围在实现该功能时确定）。
    /// </summary>
    public List<ulong> DeployerIds { get; set; } = new();

    /// <summary>维护者用户ID(用于 @ 提及/私信)。</summary>
    [SnowflakeId]
    public ulong MaintainerId { get; set; }

    /// <summary>
    /// 状态频道ID(上线/重连等运行状态通知的发送目标，全局唯一，不区分服务器)。
    /// </summary>
    [SnowflakeId]
    public ulong StatusChannel { get; set; }

    /// <summary>
    /// 是否允许通过 Discord 交互(斜杠命令/按钮)修改本配置文件。
    /// <para>
    /// 默认 <c>false</c>：配置以手工编辑为准，运行期交互不写回文件。
    /// 开启后交互修改走 <see cref="ConfigLoader{T}.TryUpdate"/>（校验通过才原子写回），
    /// 但这也意味着任何能触发这些交互的用户都能改变 Bot 行为，请确保调用侧已做部署者鉴权。
    /// </para>
    /// </summary>
    public bool AllowModifyByInteraction { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConfigValidation.CheckIdList(DeployerIds, nameof(DeployerIds)))
        {
            yield return result;
        }
    }
}

/// <summary>
/// 单个服务器的专属配置。
/// <para>
/// Discord 中 channel 和 role 都必然归属唯一一个服务器（channel → guild、role → guild 都是函数关系），
/// 因此这类配置必须按服务器存放。放到全局会导致：多服务器时工单通知错投到别人的频道、管理员鉴权在多服务器下失效。
/// </para>
/// </summary>
public sealed class GuildConfig : IValidatableObject
{
    /// <summary>
    /// 可以管理工单的管理员身份组ID（角色作用域为本服务器）。
    /// 允许为空（= 该服务器无人可管理工单，相当于关闭管理入口）。
    /// </summary>
    public List<ulong> AllowManageTicketAdminRoleIds { get; set; } = new();

    /// <summary>
    /// 可以提出工单的身份组ID列表（角色作用域为本服务器）。
    /// 允许为空（= 该服务器无人可提出工单，相当于关闭该功能）。
    /// </summary>
    public List<ulong> AllowTicketRoleIds { get; set; } = new();

    /// <summary>投诉入口面板频道ID(入口面板消息发布至此)。</summary>
    [SnowflakeId]
    public ulong EntryPaneChannel { get; set; }

    /// <summary>通知频道ID(新工单通知/跳转链接发送至此)。</summary>
    [SnowflakeId]
    public ulong NotifierChannel { get; set; }

    /// <summary>工单日志频道ID(工单操作记录/日志发送至此)。</summary>
    [SnowflakeId]
    public ulong TicketLogChannel { get; set; }

    /// <summary>工单频道的归属分类ID(新建工单频道时放置于此)。</summary>
    [SnowflakeId]
    public ulong TicketCategory { get; set; }

    /// <summary>
    /// 是否启用本服务器的工单冷却。
    /// <para>
    /// 为 <c>false</c> 时 <see cref="TicketCd"/> 不生效、也不参与校验（可留 0），
    /// 便于测试环境或不需要限流的服务器直接关掉冷却。
    /// </para>
    /// </summary>
    public bool EnableTicketCd { get; set; } = true;

    /// <summary>
    /// 工单冷却时间（小时）：同一用户两次提交工单之间的最小间隔。
    /// 仅在 <see cref="EnableTicketCd"/> 为 <c>true</c> 时生效与校验。
    /// </summary>
    public int TicketCd { get; set; }

    /// <summary>
    /// 待审核工单的自动通过时间（小时）：投诉提交后超过该时长仍无人审核，则自动通过。
    /// 为 <c>0</c> 时表示关闭自动通过（必须由管理员手动审核）。
    /// </summary>
    public int AutoApproveHours { get; set; }

    /// <summary>
    /// 归档时选择“删除频道”后的倒计时（秒）：归档通告发出后等待该时长再删频道，
    /// 让管理组还能看到最后一条通告。是否删除由归档操作当场指定。
    /// </summary>
    public int ArchiveDeleteCountdownSeconds { get; set; } = 60;

    /// <summary>
    /// 入口面板消息ID（由 Bot 发布面板后回写，不对外展示）。
    /// 非 0 时再次发布将原地更新，避免同一频道出现多份入口面板。
    /// </summary>
    public ulong EntryPaneMessageId { get; set; }

    public GuildConfig GetDeepCopy()
    {
        return new GuildConfig
        {
            // List<ulong> 值类型无引用
            AllowManageTicketAdminRoleIds = AllowManageTicketAdminRoleIds.ToList(),
            // List<ulong> 值类型无引用
            AllowTicketRoleIds = AllowTicketRoleIds.ToList(),
            // ulong
            EntryPaneChannel = EntryPaneChannel,
            // ulong
            NotifierChannel = NotifierChannel,
            // ulong
            TicketLogChannel = TicketLogChannel,
            // ulong
            TicketCategory = TicketCategory,
            // bool
            EnableTicketCd = EnableTicketCd,
            // int
            TicketCd = TicketCd,
            // int
            AutoApproveHours = AutoApproveHours,
            // int
            ArchiveDeleteCountdownSeconds = ArchiveDeleteCountdownSeconds,
            // ulong（Bot 回写，编辑器不得清空）
            EntryPaneMessageId = EntryPaneMessageId,
        };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EnableTicketCd && !TicketConfigLimits.IsValidTicketCd(TicketCd))
        {
            yield return new ValidationResult(
                $"必须在 {TicketConfigLimits.TicketCdInclusiveMin} 到 {TicketConfigLimits.TicketCdInclusiveMax} 之间，当前值为 {TicketCd}",
                [nameof(TicketCd)]);
        }

        if (!TicketConfigLimits.IsValidAutoApproveHours(AutoApproveHours))
        {
            yield return new ValidationResult(
                $"必须在 {TicketConfigLimits.AutoApproveHoursInclusiveMin} 到 {TicketConfigLimits.AutoApproveHoursInclusiveMax} 之间（0 表示不自动通过），当前值为 {AutoApproveHours}",
                [nameof(AutoApproveHours)]);
        }

        if (!TicketConfigLimits.IsValidArchiveCountdown(ArchiveDeleteCountdownSeconds))
        {
            yield return new ValidationResult(
                $"删除频道倒计时必须在 {TicketConfigLimits.ArchiveCountdownInclusiveMin} 到 {TicketConfigLimits.ArchiveCountdownInclusiveMax} 秒之间，当前值为 {ArchiveDeleteCountdownSeconds}",
                [nameof(ArchiveDeleteCountdownSeconds)]);
        }

        foreach (var result in ConfigValidation.CheckIdList(AllowManageTicketAdminRoleIds, nameof(AllowManageTicketAdminRoleIds)))
        {
            yield return result;
        }

        foreach (var result in ConfigValidation.CheckIdList(AllowTicketRoleIds, nameof(AllowTicketRoleIds)))
        {
            yield return result;
        }
    }
}
