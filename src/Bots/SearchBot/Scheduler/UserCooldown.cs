using System.Collections.Concurrent;

namespace SearchBot.Scheduler;

public class UserCooldown
{
    // 字典的值现在是冷却的结束时间
    private readonly ConcurrentDictionary<ulong, DateTime> _userCooldowns = new();
    
    // 冷却时间可以配置，这里设置为1分钟
    private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 检查用户是否在冷却中。如果不在，则为他设置新的冷却时间。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>如果用户在冷却中，返回 true；否则返回 false。</returns>
    public bool IsOnCooldown(ulong userId)
    {
        var now = DateTime.UtcNow;

        // 尝试获取用户的冷却结束时间
        if (_userCooldowns.TryGetValue(userId, out var cooldownEndTime))
        {
            // 如果当前时间 < 冷却结束时间，说明还在冷却中
            if (now < cooldownEndTime)
            {
                return true; // 在冷却
            }
        }

        // 如果用户不存在，或者冷却时间已过，就为其设置下一次的冷却结束时间
        var newCooldownEndTime = now.Add(_cooldownDuration);
        _userCooldowns.AddOrUpdate(userId, newCooldownEndTime, (key, oldV) => newCooldownEndTime);
        
        return false; // 不在冷却
    }
}