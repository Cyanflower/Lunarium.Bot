using System.ComponentModel.DataAnnotations;

using Lunarium.Bot.Framework.Configuration;

namespace Lunarium.Bot.Lum.Model;

/// <summary>
/// Lum 机器人的完整配置（对应 appsettings.json）。
/// <para>
/// 校验规则直接写在模型上：简单规则用特性（如 <see cref="SnowflakeIdAttribute"/>），
/// 条件/跨字段规则写在各类的 <see cref="IValidatableObject.Validate"/> 里。
/// 嵌套配置由容器类显式调用 <see cref="ConfigValidation.Check"/> 递归校验。
/// </para>
/// </summary>
public sealed class LumConfig : IValidatableObject
{
    /// <summary>部署者配置。</summary>
    public DeployerConfig DeployerConfig { get; set; } = new();

    /// <summary>部署服务器配置。键为服务器ID，值为该服务器的专属配置。</summary>
    public Dictionary<ulong, GuildConfig> DeployGuildConfig { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConfigValidation.Check(DeployerConfig, nameof(DeployerConfig)))
        {
            yield return result;
        }

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
/// 部署者鉴权配置（全局唯一，不区分服务器——部署者身份是运维层面的，与具体服务器无关）。
/// </summary>
public sealed class DeployerConfig : IValidatableObject
{
    /// <summary>
    /// 部署者用户ID列表（鉴权用；具体管控范围在实现该功能时确定）。
    /// </summary>
    public List<ulong> DeployerIds { get; set; } = new();

    /// <summary>维护者用户ID（用于 @ 提及/私信）。</summary>
    [SnowflakeId]
    public ulong MaintainerId { get; set; }

    /// <summary>状态频道ID（上线/重连等运行状态通知的发送目标）。</summary>
    [SnowflakeId]
    public ulong StatusChannel { get; set; }

    /// <summary>
    /// 是否允许通过 Discord 交互（斜杠命令/按钮）修改本配置文件。
    /// <para>
    /// 默认 <c>false</c>：配置以手工编辑为准，运行期交互不写回文件。
    /// 开启后交互修改走 <see cref="ConfigLoader{T}.TryUpdate"/>（校验通过才原子写回），
    /// 调用侧仍需自行做部署者鉴权。
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
/// channel / role 都必然归属唯一一个服务器，因此这类配置必须按服务器存放。
/// </para>
/// </summary>
public sealed class GuildConfig : IValidatableObject
{
    /// <summary>
    /// 管理组身份组ID（服务器作用域）。允许为空。
    /// </summary>
    public List<ulong> StaffIds { get; set; } = new();

    /// <summary>
    /// 出入自动化配置；为 <see langword="null"/> 表示该服务器不启用。
    /// </summary>
    public AutoWelcomeConfig? AutoWelcome { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConfigValidation.CheckIdList(StaffIds, nameof(StaffIds)))
        {
            yield return result;
        }

        if (AutoWelcome is not null)
        {
            foreach (var result in ConfigValidation.Check(AutoWelcome, nameof(AutoWelcome)))
            {
                yield return result;
            }
        }
    }
}

/// <summary>
/// 出入自动化（入群欢迎 / 自动分配身份组 / 离群通知）。
/// </summary>
public sealed class AutoWelcomeConfig
{
    /// <summary>欢迎消息发送的频道ID（必填）。</summary>
    [SnowflakeId]
    public ulong WelcomeChannelId { get; set; }

    /// <summary>入群时自动分配的身份组ID；为 <c>0</c> 表示不分配。</summary>
    public ulong AutoDistributeRoleId { get; set; }

    /// <summary>离群通知发送的频道ID；为 <c>0</c> 表示不通知。</summary>
    public ulong LeaveNotificationChannelId { get; set; }
}
