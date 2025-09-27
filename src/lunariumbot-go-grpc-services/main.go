package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"strings"
	"time"
	gonet "net"

	"google.golang.org/grpc"

	// 导入生成的 gRPC 代码
	pb "lunariumbot-go-grpc-services/internal/pb/monitoring"

	"github.com/shirou/gopsutil/v3/cpu"
	"github.com/shirou/gopsutil/v3/disk"
	"github.com/shirou/gopsutil/v3/mem"
	"github.com/shirou/gopsutil/v3/net"
	"github.com/shirou/gopsutil/v3/process"
)

// 1. 定义一个 server 结构体，它将实现 proto 文件中定义的 PerformanceMonitorServer 接口
type monitoringServer struct {
	pb.UnimplementedPerformanceMonitorServer // 嵌入这个结构体可以保证向前兼容
}

// 2. 实现 GetSystemInfo 方法
func (s *monitoringServer) GetSystemInfo(ctx context.Context, in *pb.Empty) (*pb.SystemInfo, error) {
	log.Println("收到 GetSystemInfo 请求")

	// 使用 gopsutil 收集静态系统信息
	cpuInfo, err := cpu.Info()
	if err != nil {
		return nil, fmt.Errorf("获取CPU信息失败: %v", err)
	}
	memInfo, err := mem.VirtualMemory()
	if err != nil {
		return nil, fmt.Errorf("获取内存信息失败: %v", err)
	}
	// 我们以根目录为例获取磁盘信息
	exePath, err := os.Executable()
	if err != nil {
		return nil, fmt.Errorf("无法获取可执行文件路径: %v", err)
	}
	projectDir := filepath.Dir(exePath)
	diskInfo, err := disk.Usage(projectDir)
	if err != nil {
		return nil, fmt.Errorf("获取项目所在磁盘信息失败: %v", err)
	}

	// 构造并返回响应
	return &pb.SystemInfo{
		SystemCpuName:       cpuInfo[0].ModelName,
		SystemCpuCoreThread: cpuInfo[0].Cores,
		CpuFrequency:        cpuInfo[0].Mhz,
		SystemMemoryTotalMb: memInfo.Total / 1024 / 1024,
		DiskTotalMb:         diskInfo.Total / 1024 / 1024,
	}, nil
}

// 获取文件夹大小的辅助函数
func getFolderSize(folderPath string) (uint64, error) {
	var size uint64
	err := filepath.Walk(folderPath, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			// 如果遇到权限问题或文件不存在，跳过而不是返回错误
			return nil
		}
		if !info.IsDir() {
			size += uint64(info.Size())
		}
		return nil
	})
	return size, err
}

// 获取 .db 文件信息的辅助函数
func getDatabaseFiles(dataFolderPath string) ([]*pb.DatabaseFile, error) {
	var dbFiles []*pb.DatabaseFile
	
	err := filepath.Walk(dataFolderPath, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return nil // 跳过有问题的文件
		}
		
		if !info.IsDir() && strings.HasSuffix(strings.ToLower(info.Name()), ".db") {
			dbFiles = append(dbFiles, &pb.DatabaseFile{
				Filename: info.Name(),
				SizeMb:   uint64(info.Size()) / 1024 / 1024,
			})
		}
		return nil
	})
	
	return dbFiles, err
}

