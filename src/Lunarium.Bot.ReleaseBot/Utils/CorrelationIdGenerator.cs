using Lunarium.Bot.Logger;
using System;

namespace Lunarium.Bot.ReleaseBot.Utils;

/// <summary>
/// 生成人类可读的, 用于关联日志和用户反馈的唯一ID。
/// </summary>
public static class CorrelationIdGenerator
{
    // 定义一个易于读写, 不易混淆的字符集
    private static readonly char[] _charSet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    
    // 定义随机部分的长度
    private const int RandomPartLength = 5;

    /// <summary>
    /// 生成一个新的关联ID
    /// 格式: YYYYMMDD-HHMMSS-XXXXX
    /// </summary>
    /// <returns>一个格式化的关联ID字符串</returns>
    public static string Generate()
    {
        // 使用自实现的日志库工具获取与日志完全同步的时间戳
        // 确保ID的时间部分和日志记录的时间部分来源一致
        DateTimeOffset timestamp = LogUtils.GetLogSystemTimestamp();

        // 将时间戳格式化为需要的前半部分
        string timestampPart = timestamp.ToString("yyyyMMdd-HHmmss");

        // 生成5位的随机码部分
        var randomPartChars = new char[RandomPartLength];
        for (int i = 0; i < RandomPartLength; i++)
        {
            randomPartChars[i] = _charSet[Random.Shared.Next(_charSet.Length)];
        }
        string randomPart = new string(randomPartChars);

        // 组合并返回最终的ID
        return $"{timestampPart}-{randomPart}";
    }
}
