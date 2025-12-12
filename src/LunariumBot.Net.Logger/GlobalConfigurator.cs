namespace LunariumBot.Net.Logger;

/// <summary>
/// 全局配置器 - 用于配置日志系统的全局设置
/// </summary>
public static class GlobalConfigurator
{
    private static readonly List<Action> _configOperations = new();
    private static bool _isConfiguring = false;

    #region 公共 API: 主方法
    
    /// <summary>
    /// 开始配置流程(链式调用的起点)
    /// </summary>
    public static ConfigurationBuilder Configure()
    {
        if (GlobalConfigLock.Configured)
        {
            throw new InvalidOperationException(
                "Global configuration has already been applied. " +
                "Configuration can only be done once during application startup.");
        }
        
        if (_isConfiguring)
        {
            throw new InvalidOperationException(
                "Configuration is already in progress. " +
                "Call Apply() to complete the current configuration.");
        }

        _isConfiguring = true;
        _configOperations.Clear();
        return new ConfigurationBuilder();
    }

    /// <summary>
    /// 应用配置(在 ConfigurationBuilder 中调用)
    /// </summary>
    internal static void ApplyConfiguration()
    {
        if (!_isConfiguring)
        {
            throw new InvalidOperationException(
                "No configuration in progress. Call Configure() first.");
        }

        try
        {
            // 应用默认配置
            ApplyDefaultConfiguration();
            
            // 应用用户自定义配置(会覆盖默认配置)
            foreach (var operation in _configOperations)
            {
                operation.Invoke();
            }
            
            // 锁定配置
            GlobalConfigLock.CompleteConfig();
        }
        finally
        {
            _isConfiguring = false;
            _configOperations.Clear();
        }
    }

    /// <summary>
    /// 仅应用默认配置(在 LoggerBuilder.Build() 中调用)
    /// </summary>
    internal static void ApplyDefaultIfNotConfigured()
    {
        if (!GlobalConfigLock.Configured)
        {
            ApplyDefaultConfiguration();
            GlobalConfigLock.CompleteConfig();
        }
    }

    #endregion

    #region 内部方法

    internal static void AddConfigOperation(Action operation)
    {
        if (!_isConfiguring)
        {
            throw new InvalidOperationException(
                "Configuration not started. Call Configure() first.");
        }
        _configOperations.Add(operation);
    }

    private static void ApplyDefaultConfiguration()
    {
        // 默认配置：本地时间 + ISO8601(JSON) + 自定义格式(Text)
        LogTimestampConfig.UseLocalTime();
        TimestampFormatConfig.ConfigJsonMode(JsonTimestampMode.ISO8601);
        TimestampFormatConfig.ConfigTextMode(TextTimestampMode.Custom);
        TimestampFormatConfig.ConfigJsonCustomFormat("O");
        TimestampFormatConfig.ConfigTextCustomFormat("yyyy-MM-dd HH:mm:ss.fff");
    }

    #endregion

    /// <summary>
    /// 配置构建器 - 提供流式配置 API
    /// </summary>
    public class ConfigurationBuilder
    {
        internal ConfigurationBuilder() { }

        #region 日志时间系统

        /// <summary>
        /// 配置日志系统使用 UTC 时间
        /// </summary>
        public ConfigurationBuilder UseUtcTimeZone()
        {
            AddConfigOperation(() => LogTimestampConfig.UseUtcTime());
            return this;
        }

        /// <summary>
        /// 配置日志系统使用本地时间
        /// </summary>
        public ConfigurationBuilder UseLocalTimeZone()
        {
            AddConfigOperation(() => LogTimestampConfig.UseLocalTime());
            return this;
        }

        /// <summary>
        /// 配置日志系统使用自定义时区
        /// </summary>
        public ConfigurationBuilder UseCustomTimezone(TimeZoneInfo timeZone)
        {
            ArgumentNullException.ThrowIfNull(timeZone);
            AddConfigOperation(() => LogTimestampConfig.UseCustomTimeZone(timeZone));
            return this;
        }

        #endregion

        #region JSON 时间戳格式

        /// <summary>
        /// JSON 日志使用 Unix 时间戳(秒) - 始终为 UTC
        /// </summary>
        public ConfigurationBuilder UseJsonUnixTimestamp()
        {
            AddConfigOperation(() => TimestampFormatConfig.ConfigJsonMode(JsonTimestampMode.Unix));
            return this;
        }

