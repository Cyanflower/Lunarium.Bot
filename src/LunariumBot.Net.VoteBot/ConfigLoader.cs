// .NET 核心库
using System.Text.Json;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.VoteBot.Models;


namespace LunariumBot.Net.VoteBot;

public class ConfigLoader
{
    public VoteBotConfig Config { get; private set; }
    private readonly ILogger _logger;

    /// <summary>
    /// 获取SearchBot配置实例
    /// </summary>
    public ConfigLoader(ILogger logger)
    {
        _logger = logger.ForContext("ConfigLoader");
        try
        {
            Config = LoadConfigFromFile();
            _logger.Info("⚙️ 配置文件加载完成");
        }
        catch (Exception ex)
        {
            _logger.Critical("配置加载失败: {ex}", ex);
            throw;
        }
    }

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    private static VoteBotConfig LoadConfigFromFile()
    {
        try
        {
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.jsonc");

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"配置文件不存在: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            var VoteBotConfig = JsonSerializer.Deserialize<VoteBotConfig>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip // 支持JSONC注释
            });
            return VoteBotConfig ?? throw new InvalidOperationException("配置文件中未找到对应配置的数据");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException ($"加载配置失败: {ex.Message}", ex);
        }
    }
}