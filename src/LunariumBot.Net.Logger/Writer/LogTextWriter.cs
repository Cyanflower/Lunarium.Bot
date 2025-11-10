using System.Text;
using System.Text.Json;
using LunariumBot.Net.Logger.Parser;

namespace LunariumBot.Net.Logger.Writer;

internal class LogTextWriter : LogWriter
{
    // ============= 池化管理 API =============
    protected override void ReturnToPool() => WriterPool.Return(this);

    #region 公共 API
    // =============== 公共 API ===============
    protected override LogTextWriter WriteTimestamp(DateTimeOffset timestamp)
    {
        switch (TimestampFormatConfig.TextMode)
        {
            case TextTimestampMode.Unix:
                _stringBuilder.Append($"[{timestamp.ToUnixTimeSeconds()}] ");
                break;
            case TextTimestampMode.UnixMs:
                _stringBuilder.Append($"[{timestamp.ToUnixTimeMilliseconds()}] ");
                break;
            case TextTimestampMode.ISO8601:
                _stringBuilder.Append($"[{timestamp:O}] ");
                break;
            case TextTimestampMode.Custom:
                _stringBuilder.Append('[');
                _stringBuilder.AppendFormat($"{{0:{TimestampFormatConfig.TextCustomFormat}}}", timestamp);
                _stringBuilder.Append("] ");
                break;
        }
        return this;
    }

    protected override LogTextWriter WriteLevel(LogLevel level)
    {
        var levelStr = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "UNK"
        };
        _stringBuilder.Append($"[{levelStr}] ");
        return this;
    }

    protected override LogTextWriter WriteContext(string? context)
    {
        if (!string.IsNullOrEmpty(context))
            _stringBuilder.Append($"[{context}] ");
        return this;
    }

    protected override LogTextWriter WriteRenderedMessage(MessageTemplateTokens[] tokens, object?[] propertys)
    {
        int i = 0;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken textToken:
                    _stringBuilder.Append(textToken.Text);
                    break;
                case PropertyToken propertyToken:
                    if (propertys.Length == 0)
                    {
                        _stringBuilder.Append(propertyToken.RawText.Text);
                    }
                    else
                    {
                        RenderPropertyToken(propertyToken, propertys, i);
                        i++;
                    }
                    break;
            }
        }
        return this;
    }

    protected override LogTextWriter WriteException(Exception? exception)
    {
        if (exception != null)
        {
            _stringBuilder.AppendLine();
            _stringBuilder.Append(exception);
        }
        return this;
    }

    // ========================================
    #endregion



    #region 辅助方法
    // ================ 辅助方法 ================

    private void RenderPropertyToken(PropertyToken propertyToken, object?[] propertys, int i)
    {
        try
        {
            // 获取要渲染的值
            object? value = GetPropertyValue(propertyToken, propertys, i, out bool found);

            if (!found)
            {
                // 如果找不到对应的参数，输出原始文本
                _stringBuilder.Append(propertyToken.RawText.Text);
                return;
            }

            if (value is null)
            {
                _stringBuilder.Append("null");
                return;
            }
            // 当具有解构标识或设置了默认解构(且是集合类型)时, 尝试解构对象且跳过对齐和格式化(对json格式无意义)
            if (propertyToken.Destructuring == Destructuring.Destructure || (DestructuringConfig.AutoDestructureCollections && IsCommonCollectionType(value)))
            {
                // 当遇到 {@...} 时，使用 JsonSerializer 把它变成一个易读的 JSON 字符串
                try
                {
                    var jsonString = JsonSerializer.Serialize(value, JsonSerializationConfig.Options);
                    _stringBuilder.Append(jsonString); 
                }
                catch
                {
                    // 无法序列化, 输出标量
                    _stringBuilder.Append(value is not null ? value.ToString() : "null");
                }
                return; // 处理完就返回, 跳过构建格式字符串
            }

            // 构建格式字符串，支持对齐和格式化
            string formatString = BuildFormatString(propertyToken.Alignment, propertyToken.Format);
            _stringBuilder.AppendFormat(formatString, value);
        }
        catch (Exception ex)
        {
            _stringBuilder.Append(propertyToken.RawText.Text);
            InternalLogger.Error(ex, $"LogWriter WriteValue Failed: {propertyToken.RawText.Text}");
        }
    }

    // 获取属性值
    private object? GetPropertyValue(PropertyToken propertyToken, object?[] propertys, int namedIndex, out bool found)
    {
        // 处理位置参数, 例如: {0}
        if (propertyToken.PropertyType == PropertyType.Positional && 
            int.TryParse(propertyToken.PropertyName.AsSpan(), out int index))
        {
            if (index >= propertys.Length || index < 0)
            {
                found = false;
                return null;
            }
            found = true;
            return propertys[index];
        }
        
        // 处理命名参数
        if (namedIndex >= propertys.Length || namedIndex < 0)
        {
            found = false;
            return null;
        }
        found = true;
        return propertys[namedIndex];
    }

    private static string BuildFormatString(int? alignment, string? format)
    {
        // 如果超长则回退到堆分配
        // yyyy-MM-dd HH:mm:ss.fff zzz 在格式化中较长的时间格式化, 96个字符也足够容纳前面的例子x3了
        if (format is not null && format.Length > 96) return BuildFormatStringByHeap(alignment, format);
        Span<char> buffer = stackalloc char[128];
        int pos = 0;
        
        buffer[pos++] = '{';
        buffer[pos++] = '0';
        
        if (alignment.HasValue)
        {
            buffer[pos++] = ',';
            alignment.Value.TryFormat(buffer[pos..], out int written);
            pos += written;
        }
        
        if (!string.IsNullOrEmpty(format))
        {
            buffer[pos++] = ':';
            format.AsSpan().CopyTo(buffer[pos..]);
            pos += format.Length;
        }
        
        buffer[pos++] = '}';
        return new string(buffer[..pos]);
    }

    // 构建格式字符串: {index,alignment:format}
    private static string BuildFormatStringByHeap(int? alignment, string? format)
    {
        var sb = new StringBuilder("{0");

        // 添加对齐: {0,10} 或 {0,-10}
        if (alignment.HasValue)
        {
            sb.Append(',');
            sb.Append(alignment.Value);
        }

        // 添加格式: {0:D} 或 {0,10:D}
        if (!string.IsNullOrEmpty(format))
        {
            sb.Append(':');
            sb.Append(format);
        }

        sb.Append('}');
        return sb.ToString();
    }
    #endregion
}