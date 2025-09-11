using Microsoft.Extensions.Hosting;

using Grpc.Core;
using Grpc.Net.Client;

using LunariumBot.Net.gRPC.Monitoring;
using static LunariumBot.Net.gRPC.Monitoring.PerformanceMonitor;

using LunariumBot.Net.Logger;

using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot;

public class PerformanceMonitoringService : IHostedService
{
    private readonly GrpcChannel _channel;
    private readonly PerformanceMonitorClient _client;
    private readonly ILogger _logger;

    private CancellationTokenSource? _perfSubCancelTokenSource = new CancellationTokenSource();
    private Task? _subscribePerformanceData;
    private SystemInfo? systemInfo;

    public PerformanceMonitoringService(ILogger logger, string address)
    {
        // 创建专门为此服务配置的 SocketsHttpHandler
        var socketsHandler = new SocketsHttpHandler()
        {
            UseProxy = false,  // 禁用代理
            // 启用 HTTP/2 未加密支持（仅对此 handler 有效）
            EnableMultipleHttp2Connections = true,
            // 其他 HTTP/2 相关配置
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            // 连接超时设置
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = socketsHandler,
            Credentials = ChannelCredentials.Insecure,
            // 明确指定使用 HTTP/2
            HttpVersion = new Version(2, 0)
        });
        
        _logger = logger.ForContext("PerformanceMonitoringService");
        _client = new PerformanceMonitorClient(_channel);
        _logger.Info("性能监控服务已初始化，目标地址: {address}", address);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            systemInfo = await GetSystemInfoAsync();
            if (systemInfo == null)
            {
                // 如果获取失败
                throw new InvalidOperationException("未能通过gRPC获取到性能探针信息, 服务程序可能未在运行, 将不会启动性能监控流");
            }
            // 开始性能监控
            _subscribePerformanceData = Task.Run(() => SubscribePerformanceDataAsync(( _ ) => { }, _perfSubCancelTokenSource!.Token), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error("性能监控启动失败, 将不会启动性能监控流, 异常:", ex);
                // 关闭自己
            await StopAsync(cancellationToken);
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("⏳ 正在停止性能监控服务...");
        // --- 释放资源 ---
        // 取消gRPC调用
        _perfSubCancelTokenSource?.Cancel(); // 使用 ?. 避免在 StartAsync 失败时抛出异常

        if (_subscribePerformanceData != null)
        {
            try
            {
                // 给一个小的超时时间，防止任务卡住导致程序无法正常关闭
                await _subscribePerformanceData.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 这是我们期望的取消异常，很正常！
                _logger.Debug("订阅任务已成功取消。");
            }
            catch (TimeoutException)
            {
                _logger.Warning("等待订阅任务关闭超时。");
            }
            catch(Exception ex)
            {
                 _logger.Error("等待订阅任务关闭时出现意外异常。", ex);
            }
        }
        
        _subscribePerformanceData = null; // 在这里释放任务引用
        _logger.Info("✅ 性能监控服务已成功停止。");
    }

    /// <summary>
    /// 获取一次性的系统信息
    /// </summary>
    /// <returns>系统信息，如果失败则返回 null</returns>
    public async Task<SystemInfo?> GetSystemInfoAsync()
    {
        try
        {
            _logger.Debug("正在请求系统信息...");
            var systemInfo = await _client.GetSystemInfoAsync(new Empty());
            _logger.Info("成功获取系统信息:");
            _logger.Info("  - CPU 型号: {systemInfo.SystemCpuName}",systemInfo.SystemCpuName);
            _logger.Info("  - CPU 线程: {systemInfo.SystemCpuCoreThread}",systemInfo.SystemCpuCoreThread);
            _logger.Info("  - CPU 频率: {systemInfo.CpuFrequency} Ghz",systemInfo.CpuFrequency / 1000.0);
            _logger.Info("  - MEM 容量: {systemInfo.SystemMemoryTotalMb} MB",systemInfo.SystemMemoryTotalMb);
            _logger.Info("  - DISK大小: {systemInfo.DiskTotalMb} MB",systemInfo.DiskTotalMb);
            return systemInfo;
        }
        catch (RpcException ex)
        {
            _logger.Error(ex,"请求系统信息失败: {ex.Status}", ex.Status);
            return null;
        }
    }

