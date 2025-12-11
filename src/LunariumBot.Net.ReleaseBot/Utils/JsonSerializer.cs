using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace LunariumBot.Net.ReleaseBot.Utils;

public static class JsonSerializerConfig
{
    
    public static JsonSerializerOptions Options { get; }
    public static JsonSerializerOptions OptionsWithIndented { get; }
    static JsonSerializerConfig()
    {
        Options = new JsonSerializerOptions()
        {
            WriteIndented = false,
            // 允许的 Unicode 范围
            Encoder = JavaScriptEncoder.Create(
                UnicodeRanges.BasicLatin,
                UnicodeRanges.CjkUnifiedIdeographs,           // 中日韩统一表意文字
                UnicodeRanges.CjkUnifiedIdeographsExtensionA, // CJK 扩展 A
                UnicodeRanges.CjkCompatibilityIdeographs,     // CJK 兼容汉字
                UnicodeRanges.CjkSymbolsandPunctuation        // CJK 符号和标点
            )
        };
        OptionsWithIndented = new JsonSerializerOptions()
        {
            WriteIndented = true,
            // 允许的 Unicode 范围
            Encoder = JavaScriptEncoder.Create(
                UnicodeRanges.BasicLatin,
                UnicodeRanges.CjkUnifiedIdeographs,           // 中日韩统一表意文字
                UnicodeRanges.CjkUnifiedIdeographsExtensionA, // CJK 扩展 A
                UnicodeRanges.CjkCompatibilityIdeographs,     // CJK 兼容汉字
                UnicodeRanges.CjkSymbolsandPunctuation        // CJK 符号和标点
            )
        };
    }
}