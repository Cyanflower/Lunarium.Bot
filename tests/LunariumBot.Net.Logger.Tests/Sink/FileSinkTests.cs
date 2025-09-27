using Xunit;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LunariumBot.Net.Logger.Sink;
using LunariumBot.Net.Logger.Interfaces;

namespace LunariumBot.Net.Logger.Tests.Sink;

// IDisposable 接口让 xUnit 可以在每个测试后帮我们自动调用 Dispose 方法进行清理
public class FileSinkTests : IDisposable
{
    private readonly string _testDirectory;

    // 这是构造函数，每个测试方法运行前都会被调用一次
    public FileSinkTests()
    {
        // 创建一个唯一的临时目录，用于本次测试
        _testDirectory = Path.Combine(Path.GetTempPath(), $"logger-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    // 这是 Dispose 方法，每个测试方法运行后都会被调用一次
    public void Dispose()
    {
        // 清理战场！删除临时目录和其中的所有文件
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private LogEntry CreateLogEntry(string message, LogLevel level = LogLevel.Info, DateTime? timestamp = null)
    {
        return new LogEntry(
            "Test",
            timestamp ?? DateTime.Now,
            level,
            message,
            new Dictionary<string, object?>(),
            "TestContext"
        );
    }

    // === 测试 TimedRotatingFileSink (按天轮转) ===

    [Fact]
    public void TimedRotatingFileSink_Emit_CreatesFileWithCorrectDate()
    {
        // Arrange
        var logPath = Path.Combine(_testDirectory, "timed.log");
        using var sink = new TimedRotatingFileSink(logPath);
        var entry = CreateLogEntry("Hello, timed log!");
        var expectedFileName = $"timed-{DateTime.Now:yyyy-MM-dd}.log";

        // Act
        sink.Emit(entry, toJson: false, indented: false);
        sink.Dispose(); // 手动 Dispose 确保文件内容被写入

        // Assert
        var files = Directory.GetFiles(_testDirectory);
        Assert.Single(files); // 应该只创建了一个文件
        Assert.Equal(expectedFileName, Path.GetFileName(files[0]));
        Assert.Contains("Hello, timed log!", File.ReadAllText(files[0]));
    }
    
    [Fact]
    public void TimedRotatingFileSink_WhenDateChanges_CreatesNewFile()
    {
        // Arrange
        var logPath = Path.Combine(_testDirectory, "daily.log");
        using var sink = new TimedRotatingFileSink(logPath);
        var today = DateTime.Now;
        var yesterday = today.AddDays(-1);

        // Act
        sink.Emit(CreateLogEntry("Yesterday's log", timestamp: yesterday), false, false);
        sink.Emit(CreateLogEntry("Today's log", timestamp: today), false, false);
        sink.Dispose();

        // Assert
        var files = Directory.GetFiles(_testDirectory).OrderBy(f => f).ToList();
        Assert.Equal(2, files.Count); // 应该创建了两个文件
        Assert.EndsWith($"daily-{yesterday:yyyy-MM-dd}.log", files[0]);
        Assert.EndsWith($"daily-{today:yyyy-MM-dd}.log", files[1]);
    }

}