        /// <summary>
        /// JSON 日志使用 Unix 时间戳(毫秒) - 始终为 UTC
        /// </summary>
        public ConfigurationBuilder UseJsonUnixMsTimestamp()
        {
            AddConfigOperation(() => TimestampFormatConfig.ConfigJsonMode(JsonTimestampMode.UnixMs));
            return this;
        }

        /// <summary>
        /// JSON 日志使用 ISO 8601 标准格式(默认："O")
        /// </summary>
        public ConfigurationBuilder UseJsonISO8601Timestamp()
        {
            AddConfigOperation(() => TimestampFormatConfig.ConfigJsonMode(JsonTimestampMode.ISO8601));
            return this;
        }

        /// <summary>
        /// JSON 日志使用自定义格式
        /// </summary>
        public ConfigurationBuilder UseJsonCustomTimestamp(string format)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(format);
            AddConfigOperation(() =>
            {
                TimestampFormatConfig.ConfigJsonMode(JsonTimestampMode.Custom);
                TimestampFormatConfig.ConfigJsonCustomFormat(format);
            });
            return this;
        }

        #endregion

        #region 文本时间戳格式

        /// <summary>
        /// 文本日志使用 Unix 时间戳(秒) - 始终为 UTC
        /// </summary>
        public ConfigurationBuilder UseTextUnixTimestamp()
        {
            AddConfigOperation(() => TimestampFormatConfig.ConfigTextMode(TextTimestampMode.Unix));
            return this;
        }

        /// <summary>
        /// 文本日志使用 Unix 时间戳(毫秒) - 始终为 UTC
        /// </summary>
        public ConfigurationBuilder UseTextUnixMsTimestamp()
        {
            AddConfigOperation(() => TimestampFormatConfig.ConfigTextMode(TextTimestampMode.UnixMs));
            return this;
        }

        /// <summary>
        /// 文本日志使用 ISO 8601 标准格式
        /// </summary>
        public ConfigurationBuilder UseTextISO8601Timestamp()
        {
            AddConfigOperation(() => TimestampFormatConfig.ConfigTextMode(TextTimestampMode.ISO8601));
            return this;
        }

        /// <summary>
        /// 文本日志使用自定义格式(默认："yyyy-MM-dd HH:mm:ss.fff")
        /// </summary>
        public ConfigurationBuilder UseTextCustomTimestamp(string format)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(format);
            AddConfigOperation(() =>
            {
                TimestampFormatConfig.ConfigTextMode(TextTimestampMode.Custom);
                TimestampFormatConfig.ConfigTextCustomFormat(format);
            });
            return this;
        }

        #endregion

        #region 自动解构集合

        /// <summary>
        /// 启用集合类型的自动解构（无需使用 {@...} 语法）
        /// </summary>
        public ConfigurationBuilder EnableAutoDestructuring()
        {
            AddConfigOperation(() => DestructuringConfig.EnableAutoDestructuring());
            return this;
        }

        #endregion

        #region JSON 序列化配置

        /// <summary>
        /// 配置 JSON 序列化保持中文字符不转义（默认：启用）
        /// </summary>
        public ConfigurationBuilder PreserveChineseCharacters()
        {
            AddConfigOperation(() => JsonSerializationConfig.ConfigPreserveChineseCharacters(true));
            return this;
        }

        /// <summary>
        /// 配置 JSON 序列化转义中文字符为 Unicode
        /// </summary>
        public ConfigurationBuilder EscapeChineseCharacters()
        {
            AddConfigOperation(() => JsonSerializationConfig.ConfigPreserveChineseCharacters(false));
            return this;
        }

        /// <summary>
        /// 配置 JSON 序列化使用缩进格式（多行输出）
        /// </summary>
        public ConfigurationBuilder UseIndentedJson()
        {
            AddConfigOperation(() => JsonSerializationConfig.ConfigWriteIndented(true));
            return this;
        }

        /// <summary>
        /// 配置 JSON 序列化使用紧凑格式（单行输出，默认）
        /// </summary>
        public ConfigurationBuilder UseCompactJson()
        {
            AddConfigOperation(() => JsonSerializationConfig.ConfigWriteIndented(false));
            return this;
        }

        #endregion
        
        /// <summary>
        /// 应用所有配置
        /// </summary>
        public void Apply()
        {
            ApplyConfiguration();
        }
    }
}