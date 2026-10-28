// .NET 核心库
using System.Text.Json;

// 解决方案内引用项目
using Lunarium.Logging;

// 项目内命名空间
using Lunarium.Bot.DataCenter.Utils;
using Lunarium.Bot.DataCenter.Models;


namespace Lunarium.Bot.DataCenter;

public class ConfigLoader
{
    public DataCenterBotConfig Config { get; private set; }
    public HashSet<ulong> AnalysisChannelSet { get; private set; } = new();
    public HashSet<ulong> ValidChannelSet { get; private set; } = new();
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

                Credit.Configure(Config.CreditConfig);

                _logger.Info("⚙️ 配置文件加载完成");

                AnalysisChannelSet = Config.DeployConfig.GuildAnalysisConfigs.Values
                    .SelectMany(kv => kv.CategoryChannels)
                    .Select(kv => kv.Value)
                    .ToHashSet();

                ValidChannelSet = Config.DeployConfig.ScanConfigs.Values
                    .SelectMany(kv => kv.ValidChannels)
                    .Select(k => k.Value)
                    .ToHashSet();
            }
            catch (Exception ex)
            {
                _logger.Critical(ex, "配置加载失败");
                throw;
            }
        }
    }
    
    /// <summary>
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
                // 1. 加载并验证新配置
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

                // 2. 准备新数据的 HashSet (在内存中构建，尚未应用)
                var newAnalysisChannelSet = newConfig.DeployConfig.GuildAnalysisConfigs.Values
                    .SelectMany(kv => kv.CategoryChannels)
                    .Select(kv => kv.Value)
                    .ToHashSet();

                var newValidChannelSet = newConfig.DeployConfig.ScanConfigs.Values
                    .SelectMany(kv => kv.ValidChannels)
                    .Select(k => k.Value)
                    .ToHashSet();

                // 3. 调用通用逻辑更新两个集合 (原地修改)
                // 这里的 AnalysisChannelSet 和 ValidChannelSet 是类的成员属性/字段
                var analysisChangeInfo = UpdateSetInPlace(AnalysisChannelSet, newAnalysisChannelSet, "分析频道");
                var validChangeInfo = UpdateSetInPlace(ValidChannelSet, newValidChannelSet, "有效频道");

                // 4. 替换配置对象
                Config = newConfig;
                
                Credit.Configure(Config.CreditConfig);

                _logger.Info("⚙️ 配置文件加载完成");

                // 5. 拼接最终返回的变更信息
                // 如果两个都没变，显示"无变化"；否则拼接变化的那个
                var changes = new[] { analysisChangeInfo, validChangeInfo }
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                string finalMessage = changes.Any() 
                    ? $"配置更新成功 - {string.Join("; ", changes)}" 
                    : "配置更新成功 - 频道集合无变化";

                return (true, finalMessage);
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
    /// 通用集合更新逻辑：对比新旧集合，原地执行增删，并记录详细日志
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    /// <param name="currentSet">当前正在使用的集合（将被原地修改）</param>
    /// <param name="newSet">从新配置中加载的集合</param>
    /// <param name="setName">集合名称（用于日志显示）</param>
    /// <returns>变更统计信息字符串</returns>
    private string UpdateSetInPlace<T>(HashSet<T> currentSet, HashSet<T> newSet, string setName)
    {
        // 1. 找出需要移除的（在旧的中，不在新的中）
        var toRemove = currentSet.Except(newSet).ToList();
        
        // 2. 找出需要添加的（在新的中，不在旧的中）
        var toAdd = newSet.Except(currentSet).ToList();
        
        // 3. 如果没有变化，直接返回
        if (!toRemove.Any() && !toAdd.Any())
        {
            _logger.Info("[{setName}] 集合无变化", setName);
            return string.Empty; // 返回空表示无变更
        }

        // 4. 执行移除
        foreach (var item in toRemove)
        {
            currentSet.Remove(item);
            _logger.Debug("[{setName}] 移除项: {item}", setName, item);
        }

        // 5. 执行添加
        foreach (var item in toAdd)
        {
            currentSet.Add(item);
            _logger.Debug("[{setName}] 添加项: {item}", setName, item);
        }

        // 6. 生成并记录统计信息
        var changeInfo = $"{setName}: 移除 {toRemove.Count} 个, 添加 {toAdd.Count} 个";
        _logger.Info("📊 集合已更新 - {changeInfo}", changeInfo);

        return changeInfo;
    }
}