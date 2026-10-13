using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using Lunarium.Logging;

namespace Lunarium.Bot.Extensions;

public class HealthMonitor : IDisposable
{
    private readonly DiscordSocketClient _discordClient;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private readonly Task _heartbeatLoopTask;
    private Func<Task> _onConnected;
    private Func<Exception, Task> _onDisconnected;
    private bool _handlersAttached = false;
    // WebSocket 延迟信息队列: 定时查询驱动
    private readonly Queue<(DateTimeOffset Time, string State, int Latency)> _latencys = new();
    
    // 客户端连接状态信息队列: 连接事件驱动
    private readonly Queue<(DateTimeOffset Time, string connectEvent, string State, int Latency, Exception? ex)> _connectStates = new();

    // JSON 序列化选项: 无缩进(即无多余空格/换行), 且不转义非 ASCII 字符
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>JSON 重载默认采样条数: 从最多 96 条记录中均匀抽取的条数。</summary>
    public const int MonitorSampleCount = 5;

    public HealthMonitor(DiscordSocketClient discordClient, ILogger logger)
    {
        _discordClient = discordClient;
        _logger = logger.ForContext("HealthMonitoring");
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
    public string GetLatencysLog()
    {
        try
        {
            var sb = new StringBuilder();
            var items = _latencys.ToArray();
            foreach (var latency in items.Take(96))
            {
                sb.AppendLine($"[{latency.Time:O}] [State: {latency.State}] [Latency: {latency.Latency}]");
            }
            _logger.Debug("GetLatencysLog: {GetLatencysLog}", sb.ToString());
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetLatencys Failed:");
            return $"GetLatencys Failed:\n{ex}";
        }
    }
    public string GetConnectStatesLog()
    {
        try
        {
            var sb = new StringBuilder();
            var items = _connectStates.ToArray();
            foreach (var connectStates in items.Take(96))
            {
                sb.AppendLine($"[{connectStates.Time:O}] [Event: {connectStates.connectEvent}] [State: {connectStates.State}] [Latency: {connectStates.Latency}] [Exception: {(connectStates.ex is null ? "null" : connectStates.ex.Message)}]");
            }
            _logger.Debug("GetConnectStatesLog: {GetConnectStatesLog}", sb.ToString());
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetConnectStates Failed:");
            return $"GetConnectStates Failed:\n{ex}";
        }
    }

    /// <summary>
    /// 重载版本: 返回序列化后的 JSON 字符串(无空格、无换行)。
    /// 从当前缓存的记录中均匀选取 <paramref name="sampleCount"/> 条(默认 5 条, 即从 96 条中均匀取 5 条)。
    /// </summary>
    public string GetLatencysLog(int sampleCount = MonitorSampleCount)
    {
        try
        {
            var items = UniformSample(_latencys.ToArray(), sampleCount)
                .Select(l => new
                {
                    time = l.Time,
                    state = l.State,
                    latency = l.Latency
                })
                .ToArray();
            var json = JsonSerializer.Serialize(items, JsonOptions);
            _logger.Debug("GetLatencysLog(Json): {GetLatencysLog}", json);
            return json;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetLatencys(Json) Failed:");
            return "[]";
        }
    }

    /// <summary>
    /// 重载版本: 返回序列化后的 JSON 字符串(无空格、无换行)。
    /// 从当前缓存的记录中均匀选取 <paramref name="sampleCount"/> 条(默认 5 条, 即从 96 条中均匀取 5 条)。
    /// </summary>
    public string GetConnectStatesLog(int sampleCount = MonitorSampleCount)
    {
        try
        {
            var items = UniformSample(_connectStates.ToArray(), sampleCount)
                .Select(c => new
                {
                    time = c.Time,
                    @event = c.connectEvent,
                    state = c.State,
                    latency = c.Latency,
                    exception = c.ex?.Message
                })
                .ToArray();
            var json = JsonSerializer.Serialize(items, JsonOptions);
            _logger.Debug("GetConnectStatesLog(Json): {GetConnectStatesLog}", json);
            return json;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetConnectStates(Json) Failed:");
            return "[]";
        }
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
            if (_connectStates.Count >= 96)
            {
                _connectStates.Dequeue();
                _connectStates.Enqueue((DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception));
                _logger.Debug("{entry}", (DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception));
            }
            else
            {
                _connectStates.Enqueue((DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception));
                _logger.Debug("{entry}", (DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception));
            }
            
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <LoopTask>: HeartbeatLoop Exception");
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
                if (_latencys.Count >= 96)
                {
                    _latencys.Dequeue();
                    _latencys.Enqueue((DateTimeOffset.Now, status, _discordClient.Latency));
                    _logger.Debug("{entry}", (DateTimeOffset.Now, status, _discordClient.Latency));
                }
                else
                {
                    _latencys.Enqueue((DateTimeOffset.Now, status, _discordClient.Latency));
                    _logger.Debug("{entry}", (DateTimeOffset.Now, status, _discordClient.Latency));
                }
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
        _cts?.Cancel();
        try
        {
            _heartbeatLoopTask?.Wait(TimeSpan.FromSeconds(60));
        }
        catch { }
        _cts?.Dispose();
    }
}
