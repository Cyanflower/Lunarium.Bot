using System.Text.Json;
using System.Globalization;
using LunariumBot.Net.Logger.Parser;

namespace LunariumBot.Net.Logger.Writer;

internal class LogJsonWriter : LogWriter
{
    // ============= 池化管理 API =============
    protected override void ReturnToPool() => WriterPool.Return(this);

    #region 公共 API
    // =============== 公共 API ===============
    // 重写 BeginEntry 输出 JSON 开始符号
    protected override LogWriter BeginEntry()
    {
        _stringBuilder.Append('{');
        return this;
    }

    // 🎣 钩子：在渲染消息前写入 MessageTemplate
    protected override void BeforeRenderMessage(LogEntry logEntry)
    {
        WriteOriginalMessage(logEntry.MessageTemplate._messageTemplateTokens);
    }

    // 🎣 钩子：在渲染消息后写入 PropertyValue
    protected override void AfterRenderMessage(LogEntry logEntry)
    {
        WritePropertyValue(logEntry.MessageTemplate._messageTemplateTokens, logEntry.Properties);
    }

    // 重写 EndEntry 输出 JSON 结束符号
    protected override LogWriter EndEntry()
    {
        _stringBuilder.Append('}');
        return this;
    }

    protected override LogJsonWriter WriteTimestamp(DateTimeOffset timestamp)
    {
        switch (TimestampFormatConfig.JsonMode)
        {
            case JsonTimestampMode.Unix:
                _stringBuilder.Append($"\"Timestamp\":{timestamp.ToUnixTimeSeconds()},");
                break;
            case JsonTimestampMode.UnixMs:
                _stringBuilder.Append($"\"Timestamp\":{timestamp.ToUnixTimeMilliseconds()},");
                break;
            case JsonTimestampMode.ISO8601:
                _stringBuilder.Append("\"Timestamp\":");
                AppendJsonString(timestamp.ToString("O"));
                _stringBuilder.Append(',');
                break;
            case JsonTimestampMode.Custom:
                _stringBuilder.Append("\"Timestamp\":");
                AppendJsonString(timestamp.ToString(TimestampFormatConfig.JsonCustomFormat));
                _stringBuilder.Append(',');
                break;
        }
        return this;
    }

    protected override LogJsonWriter WriteLevel(LogLevel level)
    {
        var levelStr = level switch
        {
            LogLevel.Debug => "Debug",
            LogLevel.Info => "Info",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => "Unknown"
        };
        _stringBuilder.Append($"\"Level\":\"{levelStr}\",");
        _stringBuilder.Append($"\"LogLevel\":{(int)level},");
        return this;
    }

    protected override LogJsonWriter WriteContext(string? context)
    {
        if (!string.IsNullOrEmpty(context))
        {
            _stringBuilder.Append("\"Context\":");
            AppendJsonString(context);
            _stringBuilder.Append(',');
        }
        return this;
    }

