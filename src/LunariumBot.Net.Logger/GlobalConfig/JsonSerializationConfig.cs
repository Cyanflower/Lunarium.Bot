// src/LunariumBot.Net.Logger/GlobalConfig/JsonSerializationConfig.cs
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace LunariumBot.Net.Logger.GlobalConfig;

/// <summary>
/// JSON 序列化配置类，用于全局配置日志中 JSON 序列化行为
/// </summary>
internal static class JsonSerializationConfig
{
    private static JsonSerializerOptions? _options;
    private static readonly object _lock = new();

    /// <summary>
    /// 是否保持中文字符不转义（默认：true）
    /// </summary>
    internal static bool PreserveChineseCharacters { get; private set; } = true;

    /// <summary>
    /// 是否使用缩进格式输出（默认：false，即单行输出）
    /// </summary>
    internal static bool WriteIndented { get; private set; } = false;

    /// <summary>
    /// 获取全局 JSON 序列化选项
    /// </summary>
    internal static JsonSerializerOptions Options
    {
        get
        {
            if (_options == null)
            {
                lock (_lock)
                {
                    _options ??= CreateOptions();
                }
            }
            return _options;
        }
    }

    /// <summary>
    /// 配置是否保持中文字符不转义
    /// </summary>
    /// <param name="preserve">true 保持中文不转义，false 转义为 Unicode</param>
    internal static void ConfigPreserveChineseCharacters(bool preserve)
    {
        PreserveChineseCharacters = preserve;
        ResetOptions();
    }

    /// <summary>
    /// 配置是否使用缩进格式
    /// </summary>
    /// <param name="indented">true 使用缩进（多行），false 单行输出</param>
    internal static void ConfigWriteIndented(bool indented)
    {
        WriteIndented = indented;
        ResetOptions();
    }

    /// <summary>
    /// 重置选项以应用新配置
    /// </summary>
    private static void ResetOptions()
    {
        lock (_lock)
        {
            _options = null;
        }
    }

    /// <summary>
    /// 创建 JSON 序列化选项
    /// </summary>
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = WriteIndented,
            // 默认情况下处理循环引用
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            // 默认转换器设置
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        // 配置编码器以支持中文
        if (PreserveChineseCharacters)
        {
            options.Encoder = JavaScriptEncoder.Create(
                UnicodeRanges.BasicLatin,
                UnicodeRanges.CjkUnifiedIdeographs,           // 中日韩统一表意文字
                UnicodeRanges.CjkUnifiedIdeographsExtensionA, // CJK 扩展 A
                UnicodeRanges.CjkCompatibilityIdeographs,     // CJK 兼容汉字
                UnicodeRanges.CjkSymbolsandPunctuation        // CJK 符号和标点
            );
        }
        else
        {
            // 使用默认编码器（会转义非 ASCII 字符）
            options.Encoder = JavaScriptEncoder.Default;
        }

        return options;
    }
}