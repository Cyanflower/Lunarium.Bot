using System.Text.Json;

using LunariumBot.Net.gRPC.Monitoring;

namespace LunariumBot.Net.SearchBot.Model;

// 数据库文件信息
public class DatabaseFileInfo
{
    public string Filename { get; set; } = "";
    public ulong SizeMB { get; set; }
}

// 扩展 gRPC 生成的 PerformanceData，添加 C# 特有的字段
public static class PerformanceDataExtensions
{
    /// <summary>
    /// 将 gRPC 的 PerformanceData 转换为包含 GC 内存信息的完整数据
    /// </summary>
    public static PerformanceDataComplete ToComplete(this PerformanceData grpcData)
    {
        return new PerformanceDataComplete
        {
            // 从 gRPC 数据复制所有字段
            SystemCpuUsagePer = (byte)Math.Round(grpcData.SystemCpuUsagePer),
            ProcessCpuUsagePer = (byte)Math.Round(grpcData.ProcessCpuUsagePer),
            SystemUsedMemoryMB = grpcData.SystemUsedMemoryMb,
            SystemAvailableMemoryMB = grpcData.SystemAvailableMemoryMb,
            SystemCacheMemoryMB = grpcData.SystemCacheMemoryMb,
            SystemFreeMemoryMB = grpcData.SystemFreeMemoryMb,
            ProcessUsedMemoryMB = grpcData.ProcessUsedMemoryMb,
            SystemNetDownloadRateKbps = grpcData.SystemNetDownloadRateKbps,
            SystemNetUploadRateKbps = grpcData.SystemNetUploadRateKbps,

            // 文件夹大小信息
            LogsFolderSizeMB = grpcData.LogsFolderSizeMb,
            DataFolderSizeMB = grpcData.DataFolderSizeMb,
            DatabaseFiles = grpcData.DatabaseFiles.Select(db => new DatabaseFileInfo
            {
                Filename = db.Filename,
                SizeMB = db.SizeMb
            }).ToList(),

            // 添加 C# 特有的 GC 内存信息
            ProcessGCMemoryMB = (ulong)(GC.GetGCMemoryInfo().HeapSizeBytes / 1024 / 1024)
        };
    }
}

// 完整的性能数据类，包含所有字段
public class PerformanceDataComplete
{
    // cpu总使用百分比
    public byte SystemCpuUsagePer { get; set; }
    // 进程所使用的cpu百分比
    public byte ProcessCpuUsagePer { get; set; }
    // 系统目前总内存使用
    public ulong SystemUsedMemoryMB { get; set; }
    // 系统目前总可用内存
    public ulong SystemAvailableMemoryMB { get; set; }
    // 系统目前缓存内存
    public ulong SystemCacheMemoryMB { get; set; }
    // 系统目前空闲内存
    public ulong SystemFreeMemoryMB { get; set; }
    // 进程目前占用的实际内存(Used)
    public ulong ProcessUsedMemoryMB { get; set; }
    // 进程目前的GC托管内存
    public ulong ProcessGCMemoryMB { get; set; }
    // 当前系统网络状态(下载)
    public double SystemNetDownloadRateKbps { get; set; }
    // 当前系统网络状态(上传)
    public double SystemNetUploadRateKbps { get; set; }

    // 新增：文件夹监控
    // logs文件夹总大小
    public ulong LogsFolderSizeMB { get; set; }
    // data文件夹总大小
    public ulong DataFolderSizeMB { get; set; }
    // data文件夹内的数据库文件信息
    public List<DatabaseFileInfo> DatabaseFiles { get; set; } = new();

    // 程序所在磁盘的总已占用大小
    public ulong ProjectDiskUsedMb { get; set; }
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }
}