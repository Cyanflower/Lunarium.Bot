using System.ComponentModel.DataAnnotations;

namespace Lunarium.Bot.Framework.Configuration;

/// <summary>
/// 把配置对象（含嵌套的配置类）的校验结果整理成统一格式。
/// <para>
/// 简单规则写在模型的特性上（如 <see cref="SnowflakeIdAttribute"/>），
/// 条件规则/跨字段规则写在模型的 <see cref="IValidatableObject.Validate"/> 里；
/// 本类型只负责"调用标准校验器 + 递归嵌套 + 拼出可读的错误行"。
/// </para>
/// </summary>
public static class ConfigValidation
{
    /// <summary>
    /// 校验整份配置，返回形如 <c>- DeployerConfig.StatusChannel 必须是非0的雪花ID。</c> 的错误行。
    /// 空列表表示通过。
    /// </summary>
    public static IReadOnlyList<string> Validate(object config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            config,
            new ValidationContext(config),
            results,
            validateAllProperties: true);

        return results.Select(Format).ToList();
    }

    /// <summary>
    /// 校验一个嵌套配置对象，并把字段名加上前缀，供容器类的 <see cref="IValidatableObject.Validate"/> 调用。
    /// <para>
    /// 示例：<c>foreach (var r in ConfigValidation.Check(DeployerConfig, nameof(DeployerConfig))) yield return r;</c>
    /// </para>
    /// </summary>
    public static IEnumerable<ValidationResult> Check(object? config, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (config is null)
        {
            yield return new ValidationResult("不能为 null", [path]);
            yield break;
        }

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            config,
            new ValidationContext(config),
            results,
            validateAllProperties: true);

        foreach (var result in results)
        {
            var members = result.MemberNames.Any()
                ? result.MemberNames.Select(member => $"{path}.{member}")
                : [path];

            yield return new ValidationResult(result.ErrorMessage, members);
        }
    }

    /// <summary>
    /// 校验雪花 ID 列表：允许为空（= 功能对所有人关闭），但元素不得为 0。
    /// </summary>
    public static IEnumerable<ValidationResult> CheckIdList(IReadOnlyList<ulong>? ids, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (ids is null)
        {
            yield return new ValidationResult("不能为 null", [memberName]);
            yield break;
        }

        for (var index = 0; index < ids.Count; index++)
        {
            if (ids[index] == 0)
            {
                yield return new ValidationResult($"第 {index} 项必须是非0的雪花ID", [memberName]);
            }
        }
    }

    private static string Format(ValidationResult result)
    {
        var message = result.ErrorMessage ?? "校验失败";
        if (!message.EndsWith('。'))
        {
            message += "。";
        }

        var path = string.Join(", ", result.MemberNames);

        return path.Length == 0
            ? $"- {message}"
            : $"- {path} {message}";
    }
}
