// Services/ConfigLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// 负责加载和解析项目配置。
/// 此类在程序启动时被实例化，它会读取 token.txt 和 config.jsonc 文件，
/// 并将 JSON 配置反序列化为强类型的C#对象，以便在整个应用程序中安全、方便地访问。
/// </summary>
namespace SearchBot;

public class ConfigLoader
{
    // === 私有字段 ===

    /// <summary>
    /// 持有所有从 config.jsonc 反序列化而来的配置对象。
    /// 标记为 readonly 确保它在构造函数执行完毕后不会被再次修改。
    /// </summary>
    private readonly Config _config = null!;
    
    /// <summary>
    /// 日志记录器，用于记录配置加载过程中的信息和错误。
    /// </summary>
    private readonly ILogger<ConfigLoader> _logger;

    // === 构造函数 ===

    /// <summary>
    /// 初始化一个新的 <see cref="ConfigLoader"/> 实例。
    /// 此构造函数会立即执行文件读取和解析操作。
    /// 如果关键的 token.txt 文件不存在或为空，将抛出异常。
    /// 如果 config.jsonc 不存在或为空，将使用默认配置并打印警告信息。
    /// </summary>
    /// <param name="logger">用于记录日志的记录器实例。</param>
    /// <exception cref="Exception">当 token.txt 无效时抛出。</exception>
    public ConfigLoader(ILogger<ConfigLoader> logger)
    {
        _logger = logger;
        
        // === 读取 token.txt ===
        try
        {
            Token = File.ReadAllText("Configs/SearchBot/token.txt").Trim();
            if (string.IsNullOrEmpty(Token))
            {
                // Token 是程序运行的必需品，如果无效则无法继续。
                throw new Exception("错误：token.txt 为空或不存在！");
            }
        }
        catch (FileNotFoundException)
        {
            throw new Exception("错误：token.txt 不存在！");
        }

        // === 读取并解析 config.jsonc ===
        try
        {
            var jsonContent = File.ReadAllText("Configs/SearchBot/config.jsonc");
            if (!string.IsNullOrEmpty(jsonContent))
            {
                _logger.LogInformation("正在解析 config.jsonc...");
                
                try
                {
                    // 创建支持注释的 JSON 序列化选项
                    var options = new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip, // 跳过注释
                        PropertyNameCaseInsensitive = true, // 不区分大小写
                        AllowTrailingCommas = true // 允许尾随逗号
                    };
                    
                    // 使用 System.Text.Json 反序列化 JSON 配置
                    _config = JsonSerializer.Deserialize<Config>(jsonContent, options) ?? new Config();
                    _logger.LogInformation("✅ config.jsonc 解析成功");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ config.jsonc 解析失败: {ErrorMessage}", ex.Message);
                    _logger.LogWarning("将使用默认配置。如果您需要自定义配置，请检查 config.jsonc 文件格式。");
                    
                    // 解析失败时使用默认配置
                    _config = new Config();
                }
            }
            else
            {
                // 如果文件存在但内容为空，则创建一个默认的 Config 实例。
                _logger.LogWarning("config.jsonc 为空，将使用默认配置。");
                _config = new Config();
            }
        }
        catch (FileNotFoundException)
        {
            // 如果配置文件不存在，同样创建一个默认的 Config 实例，允许程序在没有自定义配置的情况下启动。
            _logger.LogInformation("未找到 config.jsonc 文件，将使用默认配置。");
            _config = new Config();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 读取 config.jsonc 时发生未知错误: {ErrorMessage}", ex.Message);
            _logger.LogWarning("将使用默认配置。");
            _config = new Config();
        }
    }

    // === 公开属性 (作为访问配置的统一入口) ===
    // 下方的所有属性都使用了C# 6.0的表达式主体语法 (=>)，
    // 它们是内部 _config 对象的只读代理，提供了简洁、安全的访问方式。

    /// <summary>
    /// 获取从 token.txt 中读取的机器人令牌。
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// 获取机器人的主服务器（Guild）ID。
    /// </summary>
    public ulong MainGuildId => _config.BotConfig.MainGuildId;

    /// <summary>
    /// 获取用于接收维护和状态通知的频道ID。
    /// </summary>
    public ulong MaintainChannelId => _config.BotConfig.MaintainChannelId;

    /// <summary>
    /// 获取一个列表，其中包含机器人命令需要部署到的所有服务器（Guild）的ID。
    /// </summary>
    public HashSet<ulong> DeployGuildIds => _config.AuthConfig.DeployGuildIds;

    /// <summary>
    /// 获取拥有机器人管理权限的角色ID列表。
    /// </summary>
    public HashSet<ulong> AdminRoleIds => _config.AuthConfig.AdminRoleIds;

    /// <summary>
    /// 获取所有服务器的特定配置。
    /// </summary>
    public GuildsConfig GuildsConfig => _config.GuildsConfig;

    /// <summary>
    /// 获取横幅（Banner）相关的配置。
    /// </summary>
    public BannerConfig BannerConfig => _config.BannerConfig;

    /// <summary>
    /// 获取所有扫描分类的名称集合 (例如 "男性向", "女性向" 等)。
    /// 使用 HashSet 是为了快速查找和去重（尽管字典的键本身就是唯一的）。
    /// </summary>
    public HashSet<string> ScanCategories => new(_config.GuildsConfig.ScanChannelIDS.Keys);

    /// <summary>
    /// 获取所有扫描频道ID的集合，此集合聚合了所有分类下的所有频道ID。
    /// 使用 HashSet 可以高效地判断某个频道ID是否存在于任何一个扫描分类中。
    /// </summary>
    public HashSet<ulong> AllScanChannelIds => new(_config.GuildsConfig.ScanChannelIDS.Values.SelectMany(list => list));
}

