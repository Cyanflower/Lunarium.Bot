using LunariumBot.Net.DataCenter.Models;

namespace LunariumBot.Net.DataCenter; // 为了方便调用，直接放在 DataCenter 命名空间

/// <summary>
/// 积分计算静态工具类
/// </summary>
public static class Credit
{
    // 使用 volatile 确保多线程热重载配置时的可见性
    private static volatile CreditConfig? _config;

    /// <summary>
    /// 注入配置（需在 ConfigLoader 初始化及重载时调用）
    /// </summary>
    public static void Configure(CreditConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 计算发送消息的积分
    /// </summary>
    /// <param name="level">积分等级</param>
    /// <param name="channelId">频道ID（可选，若未提供或未配置因数，因数默认为1）</param>
    public static int ForMessage(CreditLevel level, ulong? channelId = null) => 
        Calculate(_config?.ForMessage, level, channelId);

    /// <summary>
    /// 计算创建主题（帖子）的积分
    /// </summary>
    /// <param name="level">积分等级</param>
    /// <param name="channelId">频道ID（可选，若未提供或未配置因数，因数默认为1）</param>
    public static int ForThread(CreditLevel level, ulong? channelId = null) => 
        Calculate(_config?.ForThread, level, channelId);

    /// <summary>
    /// 计算更新内容的积分
    /// </summary>
    /// <param name="level">积分等级</param>
    /// <param name="channelId">频道ID（可选，若未提供或未配置因数，因数默认为1）</param>
    public static int ForUpdate(CreditLevel level, ulong? channelId = null) => 
        Calculate(_config?.ForUpdate, level, channelId);

    /// <summary>
    /// 核心计算逻辑
    /// </summary>
    private static int Calculate(CreditLevelSettings? settings, CreditLevel level, ulong? channelId)
    {
        // 1. 基础防御
        if (_config is null || settings is null) return 0;

        // 2. 获取基础分 (C# 8.0+ switch expression)
        int baseScore = level switch
        {
            CreditLevel.Level1 => settings.Level1,
            CreditLevel.Level2 => settings.Level2,
            CreditLevel.Level3 => settings.Level3,
            _ => 0
        };

        // 如果基础分为0，直接短路返回，节省一次字典查找开销
        if (baseScore == 0) return 0;

        // 3. 获取因数
        // 逻辑说明：
        // - 默认因数为 1
        // - 仅当 channelId 有值 且 在 ChannelFactor 字典中存在时，才覆盖因数
        int factor = 1;
        
        if (channelId.HasValue && _config.ChannelFactor.TryGetValue(channelId.Value, out int configuredFactor))
        {
            factor = configuredFactor;
        }

        return baseScore * factor;
    }
}