using System.ComponentModel.DataAnnotations;

using Lunarium.Bot.Framework.Configuration;

namespace Lunarium.Bot.Template.Model;

/// <summary>
/// 完整配置（对应 appsettings.json）。
/// <para>
/// 校验规则写在模型上：单字段用特性（如 <see cref="SnowflakeIdAttribute"/>），
/// 跨字段/条件规则写在各类的 <see cref="IValidatableObject.Validate"/>；
/// 嵌套配置由容器显式调用 <see cref="ConfigValidation.Check"/> 递归校验。
/// 启动时校验失败会导致进程直接退出，错误文案要能直接回显给运维。
/// </para>
/// </summary>
public sealed class TemplateConfig : IValidatableObject
{
    /// <summary>部署者配置（全局唯一）。</summary>
    public DeployerConfig DeployerConfig { get; set; } = new();

    /// <summary>按服务器存放的配置。键为服务器 ID。</summary>
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
/// 部署者鉴权配置。部署者身份是运维层面的，与具体服务器无关。
/// </summary>
public sealed class DeployerConfig : IValidatableObject
{
    /// <summary>部署者用户 ID 列表。</summary>
    public List<ulong> DeployerIds { get; set; } = new();

    /// <summary>维护者用户 ID（用于 @ 提及 / 私信）。</summary>
    [SnowflakeId]
    public ulong MaintainerId { get; set; }

    /// <summary>状态频道 ID（上线 / 重连通知的发送目标）。</summary>
    [SnowflakeId]
    public ulong StatusChannel { get; set; }

    /// <summary>
    /// 是否允许通过 Discord 交互修改配置。默认关闭：配置以手工编辑为准。
    /// 开启后必须走 <see cref="ConfigLoader{T}.TryUpdate"/>，且调用侧自行做部署者鉴权。
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
/// 单个服务器的专属配置。频道 / 身份组都归属唯一服务器，因此必须按服务器存放。
/// </summary>
public sealed class GuildConfig : IValidatableObject
{
    /// <summary>管理组身份组 ID（服务器作用域）。允许为空。</summary>
    public List<ulong> StaffIds { get; set; } = new();

    // 业务配置在此扩展，例如：
    // public MyFeatureConfig? MyFeature { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in ConfigValidation.CheckIdList(StaffIds, nameof(StaffIds)))
        {
            yield return result;
        }
    }
}
