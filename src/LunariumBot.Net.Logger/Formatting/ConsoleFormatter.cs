
using System.Text;
using System.Text.RegularExpressions;
using LunariumBot.Net.Logger.Extensions;

namespace LunariumBot.Net.Logger.Formatting;

/// <summary>
/// 负责将 LogEntry 格式化为带 ANSI 颜色的字符串，专为 ConsoleSink 使用。
/// </summary>
internal static class ConsoleFormatter
{
    // 定义“主题”：为不同语义和类型的元素指定颜色
    private static readonly string prefix = "[".Color(ConsoleColor.DarkGray);
    private static readonly string suffix = "]".Color(ConsoleColor.DarkGray);
    private const ConsoleColor TimestampColor = ConsoleColor.Green;
    private const ConsoleColor LevelDebugColor = ConsoleColor.DarkGray;
    private const ConsoleColor LevelInfoColor = ConsoleColor.Green;
    private const ConsoleColor LevelWarningColor = ConsoleColor.Yellow;
    private const ConsoleColor LevelErrorColor = ConsoleColor.Red;
    private const ConsoleColor LevelCriticalBgColor = ConsoleColor.DarkRed;
    private const ConsoleColor ContextColor = ConsoleColor.Cyan;
    private const ConsoleColor ExceptionColor = ConsoleColor.Red;

    // 值类型颜色
    private const ConsoleColor StringColor = ConsoleColor.Magenta;
    private const ConsoleColor NumberColor = ConsoleColor.Yellow;
    private const ConsoleColor BooleanColor = ConsoleColor.Blue;
    private const ConsoleColor NullColor = ConsoleColor.DarkBlue;
    private const ConsoleColor OtherColor = ConsoleColor.Gray; // 其他未知类型

    // JSON 专用
    private const ConsoleColor JsonKeyColor = ConsoleColor.DarkCyan;

    private static readonly Regex _propertyRegex = new(@"\{([a-zA-Z_.()][a-zA-Z0-9_.()]*)(?::([^}]+))?\}", RegexOptions.Compiled);

    /// <summary>
    /// 将日志条目格式化为适合控制台阅读的纯文本格式，并应用颜色。
    /// </summary>
    public static string FormatPlainText(LogEntry entry)
    {
        try
        {
            var sb = new StringBuilder();

            // [Timestamp]
            sb.Append(prefix);
            sb.Append($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}".Color(TimestampColor));
            sb.Append(suffix);
            sb.Append(' ');

            // [Level]
            sb.Append(prefix);
            sb.Append($"{GetLogLevelString(entry.Level)}");
            sb.Append(suffix);
            sb.Append(' ');

            // [Context]
            if (!string.IsNullOrEmpty(entry.Context))
            {
                sb.Append(prefix);
                sb.Append($"{entry.Context.Color(ContextColor)}");
                sb.Append(suffix);
                sb.Append(' ');
            }
            
            // 渲染消息模板
            var renderedMessage = _propertyRegex.Replace(entry.MessageTemplate, match =>
            {
                // 捕获组 1 是属性名
                var key = match.Groups[1].Value;
                // 捕獲組 2 是格式化字符串 (可能为空)
                var format = match.Groups[2].Success ? match.Groups[2].Value : null;

                if (entry.Properties.TryGetValue(key, out var value))
                {
                    // 把值和格式化字符串都传给 ColorizeValue
                    return ColorizeValue(value, format);
                }

                // 找不到属性则保留原样
                return match.Value;
            });

            sb.Append(renderedMessage);

            // 附加异常信息
            if (entry.Exception != null)
            {
                // 换行
                sb.AppendLine();
                sb.Append(entry.Exception.ToString().Color(ExceptionColor));
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FormatPlainText Ex:\n{ex}".Color(ConsoleColor.Red));
            return "";
        }
    }

    /// <summary>
    /// 将日志条目格式化为 JSON，并对 Key 和不同类型的值进行染色。
    /// </summary>
    public static string FormatJson(LogEntry entry, bool indented = false)
    {
        try
        {
            // 先生成无色的标准 JSON 字符串
            string json = entry.ToJson(indented);

            // 使用正则表达式为不同部分上色 
            // 染色 Key - 支持转义引号 \"
            // ((?:\\"|[^"])*) 解释: 这是一个非捕获组，匹配一个转义的引号 或 一个不是引号的字符，整个组重复任意次
            json = Regex.Replace(json, @"""((?:\\""|[^""])*)""\s*:", m => $"\"{m.Groups[1].Value.Color(JsonKeyColor)}\":");

            // 染色 String Values - 支持转义引号 \"，并且冒号后可以有0或多个空格
            json = Regex.Replace(json, @":\s*""((?:\\""|[^""])*)""", m => $": \"{m.Groups[1].Value.Color(StringColor)}\"");
            
            // 染色 Number Values - 支持科学记数法，并且冒号后可以有0或多个空格
            // (-? BLAH BLAH) 增加了对 e/E 的支持
            json = Regex.Replace(json, @":\s*(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)", m => $": {m.Groups[1].Value.Color(NumberColor)}");
            
            // 染色 Boolean Values - 冒号后可以有0或多个空格
            json = Regex.Replace(json, @":\s*(true|false)", m => $": {m.Groups[1].Value.Color(BooleanColor)}");
            
            // 染色 Null Values - 冒号后可以有0或多个空格
            json = Regex.Replace(json, @":\s*(null)", m => $": {m.Groups[1].Value.Color(NullColor)}");


            return json;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FormatPlainText Ex:\n{ex}".Color(ConsoleColor.Red));
            return "";
        }
    }

    private static string GetLogLevelString(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => "DBG".Color(LevelDebugColor),
            LogLevel.Info => "INF".Color(LevelInfoColor),
            LogLevel.Warning => "WRN".Color(LevelWarningColor),
            LogLevel.Error => "ERR".Color(LevelErrorColor),
            LogLevel.Critical => "CRT".Color(ConsoleColor.White, LevelCriticalBgColor),
            _ => "UNK".Color(ConsoleColor.White, ConsoleColor.Yellow)
        };
    }

    private static string ColorizeValue(object? value, string? format)
    {
        // 1. 先根据值的原始类型确定颜色
        ConsoleColor color;
        if (value is null) color = NullColor;
        else if (value is string) color = StringColor;
        else if (value is bool) color = BooleanColor;
        else if (value is short or int or long or ulong or float or double or decimal) color = NumberColor;
        else color = OtherColor;

        // 2. 然后根据 format 参数来生成最终的字符串
        var formattedString = GetFormattedString(value, format);
        
        // 3. 最后，用第一步确定的颜色来包裹第二步生成的字符串
        return formattedString.Color(color);
    }
    
    /// <summary>
    /// 辅助方法，用于处理值的格式化。
    /// </summary>
    private static string GetFormattedString(object? value, string? format)
    {
        if (value is null)
        {
            return "null";
        }

        // 如果提供了格式化字符串，并且值实现了 IFormattable 接口 (大部分基础类型都实现了)
        if (!string.IsNullOrEmpty(format) && value is IFormattable formattableValue)
        {
            try
            {
                // 使用指定的格式
                return formattableValue.ToString(format, null);
            }
            catch (FormatException)
            {
                // 如果格式化失败，就回退到默认的 ToString()
                return value.ToString() ?? "";
            }
        }
        
        // 否则，直接用默认的 ToString()
        return value.ToString() ?? "";
    }
}