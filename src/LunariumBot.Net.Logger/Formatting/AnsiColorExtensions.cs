namespace LunariumBot.Net.Logger.Formatting;

/// <summary>
/// 提供用于处理 ANSI 颜色代码的扩展方法。
/// 主要用于在支持 ANSI 转义序列的终端中为字符串添加颜色。
/// </summary>
public static class AnsiColorExtensions
{
    // ANSI 指令的前缀
    private const string AnsiPrefix = "\x1b["; // \x1b 是 ESC 字符的十六进制表示
    // 重置所有颜色和样式的指令
    private const string AnsiReset = "\x1b[0m";

    /// <summary>
    /// (扩展方法) 使用指定的颜色包裹字符串，为其添加 ANSI 颜色代码。
    /// </summary>
    /// <param name="text">要添加颜色的原始字符串。</param>
    /// <param name="color">要应用的 ConsoleColor 枚举值。</param>
    /// <returns>一个带有 ANSI 颜色代码的新字符串。在兼容的终端中会显示为彩色。</returns>
    public static string Color(this string text, ConsoleColor color)
    {
        var colorCode = MapConsoleColorToAnsiForeground(color);
        // 格式: [颜色指令]文本[重置指令]
        return $"{AnsiPrefix}{colorCode}m{text}{AnsiReset}";
    }

    public static string Color(this string text, ConsoleColor foreground, ConsoleColor background)
    {
        var foregroundCode = MapConsoleColorToAnsiForeground(foreground);
        var backgroundCode = MapConsoleColorToAnsiBackground(background);
        // ANSI标准允许用分号组合多个代码
        return $"{AnsiPrefix}{foregroundCode};{backgroundCode}m{text}{AnsiReset}";
    }

    /// <summary>
    /// 将 ConsoleColor 枚举值映射到对应的 ANSI 前景色代码。
    /// </summary>
    /// <param name="color">要转换的 ConsoleColor。</param>
    /// <returns>表示 ANSI 颜色代码的字符串。</returns>
    private static string MapConsoleColorToAnsiForeground(ConsoleColor color)
    {
        return color switch
        {
            // 这是标准的前景色代码
            ConsoleColor.Black => "30",
            ConsoleColor.DarkRed => "31",
            ConsoleColor.DarkGreen => "32",
            ConsoleColor.DarkYellow => "33",
            ConsoleColor.DarkBlue => "34",
            ConsoleColor.DarkMagenta => "35",
            ConsoleColor.DarkCyan => "36",
            ConsoleColor.Gray => "37",
            ConsoleColor.DarkGray => "90",
            ConsoleColor.Red => "91",
            ConsoleColor.Green => "92",
            ConsoleColor.Yellow => "93",
            ConsoleColor.Blue => "94",
            ConsoleColor.Magenta => "95",
            ConsoleColor.Cyan => "96",
            ConsoleColor.White => "97",
            _ => "37" // 默认灰色
        };
    }
    // 背景色映射方法 (背景色代码通常是前景色+10)
    private static string MapConsoleColorToAnsiBackground(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => "40",
            ConsoleColor.DarkRed => "41",
            ConsoleColor.DarkGreen => "42",
            ConsoleColor.DarkYellow => "43",
            ConsoleColor.DarkBlue => "44",
            ConsoleColor.DarkMagenta => "45",
            ConsoleColor.DarkCyan => "46",
            ConsoleColor.Gray => "47",
            ConsoleColor.DarkGray => "100",
            ConsoleColor.Red => "101",
            ConsoleColor.Green => "102",
            ConsoleColor.Yellow => "103",
            ConsoleColor.Blue => "104",
            ConsoleColor.Magenta => "105",
            ConsoleColor.Cyan => "106",
            ConsoleColor.White => "107",
            _ => "40" // 默认黑色背景
        };
    }
}