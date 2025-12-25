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
    private int Token => _token;
    private int TokenLimit => _tokenLimit;
    private int _tokenLimit;
    private int _token;
    private Lock _lock = new();

    public TokenBucket(int tokenLimit, int? currentToken = null)
    {
        _tokenLimit = tokenLimit;
        _token = currentToken ?? tokenLimit;
    }

    public bool GetToken(int token)
    {
        lock (_lock)
        {
            if (token == 0)
            {
                return true;
            }
            if (_token - token > 0)
            {
                _token = _token - token;
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public bool CheckToken(int token)
    {
        lock (_lock)
        {
            if (token == 0)
            {
                return true;
            }
            return _token - token > 0;
        }
    }

    public void UndoGetToken(int token)
    {
        lock (_lock)
        {
            if (token == 0)
            {
                return;
            }
            if (_token < _tokenLimit)
            {
                _token = _token + token;
                if (_token > _tokenLimit)
                {
                    _token = _tokenLimit;
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