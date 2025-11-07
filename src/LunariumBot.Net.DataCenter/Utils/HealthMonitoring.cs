using System.Text;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using LunariumBot.Net.Logger;

using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Utils;

public class HealthMonitoring : IDisposable
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

    public HealthMonitoring(DiscordSocketClient discordClient, ILogger logger)
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
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetConnectStates Failed:");
            return $"GetConnectStates Failed:\n{ex}";
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
            if (_connectStates.Count > 96)
            {
                _connectStates.Dequeue();
                _connectStates.Append((DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception));
            }
            else
            {
                _connectStates.Append((DateTimeOffset.Now, connectEvent, status, _discordClient.Latency, exception));
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
                if (_latencys.Count > 96)
                {
                    _latencys.Dequeue();
                    _latencys.Append((DateTimeOffset.Now, status, _discordClient.Latency));
                }
                else
                {
                    _latencys.Append((DateTimeOffset.Now, status, _discordClient.Latency));
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
