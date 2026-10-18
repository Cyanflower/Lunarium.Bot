
using Lunarium.Scheduler;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using Lunarium.Logging;

namespace Lunarium.Bot.Framework.Health;

public class HealthMonitor : IDisposable
{
    public record Latency(DateTimeOffset Time, string State, int LatencyMs)
    {
        public override string ToString()
        {
            return $"[{Time:O}] [State: {State}] [Latency: {LatencyMs} ms]";
        }
    }
    public record ConnectState(DateTimeOffset Time, string ConnectEvent, string State, int Latency, Exception? Ex)
    {
        public override string ToString()
        {
            if (Ex is null)
            {
                return $"[{Time:O}] [Event: {ConnectEvent}] [State: {State}] [Latency: {Latency}]";
            }
            return $"[{Time:O}] [Event: {ConnectEvent}] [State: {State}] [Latency: {Latency}] [Exception: {Ex.Message}]";
        }
    }

    private readonly DiscordSocketClient _discordClient;
    private readonly ILogger _logger;
    private readonly HealthMonitorConfig _config;
    private readonly CancellationTokenSource _cts;
    private readonly Task _heartbeatLoopTask;
    private Func<Task> _onConnected;
    private Func<Exception, Task> _onDisconnected;
    private bool _handlersAttached = false;
    // WebSocket 延迟信息队列: 定时查询驱动
    private readonly Queue<Latency> _latencys = new();
    private readonly Lock _latencyLock = new();
    
    // 客户端连接状态信息队列: 连接事件驱动
    private readonly Queue<ConnectState> _connectStates = new();
    private readonly Lock _connectStateLock = new();

    private const int QueueLengthLimit = 96;

    // JSON 序列化选项: 无缩进(即无多余空格/换行), 且不转义非 ASCII 字符
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public HealthMonitor(DiscordSocketClient discordClient, ILogger logger, HealthMonitorConfig config)
    {
        _config = config;
        _discordClient = discordClient;
        _logger = logger.ForContext("HealthMonitoring");
        _logger.Info(
            "健康监控配置: HTTP 服务 {HttpEnabled} (端口 {HttpPort}), 心跳上报 {HeartbeatEnabled} -> {HeartbeatTarget}",
            config.EnableHttpServer, config.HttpPort, config.EnableHeartbeat, config.HeartbeatTarget);
        _cts = new CancellationTokenSource();
        _heartbeatLoopTask = Task.Run(() => HeartbeatLoop(_cts.Token), _cts.Token);
        _onConnected = () =>
        {
            ConnectEventHandler("Connected");
            return Task.CompletedTask;
        };

        _onDisconnected = (ex) =>
        {
            ConnectEventHandler("Disconnected", ex);
            return Task.CompletedTask;
        };
    }

    public void AddConnectEventHandler()
    {
        if (_handlersAttached) return;
        try
        {
            _handlersAttached = true;
            _discordClient.Connected += _onConnected;
            _discordClient.Disconnected += _onDisconnected;
            _logger.Info("✅ 连接事件处理方法已添加");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 连接事件处理方法添加失败, 正在执行清理...");
            throw;
        }
    }

    public void RemoveConnectEventHandler()
    {
        if (!_handlersAttached) return;
        try
        {
            if (_onConnected != null)
                _discordClient.Connected -= _onConnected;
            if (_onDisconnected != null)
                _discordClient.Disconnected -= _onDisconnected;
            _logger.Info("✅ 连接事件处理方法已移除");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 连接事件处理方法移除失败");
            throw;
        }
        finally
        {
            _handlersAttached = false;
        }
    }

    public IReadOnlyList<string> GetLatencyLogs(int sampleCount = 5, int windowSize = 32)
    {
        try
        {
            VerifyArgument(sampleCount, windowSize);

            var result = UniformSample<Latency>(GetLatencies(windowSize), sampleCount);

            return result.Select(x => x.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetLatencys Failed");
            return [$"GetLatencys Failed: {ex.Message}"];
        }
    }

    public IReadOnlyList<string> GetConnectStateLogs(int sampleCount = 5, int windowSize = 32)
    {
        try
        {
            VerifyArgument(sampleCount, windowSize);

            var result = UniformSample<ConnectState>(GetContentStatus(windowSize), sampleCount);

            return result.Select(x => x.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetConnectStates Failed");
            return [$"GetConnectStates Failed: {ex.Message}"];
        }
    }

    private static void VerifyArgument(int sampleCount, int windowSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(windowSize, QueueLengthLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleCount, windowSize);
    }

    /// <summary>
    /// 从 <paramref name="items"/> 中均匀选取 <paramref name="count"/> 个元素。
    /// 元素不足时返回全部; <paramref name="count"/> 小于等于 0 时返回空数组。
    /// </summary>
    private static T[] UniformSample<T>(T[] items, int count)
    {
        if (count <= 0 || items.Length == 0) return Array.Empty<T>();
        if (items.Length <= count) return items;

        var result = new T[count];
        if (count == 1)
        {
            result[0] = items[items.Length - 1];
            return result;
        }

        for (var i = 0; i < count; i++)
        {
            var index = (int)Math.Round(i * (items.Length - 1) / (double)(count - 1));
            result[i] = items[index];
        }
        return result;
    }

    private Latency[] GetLatencies(int count)
    {
        lock (_latencyLock)
        {
            return _latencys.TakeLast(count).ToArray();
        }
    }

    private ConnectState[] GetContentStatus(int count)
    {
        lock (_connectStateLock)
        {
            return _connectStates.TakeLast(count).ToArray();
        }
    }

    private void ConnectEventHandler(string connectEvent, Exception? exception = null)
    {
        try
        {
            var status = _discordClient.ConnectionState switch
            {
                ConnectionState.Connected => "Connected",
                ConnectionState.Connecting => "Connecting",
                ConnectionState.Disconnected => "Disconnected",
                ConnectionState.Disconnecting => "Disconnecting",
                _ => "Unknow State"
            };

            var entry = new ConnectState(DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception);


            lock (_connectStateLock)
            {
                if (_connectStates.Count >= QueueLengthLimit)
                {
                    _connectStates.Dequeue();
                }
                _connectStates.Enqueue(entry);
            }

            _logger.Debug("{entry}", entry);
            
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <LoopTask>: ConnectEventHandler Exception");
        }
    }

    private async Task HeartbeatLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var status = _discordClient.ConnectionState switch
                {
                    ConnectionState.Connected => "Connected",
                    ConnectionState.Connecting => "Connecting",
                    ConnectionState.Disconnected => "Disconnected",
                    ConnectionState.Disconnecting => "Disconnecting",
                    _ => "Unknow State"
                };

                var entry = new Latency(DateTimeOffset.Now, status, _discordClient.Latency);

                lock (_latencyLock)
                {
                    if (_latencys.Count >= QueueLengthLimit)
                    {
                        _latencys.Dequeue();
                    }
                    _latencys.Enqueue(entry);
                }

                _logger.Debug("{entry}", entry);

                await Task.Delay(30000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("✅ HeartbeatLoop 已根据关闭请求正常停止。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <LoopTask>: HeartbeatLoop Exception");
        }
    }
    
    public void Dispose()
    {
        RemoveConnectEventHandler();
        _cts?.Cancel();
        try
        {
            _heartbeatLoopTask?.Wait(TimeSpan.FromSeconds(60));
        }
        catch { }
        _cts?.Dispose();
    }
}