// === 以下是与 config.jsonc 结构完全对应的C#模型类 ===
// 这些类使用了属性初始化器来提供默认值，确保即使配置文件中缺少某些字段，
// 程序也能安全地访问这些属性而不会遇到空引用异常。

/// <summary>
/// 整个 config.jsonc 文件的根配置对象。
/// </summary>
public class Config
{
    public BotConfig BotConfig { get; set; } = new();
    public AuthConfig AuthConfig { get; set; } = new();
    public GuildsConfig GuildsConfig { get; set; } = new();
    public BannerConfig BannerConfig { get; set; } = new();
}

/// <summary>
/// 代表 BotConfig 配置节。
/// </summary>
public class BotConfig
{
    public ulong MainGuildId { get; set; } = 0;
    public ulong MaintainChannelId { get; set; } = 0;
}

/// <summary>
/// 代表 AuthConfig 配置节。
/// </summary>
public class AuthConfig
{
    
    public HashSet<ulong> DeployGuildIds { get; set; } = new();
    public HashSet<ulong> AdminRoleIds { get; set; } = new();
}

/// <summary>
/// 代表 GuildsConfig 配置节，包含多个服务器的特定设置。
/// </summary>
public class GuildsConfig
{
    /// <summary>
    /// 以服务器ID字符串为键，存储每个服务器的特定信息。
    /// Key: GuildId (as string), Value: GuildInfo
    /// </summary>
    public Dictionary<string, GuildInfo> GuildIds { get; set; } = new();

    /// <summary>
    /// 以扫描类别为键，存储需要扫描的频道ID列表。
    /// Key: Category Name (e.g., "男性向"), Value: List of Channel Ids
    /// </summary>
    public Dictionary<string, List<ulong>> ScanChannelIDS { get; set; } = new();
}

/// <summary>
/// 代表单个服务器的配置信息。
/// </summary>
public class GuildInfo
{
    public ulong DashboardChannelId { get; set; } = 0;
}

/// <summary>
/// 代表 BannerConfig 配置节，它包含了多个子配置。
/// </summary>
public class BannerConfig
{
    public BannerItemConfig New { get; set; } = new();
    public BannerItemConfig Popular { get; set; } = new();
    public BannerItemConfig Feature { get; set; } = new();
    public BannerItemConfig Toplist { get; set; } = new();
}

/// <summary>
/// 代表 BannerConfig 中的单个项，如 BannerConfig.New。
/// </summary>
public class BannerItemConfig
{
    public List<ulong> ChannelId { get; set; } = new();
}
