using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;

using Lunarium.Bot.Scheduler;
using Lunarium.Bot.Extensions;
using Lunarium.Bot.ReleaseBot.Utils;
using Lunarium.Bot.Logger;

namespace Lunarium.Bot.ReleaseBot.TokenBucketServices;

public class TokenBucketService : IDisposable
{
    public int TokenLimit => _tokenLimit;
    private readonly ILogger _logger;
    private int _tokenLimit;
    
    // 读写锁
    private readonly ReaderWriterLockSlim _globalLock = new(); 
    
    private readonly ConcurrentDictionary<ulong, TokenBucket> _tokenBuckets = new();

    public TokenBucketService(ILogger logger, int tokenLimit)
    {
        _logger = logger;
        _tokenLimit = tokenLimit;
    }

    /// <summary>
    /// 获取令牌
    /// 允许多个线程同时进入 (持有读锁)
    /// 但如果有 WriteLock(ResumeAllToken) 正在运行, 这里会阻塞
    /// </summary>
    public (bool Allowable, int RemainingToken, int TokenLimit) ConsumeToken(ulong userId, int token)
    {
        // 进入读锁模式
        _globalLock.EnterReadLock();
        try
        {
            var bucket = _tokenBuckets.GetOrAdd(userId, _ => new TokenBucket(_tokenLimit));

            var allowable = bucket.GetToken(token);

            return (allowable, bucket.CurrentToken, _tokenLimit);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetToken Exception");
            throw;
        }
        finally
        {
            // 释放读锁
            _globalLock.ExitReadLock();
        }
    }

    public (bool Allowable, int RemainingToken, int TokenLimit) CheckToken(ulong userId, int token)
    {
        // 进入读锁模式
        _globalLock.EnterReadLock();
        try
        {
            var bucket = _tokenBuckets.GetOrAdd(userId, _ => new TokenBucket(_tokenLimit));

            var allowable = bucket.CheckToken( token);

            return (allowable, bucket.CurrentToken, _tokenLimit);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetToken Exception");
            throw;
        }
        finally
        {
            // 释放读锁
            _globalLock.ExitReadLock();
        }
    }
    
    public void UndoConsumeToken(ulong userId, int token)
    {
        // 进入读锁模式
        _globalLock.EnterReadLock();
        try
        {
            if (_tokenBuckets.TryGetValue(userId, out var tokenBucket))
            {
                tokenBucket.UndoGetToken(token);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "UndoGetToken Exception");
            throw;
        }
        finally
        {
            // 释放读锁
            _globalLock.ExitReadLock();
        }
    }

    public int GetRemainingToken(ulong userId)
    {
        // 进入读锁模式
        _globalLock.EnterReadLock();
        try
        {
            var bucket = _tokenBuckets.GetOrAdd(userId, _ => new TokenBucket(_tokenLimit));

            return bucket.CurrentToken;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetRemainingToken Exception");
            throw;
        }
        finally
        {
            // 释放读锁
            _globalLock.ExitReadLock();
        }
    }

    /// <summary>
    /// 重置/恢复所有令牌 (或者修改配置)
    /// 这是一个独占操作 (持有写锁)
    /// 执行期间, 没有任何 GetToken 能运行
    /// </summary>
    public void ResumeAllToken()
    {
        // 进入写锁
        _globalLock.EnterWriteLock();
        try
        {
            _logger.Info("开始重置...");

            // 直接清空字典是来重置
            // 下次 GetToken 时会自动创建新的满额 Bucket
            _tokenBuckets.Clear();
        }
        finally
        {
            // 释放写锁
            _globalLock.ExitWriteLock();
        }
    }

    public void UpdateTokenLimit(int newLimit)
    {
        // 进入写锁
        _globalLock.EnterWriteLock();
        try
        {
            _tokenLimit = newLimit;

            foreach (var bucket in _tokenBuckets.Values)
            {
                bucket.SetTokenLimit(_tokenLimit);
            }
        }
        finally
        {
            // 释放写锁
            _globalLock.ExitWriteLock();
        }
    }

    // public async Task SaveStateAsync(string filePath)
    // {
    //     TokenBucketSnapshot snapshot;

    //     _globalLock.EnterReadLock();
    //     try
    //     {
    //         snapshot = new TokenBucketSnapshot
    //         {
    //             SaveTime = DateTimeOffset.Now,
    //             GlobalTokenLimit = _tokenLimit,
    //             Buckets = _tokenBuckets.ToDictionary(k => k.Key, v => v.Value.CurrentToken)
    //         };
    //     }
    //     finally
    //     {
    //         _globalLock.ExitReadLock();
    //     }

    //     try
    //     {
    //         // 写入文件
    //         var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    //         await using var createStream = File.Create(filePath);
    //         await JsonSerializer.SerializeAsync(createStream, snapshot, jsonOptions);

    //         _logger.Info($"TokenBucket 状态已保存, 桶数量: {snapshot.Buckets.Count}");
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error(ex, "保存令牌桶状态失败");
    //     }
    // }
    
    // public async Task LoadStateAsync(string filePath)
    // {
    //     if (!File.Exists(filePath))
    //     {
    //         _logger.Info("不存在旧状态文件, 重置全部状态");
    //         return;
    //     }

    //     TokenBucketSnapshot? snapshot = null;
    //     try
    //     {
    //         await using var openStream = File.OpenRead(filePath);
    //         snapshot = await JsonSerializer.DeserializeAsync<TokenBucketSnapshot>(openStream);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error(ex, "无法加载旧状态文件, 重置全部状态");
    //         return;
    //     }

    //     if (snapshot == null) return;

    //     // --- 判断是否过期 ---
    //     var utcNow = DateTimeOffset.UtcNow;

    //     TimeZoneInfo cstZone;
    //     try
    //     {
    //         cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
    //     }
    //     catch (TimeZoneNotFoundException)
    //     {
    //         cstZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    //     }
        
    //     var now = TimeZoneInfo.ConvertTime(utcNow, cstZone);
    //     var saveTime = snapshot.SaveTime.LocalDateTime;

    //     // 判断逻辑: 如果 保存日期 < 当前日期, 说明已经跨过 0 点了
    //     // 例如: 保存于 10日 23:59, 现在是 11日 00:01 -> 丢弃
    //     // 保存于 11日 01:00, 现在是 11日 02:00 -> 恢复
    //     bool isSameDay = saveTime.Date == now.Date;

    //     if (!isSameDay)
    //     {
    //         _logger.Info($"Save file is from {saveTime} (previous day). Resetting quotas.");
    //         return;
    //     }

    //     // --- 恢复数据 ---
        
    //     _globalLock.EnterWriteLock(); // 写入锁，防止加载过程中有人访问
    //     try
    //     {
    //         // 可选：恢复之前的 Limit 设置
    //         // _tokenLimit = snapshot.GlobalTokenLimit; 

    //         _tokenBuckets.Clear();

    //         foreach (var kvp in snapshot.Buckets)
    //         {
    //             var userId = kvp.Key;
    //             var remainingToken = kvp.Value;
                
    //             // 恢复每一个桶
    //             var bucket = new TokenBucket(_tokenLimit, remainingToken);
    //             _tokenBuckets[userId] = bucket;
    //         }
            
    //         _logger.Info($"Restored {snapshot.Buckets.Count} user buckets from save.");
    //     }
    //     finally
    //     {
    //         _globalLock.ExitWriteLock();
    //     }
    // }

    public void Dispose()
    {
        _globalLock?.Dispose();
    }
}