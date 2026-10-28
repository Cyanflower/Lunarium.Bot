namespace Lunarium.Bot.DataCenter.Utils;

/// <summary>
/// PostgreSQL 写入边界的文本清洗。
/// <para>
/// <c>text</c>/<c>varchar</c> 不接受 NUL（0x00，服务端报 <c>22021</c>），<c>jsonb</c> 也不接受
/// <c>\u0000</c>（报 <c>22P05</c>）。Discord 消息正文可以携带 U+0000，因此在转换为 Db 模型时
/// 统一剥除，避免一条脏数据让整批事务回滚。
/// </para>
/// </summary>
public static class PgText
{
    /// <summary>
    /// 移除 NUL 字符。<see langword="null"/> 原样返回；不含 NUL 时返回原实例（不额外分配）。
    /// </summary>
    public static string? Sanitize(string? value) =>
        value is null || value.IndexOf('\0') < 0 ? value : value.Replace("\0", string.Empty);

    /// <summary>
    /// 移除 NUL 字符，<see langword="null"/> 返回空字符串（用于非空列）。
    /// </summary>
    public static string SanitizeOrEmpty(string? value) => Sanitize(value) ?? string.Empty;

    /// <summary>
    /// 判断字符串是否含 NUL。
    /// </summary>
    public static bool ContainsNul(string? value) => value is not null && value.IndexOf('\0') >= 0;

    /// <summary>
    /// 清洗字符串集合（用于 <c>jsonb</c> 列）；<see langword="null"/> 原样返回。
    /// </summary>
    public static List<string>? SanitizeList(List<string>? values) =>
        values?.Select(value => Sanitize(value) ?? string.Empty).ToList();
}
