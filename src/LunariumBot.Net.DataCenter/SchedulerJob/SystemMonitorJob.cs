using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Discord;
using Discord.Net;
using Discord.WebSocket;

using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.Logger;
using LunariumBot.Net.Scheduler.Interfaces;

namespace LunariumBot.Net.DataCenter.Scheduler;

/// <summary>
/// 一个定时任务，负责执行数据分析、生成报告并通过 Embed 消息推送到各服务器的 Dashboard 频道。
/// 支持多服务器独立推送，每个服务器接收各自的数据分析报告。
/// </summary>
public class SystemMonitorJob : IAsyncJob, IDisposable
{
    // 数据库服务
    private readonly DatabaseService _databaseService;
    // 配置
    private readonly ConfigLoader _configLoader;
    // 日志
    private readonly ILogger _logger;

    // 采样间隔（毫秒）
    private const int SamplingIntervalMs = 100000;

    // 固定系统信息
    private readonly string _osVersion;
    private readonly string _kernelVersion;
    private readonly string _dotnetVersion;
    private readonly int _cpuCount;

    // 库信息
    private readonly string DiscordNetVersion;

    // 性能信息字段
    private double _cpuUsage;
    private long _totalMemoryMB;
    private long _usedMemoryMB;
    private long _freeMemoryMB;
    private long _availableMemoryMB;
    private long _cachedMemoryMB;
    private int _websocketLatency;

    // 后台任务相关
    private readonly CancellationTokenSource _cts;
    private readonly Task _samplingTask;
    private readonly DiscordSocketClient _discordClient;

    // CPU采样相关
    private long _prevTotalTime;
    private long _prevIdleTime;
    private bool _firstSample = true;

    // 频道缓存
    private IMessageChannel? _channelCache;
    private IUserMessage? _messageCache;

    public SystemMonitorJob(DiscordSocketClient discordClient, ILogger logger, ConfigLoader configLoader, DatabaseService databaseService)
    {
        _discordClient = discordClient;
        _databaseService = databaseService;
        _configLoader = configLoader;
        _logger = logger.ForContext("SystemMonitorJob");

        // 获取固定系统信息
        (_osVersion, _kernelVersion, _dotnetVersion, _cpuCount) = GetStaticSystemInfo();

        // 获取框架版本信息
        DiscordNetVersion = GetDiscordNetVersionInfo();

        // 初始化性能信息
        UpdatePerformanceInfo();

        // 启动后台采样任务
        _cts = new CancellationTokenSource();
        _samplingTask = Task.Run(() => SamplingLoop(_cts.Token), _cts.Token);
    }

    public async Task ExecuteAsync()
    {
        try
        {
            Process currentProcess = Process.GetCurrentProcess();
            
            // 虚拟内存大小
            // long virtualMemory = currentProcess.VirtualMemorySize64 / 1024 / 1024;
            // 分页内存大小
            // long pagedMemory = currentProcess.PagedMemorySize64 / 1024 / 1024;
            // 工作集（物理内存）
            long workingSet = currentProcess.WorkingSet64 / 1024 / 1024;
            // 私有内存大小
            long privateMemory = currentProcess.PrivateMemorySize64 / 1024 / 1024;
            // GC托管堆内存
            long gcMemory = GC.GetTotalMemory(false) / 1024 / 1024;

            var dbSize = await _databaseService.GetDatabaseSize();
            var embed = new EmbedBuilder()
                .WithAuthor(_discordClient.CurrentUser)
                .WithTitle("🖥️ 系统信息")
                .WithDescription($"报告于: <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:f> | <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>")
                .AddField("💿 OS 版本", _osVersion, true)
                .AddField("💽 内核版本", _kernelVersion, true)
                .AddField("⚙️ .Net Runtime", _dotnetVersion, true)
                .AddField("📦 Discord.Net", DiscordNetVersion, true)
                .AddField("📶 WebSocket 延迟", $"{_websocketLatency} ms", true)
                .AddField("💾 数据库大小", dbSize ?? "Unknow", true)
                .AddField("🔢 CPU 数量", _cpuCount, true)
                .AddField("📈 CPU 使用率", $"{_cpuUsage:F2} %", true)
                .AddField("📊 内存大小", $"{_totalMemoryMB} MB", true)
                .AddField("📕 内存: Used/Available", $"{_usedMemoryMB} MB / {_availableMemoryMB} MB", true)
                .AddField("📗 内存: Free", $"{_freeMemoryMB} MB", true)
                .AddField("📘 内存: Cache", $"{_cachedMemoryMB} MB", true)
                // .AddField("🧾 虚拟内存", $"{virtualMemory} MB", true)
                // .AddField("📑 分页内存", $"{pagedMemory} MB", true)
                .AddField("📋 工作集", $"{workingSet} MB", true)
                .AddField("🗒 私有内存大小", $"{privateMemory} MB", true)
                .AddField("🗑 GC托管堆内存", $"{gcMemory} MB", true)
                .WithFooter("系统监控")
                .WithTimestamp(DateTimeOffset.Now)
                .WithThumbnailUrl(_discordClient.CurrentUser.GetAvatarUrl())
                .Build();
    
            if (_channelCache is null)
            {
                _channelCache = await _discordClient.GetChannelAsync(_configLoader.Config.AuthConfig.LogChannel) as IMessageChannel;
            }
    
            if (_channelCache is not null)
            {
                if (_messageCache is null)
                {
                    _messageCache = await _channelCache.SendMessageAsync(embed: embed);
                }
                else
                {
                    try
                    {
                        await _messageCache.ModifyAsync(prop =>
                        {
                            prop.Embed = embed;
                        });
                    }
                    catch (HttpException ex)
                    {
                        if ((int)ex.HttpCode == 404)
                        {
                            _messageCache = await _channelCache.SendMessageAsync(embed: embed);
                        }
                        else
                        {
                            _logger.Error(ex, "❌ <JobTask>: SystemMonitorJob.ExecuteAsync HttpException");
                        }
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ <JobTask>: SystemMonitorJob.ExecuteAsync Exception");
        }
    }

    /// <summary>
    /// 获取固定的系统信息
    /// </summary>
    private static (string osVersion, string kernelVersion, string dotnetVersion, int cpuCount) GetStaticSystemInfo()
    {
        // 1. 获取OS版本（从 /etc/os-release 读取 PRETTY_NAME）
        string osVersion = "Unknown";
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var lines = File.ReadAllLines("/etc/os-release");
                var prettyName = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                if (prettyName != null)
                {
                    osVersion = prettyName.Split('=')[1].Trim('"');
                }
            }
        }
        catch { }

        // 2. 获取内核版本（执行 uname -r）
        string kernelVersion = "Unknown";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "uname",
                Arguments = "-r",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                kernelVersion = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
            }
        }
        catch
        {
            // 备用方案：从 RuntimeInformation.OSDescription 提取
            try
            {
                var osDesc = RuntimeInformation.OSDescription;
                // 例如: "Linux 6.1.0-28-amd64 #1 SMP PREEMPT_DYNAMIC Debian 6.1.119-1 (2024-11-22)"
                var parts = osDesc.Split(' ');
                if (parts.Length > 1 && parts[0] == "Linux")
                {
                    kernelVersion = parts[1];
                }
            }
            catch { }
        }

