namespace LunariumBot.Net.Logger;

/// <summary>
/// 封装单个日志输出目标 (Sink) 的相关配置
/// </summary>
public record SinkConfiguration
{
    // 使用 Json 格式输出日志
    public bool ToJson { get; init; } = false;

    /// <summary>
    /// 上下文前缀过滤器:
    /// 包含规则列表。
    /// 只有当日志上下文以列表中的任意一个前缀开头时，才算匹配。
    /// 如果此列表为 null 或为空，则视为匹配所有日志。
    /// </summary>
    public List<string>? ContextFilterIncludes { get; init; }

    /// <summary>
    /// 排除规则列表。
    /// 如果日志上下文以列表中的任意一个前缀开头，则会被明确排除。
    /// </summary>
    public List<string>? ContextFilterExcludes { get; init; }


    /// <summary>
    /// 匹配上下文时是否忽略大小写。
    /// </summary>
    public bool IgnoreFilterCase
    {
        get
        {
            return _ignoreFilterCase;
        }
        init
        {
            _ignoreFilterCase = value;
            ComparisonType = value ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

    }

    /// <summary>
    /// IgnoreFilterCase 后备字段
    /// </summary>
    private readonly bool _ignoreFilterCase = false;

    /// <summary>
    /// <para>用于上下文前缀匹配的字符串比较方式。此属性由 <see cref="IgnoreFilterCase"/> 自动设置，不应直接修改。</para>
    /// <para>当 <see cref="IgnoreFilterCase"/> 为 true 时为 <see cref="StringComparison.OrdinalIgnoreCase"/>，
    /// 否则为 <see cref="StringComparison.Ordinal"/>。</para>
    /// </summary>
    public StringComparison ComparisonType { get; private set; }

    /// <summary>
    /// 允许记录的最低日志级别。
    /// </summary>
    public LogLevel LogMinLevel { get; init; } = LogLevel.Info;

    /// <summary>
    /// 允许记录的最高日志级别。
    /// </summary>
    public LogLevel LogMaxLevel { get; init; } = LogLevel.Critical;
}