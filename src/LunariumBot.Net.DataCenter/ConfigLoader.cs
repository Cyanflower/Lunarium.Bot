// .NET 核心库
using System.Text.Json;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Model;


namespace LunariumBot.Net.DataCenter;

public class ConfigLoader
{
    public DataCenterBotConfig Config { get; private set; }
    public HashSet<ulong> ChannelSet { get; private set; } = new();
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

                ChannelSet = Config.DeployConfig.GuildAnalysisConfigs.Values
                    .SelectMany(k => k.CategoryChannels)
                    .SelectMany(k => k.Value)
                    .ToHashSet();

            }
            catch (Exception ex)
            {
                _logger.Critical("配置加载失败: {ex}", ex);
                throw;
            }
        }
    }
    
    /// 重新加载配置
    /// </summary>
    /// <returns>执行结果元组 (是否成功, 原因说明)</returns>
    public (bool result, string reason) ReloadConfig()
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
                    var errorMessage = string.Join("; ", Errors);
                    foreach (var error in Errors)
                    {
                        _logger.Error("严重错误: {error}", error);
                    }
                    return (false, $"配置文件验证失败: {errorMessage}");
                }
                
                // 获取新配置中的所有频道
                var newChannelSet = newConfig.DeployConfig.GuildAnalysisConfigs.Values
                    .SelectMany(k => k.CategoryChannels)
                    .SelectMany(k => k.Value)
                    .ToHashSet();
                
                // 找出需要移除的频道（在旧集合中但不在新集合中）
                var channelsToRemove = ChannelSet.Except(newChannelSet).ToList();
                
                // 找出需要添加的频道（在新集合中但不在旧集合中）
                var channelsToAdd = newChannelSet.Except(ChannelSet).ToList();
                
                // 执行移除
                foreach (var channelId in channelsToRemove)
                {
                    ChannelSet.Remove(channelId);
                    _logger.Debug("移除频道: {channelId}", channelId);
                }
                
                // 执行添加
                foreach (var channelId in channelsToAdd)
                {
                    ChannelSet.Add(channelId);
                    _logger.Debug("添加频道: {channelId}", channelId);
                }
                
                // 记录变化统计
                string changeInfo;
                if (channelsToRemove.Any() || channelsToAdd.Any())
                {
                    changeInfo = $"移除 {channelsToRemove.Count} 个频道, 添加 {channelsToAdd.Count} 个频道";
                    _logger.Info("📊 频道集合已更新 - {changeInfo}", changeInfo);
                }
                else
                {
                    changeInfo = "频道集合无变化";
                    _logger.Info(changeInfo);
                }
                
                Config = newConfig;
                _logger.Info("⚙️ 配置文件加载完成");
                
                return (true, $"配置重新加载成功 - {changeInfo}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"配置重新加载失败: {ex.Message}";
                _logger.Critical(ex, "配置重新加载失败:");
                return (false, errorMsg);
            }
        }
    }

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    private static DataCenterBotConfig LoadConfigFromFile()
    {
        try
        {
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.jsonc");

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"配置文件不存在: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            var DataCenterBotConfig = JsonSerializer.Deserialize<DataCenterBotConfig>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip // 支持JSONC注释
            });
            return DataCenterBotConfig ?? throw new InvalidOperationException("配置文件中未找到对应配置的数据");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException ($"加载配置失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 验证配置
    /// </summary>
    public static (bool IsValid, List<string> Errors) ValidateConfig(DataCenterBotConfig config)
    {
        var errors = new List<string>();

        if (config.AuthConfig?.AdminRoleIds is null || config.AuthConfig.AdminRoleIds.Count == 0)
            errors.Add("AuthConfig.AdminRoleIds 不能为空");

        if (config.DeployConfig?.DeployGuildIds is null || config.DeployConfig.DeployGuildIds.Count == 0)
            errors.Add("GuildsConfig.DeployGuildIds 不能为空");

        return (errors.Count == 0, errors);
    }
}