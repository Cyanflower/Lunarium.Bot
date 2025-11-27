// .NET 核心库
using System.Text.Json;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Models;


namespace LunariumBot.Net.SearchBot;

public class ConfigLoader
{
    public SearchBotConfig Config { get; private set; }
    private readonly object _lockObject = new();
    private readonly ILogger _logger;

    /// <summary>
    /// 获取SearchBot配置实例
    /// </summary>
    public ConfigLoader(ILogger logger)
    {
        _logger = logger.ForContext("ConfigLoader");
        lock (_lockObject)
        {
            try
            {
                Config = LoadConfigFromFile();
                var (IsValid, Errors) = ValidateConfig(Config);
                if (!IsValid)
                {
                    var ex = new InvalidOperationException($"配置文件内容错误");
                    foreach (var error in Errors)
                    {
                        _logger.Critical("严重错误: {error}", error);
                    }
                    throw ex;
                }
                _logger.Info("⚙️ 配置文件加载完成");
            }
            catch (Exception ex)
            {
                _logger.Critical("配置加载失败: {ex}", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// 重新加载配置
    /// </summary>
    public void ReloadConfig()
    {
        _logger.Info("⚙️ 重新加载配置文件...");
        lock (_lockObject)
        {
            try
            {
                var newConfig = LoadConfigFromFile();
                var (IsValid, Errors) = ValidateConfig(newConfig);
                if (!IsValid)
                {
                    var ex = new InvalidOperationException("配置文件内容错误");
                    foreach (var error in Errors)
                    {
                        _logger.Error("严重错误: {error}",error);
                    }
                    return;
                }
                else
                { 
                    Config = newConfig;
                    _logger.Info("⚙️ 配置文件加载完成");
                }
            }
            catch (Exception ex)
            {
                _logger.Critical(ex,"配置重新加载失败:");
            }
        }
    }

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    private static SearchBotConfig LoadConfigFromFile()
    {
        try
        {
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.jsonc");

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"配置文件不存在: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            var SearchBotConfig = JsonSerializer.Deserialize<SearchBotConfig>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip // 支持JSONC注释
            });
            return SearchBotConfig ?? throw new InvalidOperationException("配置文件中未找到对应配置的数据");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException ($"加载配置失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 验证配置
    /// </summary>
    public static (bool IsValid, List<string> Errors) ValidateConfig(SearchBotConfig config)
    {
        var errors = new List<string>();

        if (config.AuthConfig?.AdminRoleIds == null || config.AuthConfig.AdminRoleIds.Count == 0)
            errors.Add("AuthConfig.AdminRoleIds 不能为空");

        if (config.GuildsConfig?.DeployGuildIds == null || config.GuildsConfig.DeployGuildIds.Count == 0)
            errors.Add("GuildsConfig.DeployGuildIds 不能为空");

        if (config.GuildsConfig?.ScanChannelIDS == null || config.GuildsConfig.ScanChannelIDS.Count == 0)
            errors.Add("GuildsConfig.ScanChannelIDS 不能为空");

        return (errors.Count == 0, errors);
    }
}