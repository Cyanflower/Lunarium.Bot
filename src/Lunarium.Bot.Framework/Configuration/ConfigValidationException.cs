namespace Lunarium.Bot.Framework.Configuration;

/// <summary>
/// 配置校验失败。消息格式固定，保证各 bot 的启动失败日志长一个样。
/// </summary>
public sealed class ConfigValidationException : Exception
{
    public ConfigValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>逐条错误（每条以 <c>- </c> 开头，不含标题行）。</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return "配置文件验证失败，发现以下问题："
            + Environment.NewLine
            + string.Join(Environment.NewLine, errors);
    }
}
