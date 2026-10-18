using System.ComponentModel.DataAnnotations;

namespace Lunarium.Bot.Framework.Health;

/// <summary>
/// 健康监控配置（各 bot 共享）。
/// <para>
/// 传输端点属于进程级设置（一个进程只有一个端口、一个上报目标），与具体服务器无关，
/// 因此作为全局配置节，而不是按服务器存放。
/// </para>
/// <para>
/// 监听/上报地址限制在本机回环内，所以只配端口与目标字符串，不配可绑定到 0.0.0.0 的 Host。
/// </para>
/// </summary>
public sealed class HealthMonitorConfig : IValidatableObject
{
    /// <summary>默认配置文件名。运维配置与业务配置分文件存放，避免业务侧写回覆盖运维修改。</summary>
    public const string DefaultFileName = "appsettings.health.json";

    /// <summary>监听端口下界（含）。1024 以下属于特权端口，非 root 绑定会直接失败，因此排除。</summary>
    public const int PortInclusiveMin = 1024;

    /// <summary>监听端口上界（含）。</summary>
    public const int PortInclusiveMax = 65535;

    /// <summary>是否启用本地 HTTP 健康检查服务。</summary>
    public bool EnableHttpServer { get; set; } = false;

    /// <summary>本地 HTTP 健康检查服务监听端口（仅 <see cref="EnableHttpServer"/> 为 true 时生效）。</summary>
    public int HttpPort { get; set; } = 52000;

    /// <summary>是否启用主动心跳上报。</summary>
    public bool EnableHeartbeat { get; set; } = false;

    /// <summary>心跳上报目标（仅 <see cref="EnableHeartbeat"/> 为 true 时生效），形如 "127.0.0.1:50000"。</summary>
    public string HeartbeatTarget { get; set; } = "127.0.0.1:50000";

    /// <summary>
    /// 条件校验：端口/目标只在对应功能启用时才有意义，未启用时填什么都合法。
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EnableHttpServer && HttpPort is < PortInclusiveMin or > PortInclusiveMax)
        {
            yield return new ValidationResult(
                $"必须在 {PortInclusiveMin} 到 {PortInclusiveMax} 之间，当前值为 {HttpPort}",
                [nameof(HttpPort)]);
        }

        if (EnableHeartbeat && string.IsNullOrWhiteSpace(HeartbeatTarget))
        {
            yield return new ValidationResult("不能为空", [nameof(HeartbeatTarget)]);
        }
    }
}