    protected LogJsonWriter WriteOriginalMessage(MessageTemplateTokens[] tokens)
    {
        _stringBuilder.Append("\"OriginalMessage\":\"");
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken textToken:
                    AppendJsonStringContent(textToken.Text);
                    break;
                case PropertyToken propertyToken:
                    AppendJsonStringContent(propertyToken.RawText.Text);
                    break;
            }
        }
        _stringBuilder.Append("\",");

        return this;
    }

    protected override LogJsonWriter WriteRenderedMessage(MessageTemplateTokens[] tokens, object?[] propertys)
    {
        _stringBuilder.Append("\"RenderedMessage\":\"");
        int i = 0;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case TextToken textToken:
                    AppendJsonStringContent(textToken.Text);
                    break;
                case PropertyToken propertyToken:
                    if (propertys.Length == 0)
                    {
                        AppendJsonStringContent(propertyToken.RawText.Text);
                    }
                    else
                    {
                        RenderPropertyToken(propertyToken, propertys, i);
                        i++;
                    }
                    break;
            }
        }
        _stringBuilder.Append("\",");
        return this;
    }

    protected LogJsonWriter WritePropertyValue(MessageTemplateTokens[] tokens, object?[] propertys)
    {
        _stringBuilder.Append("\"Propertys\":{");
        if (propertys.Length != 0)
        {
            int iteration = 0;
            foreach (var token in tokens)
            {
                if (token is PropertyToken propertyToken)
                {
                    _stringBuilder.Append('"');
                    AppendJsonStringContent(propertyToken.PropertyName);
                    _stringBuilder.Append("\":");
                    
                    object? value;
                    if (propertyToken.PropertyType == PropertyType.Positional && int.TryParse(propertyToken.PropertyName.AsSpan(), out int index))
                    {
                        if (index >= propertys.Length)
                        {
                            _stringBuilder.Append("null,");
                            continue;
                        }
                        value = propertys[index];
                    }
                    else
                    {
                        if (iteration >= propertys.Length)
                        {
                            _stringBuilder.Append("null,");
                            continue;
                        }
                        value = propertys[iteration];
                    }
                    
                    // 处理解构（Destructuring）
                    if (propertyToken.Destructuring == Destructuring.Destructure || (DestructuringConfig.AutoDestructureCollections && IsCommonCollectionType(value)))
                    {
                        try
                        {
                            var valueJson = JsonSerializer.Serialize(value, JsonSerializationConfig.Options);
                            _stringBuilder.Append(valueJson);
                        }
                        catch
                        {
                            ToJsonValue(value);
                        }
                    }
                    else
                    {
                        ToJsonValue(value);
                    }
                    
                    _stringBuilder.Append(',');
                    iteration++;
                }
            }
            
            // 移除末尾多余的逗号
            if (_stringBuilder.Length > 0 && _stringBuilder[_stringBuilder.Length - 1] == ',')
            {
                _stringBuilder.Remove(_stringBuilder.Length - 1, 1);
            }
        }

        _stringBuilder.Append("},");
        return this;
    }

    protected override LogJsonWriter WriteException(Exception? exception)
    {
        if (exception != null)
        {
            _stringBuilder.Append("\"Exception\":");
            AppendJsonString(exception.ToString());
        }
        else
        {
            // 移除末尾多余的逗号
            if (_stringBuilder.Length > 0 && _stringBuilder[_stringBuilder.Length - 1] == ',')
            {
                _stringBuilder.Remove(_stringBuilder.Length - 1, 1);
            }
        }
        return this;
    }

    // ========================================
    #endregion

    #region 辅助方法
    // ================ 辅助方法 ================
    
    /// <summary>
    /// 将字符串转义并添加 JSON 引号输出到 StringBuilder
    /// 例如：Hello "World" → "Hello \"World\""
    /// </summary>
    /// <param name="value">要转义的字符串</param>
    private void AppendJsonString(string value)
    {
        _stringBuilder.Append('"');
        AppendJsonStringContent(value);
        _stringBuilder.Append('"');
    }

    /// <summary>
    /// 将字符串内容转义后输出（不添加引号）
    /// 处理 JSON 必须转义的字符：" \ 换行 回车 制表符等
    /// 同时处理代理对（Surrogate Pairs）以支持 Emoji 等特殊字符
    /// 中文等 Unicode 字符直接输出，不转义（符合 JSON 标准且减小文件体积）
    /// </summary>
    /// <param name="value">要转义的字符串内容</param>
    private void AppendJsonStringContent(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            // 检查是否是高代理（Emoji 等的第一部分）
            // 代理对（Surrogate Pairs）是 UTF-16 编码中用于表示 Unicode 补充字符的机制
            // 例如 Emoji "😀" 由两个 char 组成：\uD83D\uDE00
            if (char.IsHighSurrogate(c) && i + 1 < value.Length)
            {
                char low = value[i + 1];
                if (char.IsLowSurrogate(low))
                {
                    // 保持代理对完整，直接输出（JSON 标准支持）
                    _stringBuilder.Append(c);
                    _stringBuilder.Append(low);
                    i++; // 跳过低代理
                    continue;
                }
            }

            // 处理必须转义的字符
            switch (c)
            {
                case '"':  // 双引号 - 必须转义，否则会破坏 JSON 结构
                    _stringBuilder.Append("\\\"");
                    break;
                case '\\': // 反斜杠 - 必须转义，否则会与转义序列冲突
                    _stringBuilder.Append("\\\\");
                    break;
                case '\n': // 换行符 - 必须转义，JSON 字符串不能包含实际换行
                    _stringBuilder.Append("\\n");
                    break;
                case '\r': // 回车符 - 必须转义
                    _stringBuilder.Append("\\r");
                    break;
                case '\t': // 制表符 - 必须转义
                    _stringBuilder.Append("\\t");
                    break;
                case '\b': // 退格符 - 必须转义
                    _stringBuilder.Append("\\b");
                    break;
                case '\f': // 换页符 - 必须转义
                    _stringBuilder.Append("\\f");
                    break;
                default:
                    // 控制字符（0x00-0x1F）必须转义为 \uXXXX 格式
                    // 这些字符在 JSON 字符串中不可见且可能导致解析问题
                    if (c < 0x20)
                    {
                        _stringBuilder.Append("\\u");
                        _stringBuilder.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        // 中文、日文、韩文等 Unicode 字符直接输出
                        // JSON 标准（RFC 8259）完全支持 UTF-8 编码的 Unicode 字符
                        // 不转义的优点：
                        // 1. 文件体积更小（中文转义后会变成 \uXXXX，体积增大约 3-6 倍）
                        // 2. 性能更好（无需转义处理）
                        // 3. 直接可读（便于调试和查看日志文件）
                        // 4. 所有主流日志分析工具都完美支持 UTF-8
                        _stringBuilder.Append(c);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 在 RenderedMessage 中渲染属性值（应用格式化）
    /// 此方法用于消息模板渲染，会应用用户指定的格式字符串
    /// </summary>
    private void RenderPropertyToken(PropertyToken propertyToken, object?[] propertys, int i)
    {
        try
        {
            // 处理位置变量, 例如: {0}
            if (propertyToken.PropertyType == PropertyType.Positional && 
                int.TryParse(propertyToken.PropertyName.AsSpan(), out int index))
            {
                if (index >= propertys.Length)
                {
                    AppendJsonStringContent(propertyToken.RawText.Text);
                    return;
                }
                
                if (propertys[index] is null)
                {
                    _stringBuilder.Append("null");
                    return;
                }
                
                // 应用格式化，然后转义输出
                var formatted = string.Format($"{{0:{propertyToken.Format}}}", propertys[index]);
                AppendJsonStringContent(formatted);
            }
            else // 命名参数
            {
                if (i >= propertys.Length)
                {
                    AppendJsonStringContent(propertyToken.RawText.Text);
                    return;
                }
                
                if (propertys[i] is null)
                {
                    _stringBuilder.Append("null");
                    return;
                }
                
                // 应用格式化，然后转义输出
                var formatted = string.Format($"{{0:{propertyToken.Format}}}", propertys[i]);
                AppendJsonStringContent(formatted);
            }
        }
        catch (Exception ex)
        {
            AppendJsonStringContent(propertyToken.RawText.Text);
            InternalLogger.Error(ex, $"LogWriter RenderPropertyToken Failed: {propertyToken.RawText.Text}");
        }
    }

    /// <summary>
    /// 将 .NET 对象转换为 JSON 值格式
    /// 数值和布尔类型直接输出（不带引号）
    /// 字符串类型需要转义并添加引号
    /// </summary>
    private void ToJsonValue(object? value)
    {
        if (value == null)
        {
            _stringBuilder.Append("null");
            return;
        }

        switch (value)
        {
            // ===== 数值类型 (Numbers) =====
            // 数值类型在 JSON 中不需要引号，直接输出
            // 使用 InvariantCulture 确保数值格式不受系统区域设置影响（避免逗号分隔符问题）
            case int i:
                _stringBuilder.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                _stringBuilder.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case short s:
                _stringBuilder.Append(s.ToString(CultureInfo.InvariantCulture));
                break;
            case byte b:
                _stringBuilder.Append(b.ToString(CultureInfo.InvariantCulture));
                break;
            case uint ui:
                _stringBuilder.Append(ui.ToString(CultureInfo.InvariantCulture));
                break;
            case ulong ul:
                _stringBuilder.Append(ul.ToString(CultureInfo.InvariantCulture));
                break;
            case ushort us:
                _stringBuilder.Append(us.ToString(CultureInfo.InvariantCulture));
                break;
            case sbyte sb:
                _stringBuilder.Append(sb.ToString(CultureInfo.InvariantCulture));
                break;
            case decimal dec:
                _stringBuilder.Append(dec.ToString(CultureInfo.InvariantCulture));
                break;

            // 浮点数需要特殊处理 NaN 和 Infinity
            // JSON 标准不支持 NaN 和 Infinity，需要转为字符串
            case double d:
                if (double.IsNaN(d))
                    _stringBuilder.Append("\"NaN\"");
                else if (double.IsPositiveInfinity(d))
                    _stringBuilder.Append("\"Infinity\"");
                else if (double.IsNegativeInfinity(d))
                    _stringBuilder.Append("\"-Infinity\"");
                else
                    // "R" 格式确保往返转换精度（Round-trip）
                    _stringBuilder.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case float f:
                if (float.IsNaN(f))
                    _stringBuilder.Append("\"NaN\"");
                else if (float.IsPositiveInfinity(f))
                    _stringBuilder.Append("\"Infinity\"");
                else if (float.IsNegativeInfinity(f))
                    _stringBuilder.Append("\"-Infinity\"");
                else
                    _stringBuilder.Append(f.ToString("R", CultureInfo.InvariantCulture));
                break;

            // ===== 布尔类型 (Boolean) =====
            // JSON 布尔值是小写的 true/false（不带引号）
            case bool b:
                _stringBuilder.Append(b ? "true" : "false");
                break;

            // ===== 字符串类型 =====
            // 所有字符串类型都需要转义并添加引号
            case string s:
                AppendJsonString(s);
                break;
            case char c:
                AppendJsonString(c.ToString());
                break;

            // ===== 日期时间类型 =====
            // 使用 ISO 8601 格式（"O"）以确保跨平台兼容性和精度
            case DateTime dt:
                AppendJsonString(dt.ToString("O"));
                break;
            case DateTimeOffset dto:
                AppendJsonString(dto.ToString("O"));
                break;

            // ===== 其他常见类型 =====
            case Guid g:
                AppendJsonString(g.ToString());
                break;
            case TimeSpan ts:
                AppendJsonString(ts.ToString());
                break;
            case Uri u:
                AppendJsonString(u.ToString());
                break;

            // ===== 默认回退 (Default Fallback) =====
            // 对于未知类型，调用 ToString() 并作为字符串处理
            default:
                AppendJsonString(value.ToString() ?? "null");
                break;
        }
    }
    #endregion
}