        // 3. 获取 .NET 运行时版本
        string dotnetVersion = RuntimeInformation.FrameworkDescription;

        // 4. 获取 CPU 数量
        int cpuCount = Environment.ProcessorCount;

        return (osVersion, kernelVersion, dotnetVersion, cpuCount);
    }

    private static string GetDiscordNetVersionInfo()
    {
        Assembly discordAssembly = typeof(DiscordSocketClient).Assembly;

        AssemblyName assemblyName = discordAssembly.GetName();

        Version? version = assemblyName.Version;

        return version is not null? version.ToString() : ">= 3.18.0";
    }

    /// <summary>
    /// 更新性能信息
    /// </summary>
    private void UpdatePerformanceInfo()
    {
        // 更新内存信息
        UpdateMemoryInfo();

        // 更新 WebSocket 延迟
        if (_discordClient != null)
        {
            _websocketLatency = _discordClient.Latency;
        }
    }

    /// <summary>
    /// 更新内存信息（从 /proc/meminfo 读取）
    /// </summary>
    private void UpdateMemoryInfo()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
                return;

            var lines = File.ReadAllLines("/proc/meminfo");
            var memInfo = lines
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0].Trim(),
                    parts => ParseMemoryValue(parts[1])
                );

            _totalMemoryMB = memInfo.GetValueOrDefault("MemTotal", 0) / 1024;
            _freeMemoryMB = memInfo.GetValueOrDefault("MemFree", 0) / 1024;
            _availableMemoryMB = memInfo.GetValueOrDefault("MemAvailable", 0) / 1024;
            _cachedMemoryMB = memInfo.GetValueOrDefault("Cached", 0) / 1024;
            _usedMemoryMB = _totalMemoryMB - _availableMemoryMB;
        }
        catch { }
    }

    /// <summary>
    /// 解析内存值（从 "123456 kB" 格式提取数字）
    /// </summary>
    private static long ParseMemoryValue(string value)
    {
        var parts = value.Trim().Split(' ');
        return parts.Length > 0 && long.TryParse(parts[0], out long result) ? result : 0;
    }

    /// <summary>
    /// 后台采样循环
    /// </summary>
    private async Task SamplingLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 计算 CPU 使用率
                UpdateCpuUsage();

                // 更新其他性能信息
                UpdatePerformanceInfo();

                // 等待采样间隔
                await Task.Delay(SamplingIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 忽略采样错误，继续下次采样
            }
        }
    }

    /// <summary>
    /// 更新 CPU 使用率（通过 /proc/stat 计算）
    /// </summary>
    private void UpdateCpuUsage()
    {
        try
        {
            if (!File.Exists("/proc/stat"))
                return;

            var firstLine = File.ReadLines("/proc/stat").First();
            var values = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1) // 跳过 "cpu" 标签
                .Select(long.Parse)
                .ToArray();

            // 计算总时间和空闲时间
            long totalTime = values.Sum();
            long idleTime = values[3]; // idle 是第4个值（索引3）

            if (!_firstSample)
            {
                // 计算差值
                long totalDiff = totalTime - _prevTotalTime;
                long idleDiff = idleTime - _prevIdleTime;

                if (totalDiff > 0)
                {
                    _cpuUsage = (totalDiff - idleDiff) / (double)totalDiff * 100.0;
                }
            }
            else
            {
                _firstSample = false;
            }

            // 保存当前值供下次计算
            _prevTotalTime = totalTime;
            _prevIdleTime = idleTime;
        }
        catch { }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _samplingTask?.Wait(TimeSpan.FromSeconds(20));
        }
        catch { }
        _cts?.Dispose();
    }
}