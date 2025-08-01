// Core/Performance.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SearchBot.Core
{
    /// <summary>
    /// 性能监控服务，每分钟统计CPU和内存使用情况
    /// </summary>
    public class PerformanceMonitorService : IHostedService, IDisposable
    {
        private readonly ILogger<PerformanceMonitorService> _logger;
        private Timer? _timer;
        private readonly Process _currentProcess;
        private DateTime _lastCpuTime;
        private TimeSpan _lastTotalProcessorTime;

        public PerformanceMonitorService(ILogger<PerformanceMonitorService> logger)
        {
            _logger = logger;
            _currentProcess = Process.GetCurrentProcess();
            _lastCpuTime = DateTime.UtcNow;
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔍 性能监控服务启动");
            
            // 每60秒执行一次性能统计
            _timer = new Timer(
                callback: _ => LogPerformanceMetrics(),
                state: null,
                dueTime: TimeSpan.FromMinutes(1), // 1分钟后开始
                period: TimeSpan.FromMinutes(1)   // 每1分钟执行
            );

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔍 性能监控服务停止");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private void LogPerformanceMetrics()
        {
            try
            {
                // 获取内存使用情况
                var memoryUsed = _currentProcess.WorkingSet64;
                var memoryUsedMB = memoryUsed / (1024.0 * 1024.0);

                // 计算CPU使用率
                var currentTime = DateTime.UtcNow;
                var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;
                
                var cpuUsedTime = currentTotalProcessorTime - _lastTotalProcessorTime;
                var realTime = currentTime - _lastCpuTime;
                
                var cpuUsagePercent = cpuUsedTime.TotalMilliseconds / realTime.TotalMilliseconds * 100;

                // 更新上次记录的时间
                _lastCpuTime = currentTime;
                _lastTotalProcessorTime = currentTotalProcessorTime;

                // 获取系统信息
                var gcMemory = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

                _logger.LogInformation(
                    "📊 性能统计 | CPU: {CpuUsage:F1}% | 物理内存: {MemoryMB:F1}MB | GC内存: {GcMemoryMB:F1}MB", 
                    cpuUsagePercent, 
                    memoryUsedMB,
                    gcMemory
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 获取性能指标时发生错误");
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _currentProcess?.Dispose();
        }
    }
}
