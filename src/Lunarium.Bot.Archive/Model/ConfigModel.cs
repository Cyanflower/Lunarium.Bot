using System.ComponentModel.DataAnnotations;

using Lunarium.Bot.Framework.Configuration;

namespace Lunarium.Bot.Archive.Model;

/// <summary>
/// Archive 机器人的完整配置（对应 <c>appsettings.json</c>）。
/// <para>
/// 字段规则直接写在模型上：简单规则用特性（如 <see cref="SnowflakeIdAttribute"/>），
/// 条件/跨字段规则写在各类的 <see cref="IValidatableObject.Validate"/> 里。
/// 嵌套配置由容器类显式调用 <see cref="ConfigValidation.Check"/> 递归校验。
/// </para>
/// </summary>
public sealed class ArchiveConfig : IValidatableObject
{
    /// <summary>部署者配置（运维层面，全局唯一）。</summary>
    public DeployerConfig DeployerConfig { get; set; } = new();

    /// <summary>
    /// 部署服务器配置。键为服务器ID，值为该服务器的归档配置。
    /// </summary>
    public Dictionary<ulong, GuildConfig> DeployGuildConfig { get; set; } = new();

    /// <summary>归档任务的执行间隔（分钟）。</summary>
    public int IntervalMin { get; set; } = ArchiveConfigLimits.DefaultIntervalMin;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConfigValidation.Check(DeployerConfig, nameof(DeployerConfig)))
        {
            yield return result;
        }

        if (DeployGuildConfig is null)
        {
            yield return new ValidationResult("不能为空", [nameof(DeployGuildConfig)]);
        }
        else
        {
            foreach (var (guildId, guildConfig) in DeployGuildConfig)
            {
                if (guildId == 0)
                {
                    yield return new ValidationResult("中存在为 0 的服务器ID", [nameof(DeployGuildConfig)]);
                }

                foreach (var result in ConfigValidation.Check(
                             guildConfig,
                             $"{nameof(DeployGuildConfig)}[{guildId}]"))
                {
                    yield return result;
                }
            }
        }

        if (!ArchiveConfigLimits.IsValidInterval(IntervalMin))
        {
            yield return new ValidationResult(
                $"必须在 {ArchiveConfigLimits.IntervalInclusiveMin} 到 {ArchiveConfigLimits.IntervalInclusiveMax} 分钟之间，当前值为 {IntervalMin}",
                [nameof(IntervalMin)]);
        }
    }
}

/// <summary>
/// 单个部署服务器的归档配置。
/// </summary>
public sealed class GuildConfig : IValidatableObject
{
    /// <summary>
    /// 允许保留的活跃帖子数。归档时按活跃度从低到高归档，直到不超过该值。
    /// </summary>
    public int ArchiveLimit { get; set; } = 970;

    /// <summary>
    /// 允许通过 <c>/维护 编辑配置</c> 修改本服务器归档配置的身份组ID。
    /// <para>
    /// 空列表表示只有部署者 / 维护者可以操作。
    /// </para>
    /// </summary>
    public List<ulong> StaffRoleIds { get; set; } = new();

    /// <summary>复制一份可安全编辑的配置快照。</summary>
    public GuildConfig GetDeepCopy()
    {
        return new GuildConfig
        {
            ArchiveLimit = ArchiveLimit,
            StaffRoleIds = (StaffRoleIds ?? []).ToList(),
        };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ArchiveConfigLimits.IsValidLimit(ArchiveLimit))
        {
            yield return new ValidationResult(
                $"必须在 {ArchiveConfigLimits.LimitInclusiveMin} 到 {ArchiveConfigLimits.LimitExclusiveMax - 1} 之间，当前值为 {ArchiveLimit}",
                [nameof(ArchiveLimit)]);
        }

        foreach (var result in ConfigValidation.CheckIdList(
                     StaffRoleIds,
                     nameof(StaffRoleIds)))
        {
            yield return result;
        }
    }
}

/// <summary>
/// 部署者鉴权配置（全局唯一，不区分服务器——部署者身份是运维层面的，与具体服务器无关）。
/// </summary>
public sealed class DeployerConfig : IValidatableObject
{
    /// <summary>
    /// 部署者用户ID。默认拥有跨服务器的管理权限（鉴权时作为 <c>MaintainerBypass</c> 使用）。
    /// </summary>
    public List<ulong> DeployerIds { get; set; } = new();

    /// <summary>维护者用户ID（用于 @ 提及 / 私信）。</summary>
    [SnowflakeId]
    public ulong MaintainerId { get; set; }

    /// <summary>状态/通知频道ID（上线、重连、归档报告等运行状态消息的发送目标）。</summary>
    [SnowflakeId]
    public ulong StatusChannelId { get; set; }

    /// <summary>
    /// 是否允许通过 Discord 交互（斜杠命令/按钮）修改本配置文件。
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
