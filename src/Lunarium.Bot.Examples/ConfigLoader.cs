// .NET 核心库
using System.Text.Json;
using System.Text;

// 解决方案内引用项目
using Lunarium.Bot.Logger;

// 项目内命名空间
using Lunarium.Bot.Examples.Model;


namespace Lunarium.Bot.Examples;

public class ConfigLoader
{
    public ExamplesConfig Config { get; private set; }
    private readonly object _lockObject = new();
    private readonly ILogger _logger;

    private readonly string _configPath;

    /// <summary>
    /// 获取SearchBot配置实例
    /// </summary>
    public ConfigLoader(ILogger logger)
    {
        _logger = logger.ForContext("ConfigLoader");
        _configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.jsonc");
        
        lock (_lockObject)
        {
            try
            {
                var config = LoadConfigFromFile();
                // 调用验证方法
                ValidateConfig(config);
                Config = config;
                _logger.Info("⚙️ 配置文件加载并验证成功");
            }
            catch (Exception ex)
            {
                _logger.Critical("配置加载或验证失败: {ex}", ex);
                throw; // 首次加载失败，直接抛出异常终止程序
            }
        }
    }
    
    /// <summary>
    /// 安全地更新配置
    /// </summary>
    /// <param name="updateAction">一个委托，用于对配置的副本进行修改</param>
    /// <returns>成功返回true, 失败返回false</returns>
    public bool UpdateConfig(Action<ExamplesConfig> updateAction)
    {
        _logger.Info("⚙️ 正在尝试安全更新配置...");
        lock (_lockObject)
        {
            try
            {
                // 1. 深度复制一份当前配置作为副本
                // (对于这个项目，简单的序列化/反序列化就能实现深度复制)
                var currentConfigJson = JsonSerializer.Serialize(Config);
                var configCopy = JsonSerializer.Deserialize<ExamplesConfig>(currentConfigJson)!;

                // 2. 对副本应用修改
                updateAction(configCopy);

                // 3. 对修改后的副本进行检查
                _logger.Info("正在验证修改后的配置...");
                ValidateConfig(configCopy);
                _logger.Info("✅ 配置验证通过。");

                // 4. 通过后，应用到主配置并保存到文件
                Config = configCopy;
                SaveConfig(); // SaveConfig 内部已经有日志记录了

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 配置更新失败，所有更改已被回滚。");
                return false;
            }
        }
    }
    
    /// <summary>
    /// 重新加载配置
    /// </summary>
    public void ReloadConfig()
    {
        _logger.Info("⚙️ 正在尝试重新加载配置文件...");
        lock (_lockObject)
        {
            try
            {
                var newConfig = LoadConfigFromFile();
                // 验证新配置
                ValidateConfig(newConfig);

                // 验证通过，应用新配置
                Config = newConfig;
                _logger.Info("✅ 配置文件重新加载并验证成功！新配置已生效。");
            }
            catch (Exception ex)
            {
                // 验证失败，维持旧配置
                _logger.Error(ex, "❌ 重新加载配置文件失败，将继续使用旧配置。");
                // 此处不抛出异常，以保证程序继续运行
            }
        }
    }
    
    /// <summary>
    /// 保存当前配置到文件
    /// </summary>
    public void SaveConfig()
    {
        _logger.Info("💾 正在保存当前配置到文件...");
        lock (_lockObject)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true, // 格式化输出
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 防止中文被转义
                };
                string jsonContent = JsonSerializer.Serialize(Config, options);
                File.WriteAllText(_configPath, jsonContent);
                _logger.Info("✅ 配置文件已成功保存到: {Path}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 保存配置文件时发生错误。");
                throw; // 保存失败时最好抛出异常让调用者知道
            }
        }
    }

    /// <summary>
    /// 验证配置文件的有效性
    /// </summary>
    /// <param name="config">要验证的配置对象</param>
    /// <exception cref="InvalidOperationException">当配置无效时抛出</exception>
    private void ValidateConfig(ExamplesConfig config)
    {
        var errors = new StringBuilder();

        // 1. 验证 AuthConfig
        if (config.AuthConfig == null)
        {
            errors.AppendLine("- AuthConfig 节不能为空。");
        }
        else
        {
            if (config.AuthConfig.AdminRoleIds == null || !config.AuthConfig.AdminRoleIds.Any())
            {
                errors.AppendLine("- AuthConfig.AdminRoleIds 不能为空，至少需要一个管理员角色ID。");
            }
            if (config.AuthConfig.LogChannel == 0)
            {
                errors.AppendLine("- AuthConfig.LogChannel 必须设置为一个有效的频道ID。");
            }
        }

        // 2. 验证 DeployGuildConfig
        // if (config.DeployGuildConfig == null || !config.DeployGuildConfig.Any())
        // {
        //     errors.AppendLine("- DeployGuildConfig 不能为空，至少需要配置一个服务器。");
        // }
        // else
        // {
        //     ...
        // }

        // 如果有错误，则汇总并抛出异常
        if (errors.Length > 0)
        {
            var errorMessage = $"配置文件验证失败，发现以下问题：\n{errors}";
            throw new InvalidOperationException(errorMessage);
        }
    }

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    private ExamplesConfig LoadConfigFromFile()
    {
        try
        {
            if (!File.Exists(_configPath))
                throw new FileNotFoundException($"配置文件不存在: {_configPath}");

            string jsonContent = File.ReadAllText(_configPath);
            var ExamplesBotConfig = JsonSerializer.Deserialize<ExamplesConfig>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip // 支持JSONC注释
            });
            return ExamplesBotConfig ?? throw new InvalidOperationException("配置文件内容为空或格式不正确。");
        }
        catch (JsonException jsonEx)
        {
            throw new InvalidOperationException($"加载配置失败: JSON格式错误 - {jsonEx.Message}", jsonEx);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载配置失败: {ex.Message}", ex);
        }
    }
}