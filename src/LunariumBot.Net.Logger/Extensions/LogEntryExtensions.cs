using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace LunariumBot.Net.Logger.Extensions
{
    /// <summary>
    /// 为 LogEntry 类型提供序列化为 JSON 的扩展方法。
    /// </summary>
    public static class LogEntryExtensions
    {
        /// <summary>
        /// 将 LogEntry 实例序列化为 JSON 字符串。
        /// 此方法经过特殊配置，可以优雅地处理枚举和 Exception 对象。
        /// </summary>
        /// <param name="logEntry">要序列化的日志条目实例。</param>
        /// <param name="writeIndented">是否格式化输出（带缩进），便于阅读。</param>
        /// <returns>表示该日志条目的 JSON 字符串。</returns>
        public static string ToJson(this LogEntry logEntry, bool writeIndented = false)
        {
            var options = new JsonSerializerOptions
            {
                // 设置为 true，JSON 会带上换行和缩进，非常适合在控制台或文件里查看！
                WriteIndented = writeIndented,

                // 允许编码器不对 CJK 统一汉字、平假名、片假名等字符进行转义
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, 

                // 这里是配置特殊转换器的地方
                Converters = {
                    // 让 LogLevel 枚举显示为 "Information", "Error" 等字符串，而不是数字。
                    new JsonStringEnumConverter(),
                    

                    // 使用自定义的转换器来安全、清晰地序列化 Exception 对象。
                    new ExceptionJsonConverter()
                }
            };

            // 使用配置好的选项来序列化我们的 logEntry 对象
            return JsonSerializer.Serialize(logEntry, options);
        }
    }

    /// <summary>
    /// 自定义的 JsonConverter，用于将 Exception 对象序列化为更简洁、可控的 JSON 格式。
    /// 这可以避免循环引用问题，并只包含我们关心的信息。
    /// </summary>
    internal class ExceptionJsonConverter : JsonConverter<Exception>
    {
        public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // 在日志场景中，通常不需要从 JSON 反序列化回 Exception 对象，
            // 所以这里直接抛出不支持的异常即可。
            throw new NotSupportedException("Deserializing exceptions is not supported.");
        }

        public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
        {
            // 如果异常对象是 null，就写入 null 值
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            // 开始写入一个代表 Exception 的 JSON 对象
            writer.WriteStartObject();

            // 写入异常的类型，例如："System.NullReferenceException"
            writer.WriteString("Type", value.GetType().FullName);
            // 写入异常的核心消息
            writer.WriteString("Message", value.Message);
            // 写入堆栈跟踪信息，这对于调试至关重要！
            writer.WriteString("StackTrace", value.StackTrace);

            // 如果存在内部异常 (InnerException)，我们也需要将它记录下来。
            // 这里我们通过递归调用 Write 方法来实现，可以处理任意深度的内部异常链。
            if (value.InnerException != null)
            {
                writer.WritePropertyName("InnerException");
                Write(writer, value.InnerException, options);
            }

            // 结束写入 JSON 对象
            writer.WriteEndObject();
        }
    }
}