// 3. 实现 SubscribePerformanceData 流式方法
func (s *monitoringServer) SubscribePerformanceData(req *pb.SubscriptionRequest, stream pb.PerformanceMonitor_SubscribePerformanceDataServer) error {
	interval := time.Duration(req.IntervalSeconds) * time.Second
	targetPID := req.TargetProcessPid
	
	log.Printf("收到 SubscribePerformanceData 请求, 刷新间隔: %v, 目标进程 PID: %d\n", interval, targetPID)

	exePath, err := os.Executable()
	if err != nil {
		log.Printf("严重错误：无法获取可执行文件路径: %v", err)
		return err
	}
	projectDir := filepath.Dir(exePath)

	// 使用 time.NewTicker 创建一个定时器，每隔 interval 时间触发一次
	ticker := time.NewTicker(interval)
	defer ticker.Stop() // 函数结束时停止定时器

	// 获取目标进程（C# 客户端进程）
	proc, err := process.NewProcess(targetPID)
	if err != nil {
		log.Printf("无法获取目标进程信息 (PID: %d): %v", targetPID, err)
		return err
	}
	
	// 为了计算网络速率，需要记录上一次的数据
	var lastNetStats net.IOCountersStat
	var lastNetStatTime time.Time
	initialNetStats, _ := net.IOCounters(false)
	if len(initialNetStats) > 0 {
		lastNetStats = initialNetStats[0]
		lastNetStatTime = time.Now()
	}

	// 使用一个循环来持续发送数据
	for {
		select {
		case <-ticker.C: // 等待定时器触发
			// 收集各种性能数据
			cpuPercent, _ := cpu.Percent(0, false)
			memInfo, _ := mem.VirtualMemory()
			procCpuPercent, _ := proc.CPUPercent()
			procMemInfo, _ := proc.MemoryInfo()

			// 计算网络速率
			var downloadRateKbps, uploadRateKbps float64
			currentNetStats, err := net.IOCounters(false)
			if err == nil && len(currentNetStats) > 0 {
				now := time.Now()
				duration := now.Sub(lastNetStatTime).Seconds()
				if duration > 0 {
					downloadRateKbps = float64(currentNetStats[0].BytesRecv-lastNetStats.BytesRecv) / duration / 1024 * 8
					uploadRateKbps = float64(currentNetStats[0].BytesSent-lastNetStats.BytesSent) / duration / 1024 * 8
				}
				lastNetStats = currentNetStats[0]
				lastNetStatTime = now
			}

			// 获取文件夹大小信息
			logsFolderSize, _ := getFolderSize("./logs")
			dataFolderSize, _ := getFolderSize("./data")
			databaseFiles, _ := getDatabaseFiles("./data")

			// 获取项目所在硬盘的实时使用情况
			diskInfo, err := disk.Usage(projectDir)
			var projectDiskUsedMb uint64
			if err == nil {
				projectDiskUsedMb = diskInfo.Used
			} else {
				log.Printf("警告：无法获取项目磁盘使用情况: %v", err)
			}
			
			// 构造数据
			data := &pb.PerformanceData{
				SystemCpuUsagePer:         cpuPercent[0],
				ProcessCpuUsagePer:        procCpuPercent,
				SystemUsedMemoryMb:        memInfo.Used / 1024 / 1024,
				SystemAvailableMemoryMb:   memInfo.Available / 1024 / 1024,
				SystemCacheMemoryMb:       memInfo.Cached / 1024 / 1024,
				SystemFreeMemoryMb:        memInfo.Free / 1024 / 1024,
				ProcessUsedMemoryMb:       procMemInfo.RSS / 1024 / 1024, // RSS: Resident Set Size
				SystemNetDownloadRateKbps: downloadRateKbps,
				SystemNetUploadRateKbps:   uploadRateKbps,
				LogsFolderSizeMb:         logsFolderSize / 1024 / 1024,
				DataFolderSizeMb:         dataFolderSize / 1024 / 1024,
				DatabaseFiles:            databaseFiles,
				ProjectDiskUsedMb:        projectDiskUsedMb / 1024 / 1024,
			}

			// 通过 stream 发送数据给客户端
			if err := stream.Send(data); err != nil {
				log.Printf("发送数据失败，客户端可能已断开: %v", err)
				return err // 结束这个流
			}

		case <-stream.Context().Done(): // 如果客户端关闭了连接
			log.Println("客户端连接已关闭")
			return nil // 正常退出
		}
	}
}

func main() {
	// 监听一个端口
	port := ":52002"
	lis, err := gonet.Listen("tcp", port)
	if err != nil {
		log.Fatalf("监听失败: %v", err)
	}

	// 创建一个新的 gRPC 服务器
	s := grpc.NewServer()

	// 注册我们的服务实现
	pb.RegisterPerformanceMonitorServer(s, &monitoringServer{})

	log.Printf("gRPC 服务器正在监听 %v", port)
	if err := s.Serve(lis); err != nil {
		log.Fatalf("启动服务器失败: %v", err)
	}
}