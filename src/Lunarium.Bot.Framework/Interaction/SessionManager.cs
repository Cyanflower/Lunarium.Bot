using System.Collections.Concurrent;
using Lunarium.Logging;
using Lunarium.Common.UID;
using Lunarium.Common;

namespace Lunarium.Bot.Framework.Interaction;

public class SessionManager : IDisposable
{
    private readonly ILogger _logger;
    const int MaxRetry = 5;
    private readonly string _alphabet;
    private readonly int _expireHours = 1; // 小时
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _cleanupTask;
    private volatile bool _isDisposed = false;
    private volatile bool _isBroken = false;
    private readonly ConcurrentDictionary<string, IInteractionSession> _sessions = new();

    public SessionManager(ILogger logger, string keyAlphabet = Alphabet.Base, int expiresHours = 1)
    {
        _logger = logger.ForContext("SessionManager");
        _alphabet = keyAlphabet;
        _expireHours = expiresHours;
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        _cleanupTask = Task.Run(async () => { await CleanupLoopAsync(); });
    }

    public TSession Register<TSession>(ISessionContext<TSession> context, int nanoLength = 10) where TSession : IInteractionSession
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SessionManager));
        if (_isBroken)
            throw new InvalidOperationException("SessionManager is out of service due to repeated failures.");

        var key = $"{NanoId.Generate(nanoLength, _alphabet)}";

        var expiresAt = DateTimeOffset.UtcNow.AddHours(_expireHours);

        // 自动推导 Context 给的实际 Session 类型
        var session = context.CreateSession(key, expiresAt);

        // 安全向上转型
        _sessions.TryAdd(key, session);
        return session;
    }

    public TSession Register<TSession>(ISessionContext<TSession> context, string prefix, int nanoLength = 10) where TSession : IInteractionSession
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SessionManager));
        if (_isBroken)
            throw new InvalidOperationException("SessionManager is out of service due to repeated failures.");
        if (prefix.Contains('|'))
            throw new ArgumentException("prefix Cannot Contains '|'");

        var key = $"{prefix}|{NanoId.Generate(nanoLength, _alphabet)}";


        var expiresAt = DateTimeOffset.UtcNow.AddHours(_expireHours);

        // 自动推导 Context 给的实际 Session 类型
        var session = context.CreateSession(key, expiresAt);

        // 安全向上转型
        _sessions.TryAdd(key, session);
        return session;
    }
    // 泛型 Get
    // TSession 必须是 IInteractionsCache 的实现类
    public TSession? Get<TSession>(string key) where TSession : class, IInteractionSession
    {
        // 复用上面的逻辑, 确保"续命"被执行
        var session = Get(key);

        // 尝试转换, 如果类型不匹配, 例如 ID 存在但对应的是 SearchCache 而不是 FavCache,这里会返回 null
        return session as TSession;
    }

    public bool TryGet<TSession>(string key, out TSession? session) where TSession : class, IInteractionSession
    {
        // 复用上面的逻辑, 确保"续命"被执行
        session = Get<TSession>(key);

        // 尝试转换, 如果类型不匹配, 例如 ID 存在但对应的是 SearchCache 而不是 FavCache,这里会返回 null
        return session is not null;
    }

    public IInteractionSession? Get(string key)
    {
        if (_isDisposed || _isBroken) return null;

        if (_sessions.TryGetValue(key, out var session))
        {
            // 只要用户还在访问(交互)，就给它续命 1 小时 (续命? 续命! +1h.jpg)
            session.RefreshExpiration(TimeSpan.FromHours(_expireHours));
            return session;
        }
        return null;
    }

    public void TryRemove(string key)
    {
        _sessions.TryRemove(key, out _);
    }

    private async Task CleanupLoopAsync()
    {
        var retryCount = 0;
        var token = _cts.Token;

        try
        {
            while (await _timer.WaitForNextTickAsync(token))
            {
                try
                {
                    if (_sessions.IsEmpty) continue;

                    var now = DateTimeOffset.UtcNow;

                    // 核心清理逻辑
                    foreach (var session in _sessions.Values)
                    {
                        // 再次检查取消, 避免在繁重的循环中卡死
                        if (token.IsCancellationRequested) return;

                        if (now >= session.ExpiresAt)
                        {
                            _sessions.TryRemove(session.SessionId, out _);
                        }
                    }

                    // 如果成功执行完一轮, 重置重试计数器
                    retryCount = 0;
                }
                catch (Exception ex)
                {
                    // 这里捕获的是 业务逻辑/集合操作 的异常，而不是 Timer 的异常
                    // 如果是正在关闭引发的异常，直接退出
                    if (token.IsCancellationRequested) return;

                    retryCount++;
                    _logger.Error(ex, "CleanupLoop Error: {retryCount}/{MaxRetry}", retryCount, MaxRetry);

                    if (retryCount >= MaxRetry)
                    {
                        _isBroken = true;
                        // 清空缓存
                        _sessions.Clear();
                        _logger.Critical("SessionManager Circuit Broken: Too many errors.");
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常的取消流程
        }
        catch (Exception ex)
        {
            // 这里捕获的是 Timer 本身可能的异常
            if (!_isDisposed)
            {
                _logger.Critical(ex, "SessionManager Critical Timer Failure.");
                _isBroken = true;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}