    /// <summary>
    /// 订阅性能数据流
    /// </summary>
    /// <param name="onDataReceived">接收到数据时的回调函数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="intervalSeconds">刷新间隔（秒）</param>
    public async Task SubscribePerformanceDataAsync(
        Action<PerformanceDataComplete> onDataReceived,
        CancellationToken cancellationToken,
        int intervalSeconds = 10)
    {
        // 获取当前 C# 进程的 PID
        var currentProcessId = Environment.ProcessId;

        var request = new SubscriptionRequest
        {
            IntervalSeconds = intervalSeconds,
            TargetProcessPid = currentProcessId  // 传递当前进程的 PID
        };

        _logger.Info("正在订阅性能数据流，刷新间隔 {intervalSeconds} 秒，目标进程 PID: {currentProcessId}...", intervalSeconds, currentProcessId);

        try
        {
            // 发起流式调用
            using var call = _client.SubscribePerformanceData(request, cancellationToken: cancellationToken);

            // 使用 IAsyncEnumerable 优雅地处理数据流
            await foreach (var dataFromGo in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                // 将 gRPC 数据转换为完整的性能数据对象（包含 GC 内存）
                var completeData = dataFromGo.ToComplete();
                _logger.Info(completeData.ToJson());
                // _logger.Debug($"""
                //     --- Performance Snapshot ---
                //     SysCPU Usage : {completeData.SystemCpuUsagePer}%
                //     PrcCPU Usage : {completeData.ProcessCpuUsagePer}%
                //     SysMEM Used  : {completeData.SystemUsedMemoryMB} MB
                //     SysMEM Avbl  : {completeData.SystemAvailableMemoryMB} MB
                //     SysMEM Cache : {completeData.SystemCacheMemoryMB} MB
                //     SysMEM Free  : {completeData.SystemFreeMemoryMB} MB
                //     PrcMEM Used  : {completeData.ProcessUsedMemoryMB} MB
                //     PrcMEM GC    : {completeData.ProcessGCMemoryMB} MB
                //     Net Download : {completeData.SystemNetDownloadRateKbps:F2} Kbps
                //     Net Upload   : {completeData.SystemNetUploadRateKbps:F2} Kbps
                //     Logs Folder  : {completeData.LogsFolderSizeMB} MB
                //     Data Folder  : {completeData.DataFolderSizeMB} MB
                //     Project Disk : {completeData.ProjectDiskUsedMb} MB
                //     DB Files     : {(completeData.DatabaseFiles.Any() ? string.Join(", ", completeData.DatabaseFiles.Select(f => $"{f.Filename}({f.SizeMB}MB)")) : "None")}
                //     --------------------------
                //     """);

                // 通过回调函数将整合后的数据传递出去
                onDataReceived(completeData);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // 这是用户主动取消，是正常操作
            _logger.Warning("性能数据订阅被主动取消。");
        }
        catch (RpcException ex)
        {
            _logger.Error(ex, "订阅性能数据时发生 gRPC 错误: {ex.Status}", ex.Status);
        }
        catch (Exception ex)
        {
            _logger.Error("处理性能数据流时发生未知错误。", ex);
        }

        _logger.Info("性能数据流已关闭。");
    }

    /// <summary>
    /// 实现 IAsyncDisposable 以便优雅地关闭 Channel
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.Info("⏳ 正在释放 gRPC 资源...");
        
        // 释放 CancellationTokenSource
        _perfSubCancelTokenSource?.Dispose();
        _perfSubCancelTokenSource = null;

        // 关闭 gRPC 通道
        if (_channel != null)
        {
            await _channel.ShutdownAsync();
        }
        _logger.Info("✅ gRPC 通道已关闭。");
        // GC.SuppressFinalize(this); // 告诉垃圾回收器不需要再调用终结器了
    }
}