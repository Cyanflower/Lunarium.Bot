using System.Text;
using System.Text.RegularExpressions;
using LunariumBot.Net.Logger.Extensions;

namespace LunariumBot.Net.Logger.Formatting;

/// <summary>
/// 负责将 LogEntry 格式化为适合写入文件的字符串。
/// </summary>
internal static class FileFormatter
{
    private static readonly Regex _propertyRegex = new(@"\{([a-zA-Z_.][a-zA-Z0-9_.]*)\}", RegexOptions.Compiled);
    
    /// <summary>
    /// 将日志条目格式化为适合文件存储的字符串。
    /// </summary>
    public static string Format(LogEntry entry, bool toJson, bool indented)
    {
        try
        {
            if (toJson)
            {
                // 如果是 JSON 格式，直接调用扩展方法
                return entry.ToJson(indented);
            }

            // 否则，格式化为纯文本
            return FormatPlainText(entry);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FormatPlainText Ex:\n{ex}".Color(ConsoleColor.Red));
            return "";
        }
    }
    
    private static string FormatPlainText(LogEntry entry)
    {
        // 渲染消息模板
        var renderedMessage = _propertyRegex.Replace(entry.MessageTemplate, match =>
        {
            var key = match.Groups[1].Value;
            if (entry.Properties.TryGetValue(key, out var value))
            {
                // 文件中我们不需要颜色，直接返回值
                return value?.ToString() ?? "null";
            }
            return match.Value;
        });

        // 映射 Level 短名称
        var levelString = entry.Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "UNK"
        };
        
        // 构建日志消息
        var messageBuilder = new StringBuilder();
        messageBuilder.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{levelString}] [{entry.LoggerName}] [{entry.Context}] {renderedMessage}");
        
        if (entry.Exception != null)
        {
            messageBuilder.AppendLine(); // 换行
            messageBuilder.Append(entry.Exception); // 附加完整的异常信息
        }

        return messageBuilder.ToString();
    }
}