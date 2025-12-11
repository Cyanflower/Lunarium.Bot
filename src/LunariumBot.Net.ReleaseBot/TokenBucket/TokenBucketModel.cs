using LunariumBot.Net.Extensions;
using LunariumBot.Net.ReleaseBot.Utils;

namespace LunariumBot.Net.ReleaseBot.TokenBucketServices;


public class TokenBucketSnapshot
{
    // 保存的时间
    public DateTimeOffset SaveTime { get; set; }


    // 当时的全局限流配置
    public int GlobalTokenLimit { get; set; }

    // 用户ID -> 剩余 Token 数量
    public Dictionary<ulong, int> Buckets { get; set; } = new();
}

public class TokenBucket
{
    private int _tokenLimit;
    private int _token;
    private HashSet<ulong> hasTokenThreads = new();
    private Lock _lock = new();

    public TokenBucket(int tokenLimit, int? currentToken = null)
    {
        _tokenLimit = tokenLimit;
        // 如果没有传入当前token, 则默认为满
        _token = currentToken ?? tokenLimit;
    }

    public bool GetToken(ulong threadId, int token)
    {
        lock (_lock)
        {
            if (hasTokenThreads.Contains(threadId))
            {
                return true;
            }
            if (token == 0)
            {
                return true;
            }
            if (_token > 0)
            {
                _token = _token - token;
                hasTokenThreads.Add(threadId);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public void UndoGetToken(ulong threadId)
    {
        lock (_lock)
        {
            if (hasTokenThreads.Contains(threadId))
            {
                hasTokenThreads.Remove(threadId);
                if (_token < _tokenLimit)
                {
                    _token++;
                }
            }
        }
    }

    public void ResumeToken()
    {
        lock (_lock)
        {
            _token = _tokenLimit;
        }
        
    }

    public void SetTokenLimit(int tokenLimit)
    {
        lock (_lock)
        {
            _tokenLimit = tokenLimit;
            if (_token > _tokenLimit)
            {
                _token = _tokenLimit;
            }
        }
    }
    
    public int CurrentToken
    {
        get
        {
            lock (_lock)
            {
                return _token;
            }
        }
    }
}