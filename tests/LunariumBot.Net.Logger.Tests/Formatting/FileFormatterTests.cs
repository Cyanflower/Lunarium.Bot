using Xunit;
using System;
using System.Collections.Generic;
using LunariumBot.Net.Logger.Formatting;

namespace LunariumBot.Net.Logger.Tests.Formatting;

// 同样，需要 InternalsVisibleTo 来访问 internal 的 FileFormatter

public class FileFormatterTests
{
    [Fact]
    public void Format_AsPlainText_RendersTemplateCorrectly()
    {
        // Arrange
        var entry = new LogEntry(
            LoggerName: "FileLogger",
            Timestamp: new DateTime(2023, 10, 27, 15, 30, 0),
            Level: LogLevel.Warning,
            MessageTemplate: "Disk space is running low on {Drive}",
            Properties: new Dictionary<string, object?> { { "Drive", "C:" } },
            Context: "System.Health"
        );

        // Act
        var formattedString = FileFormatter.Format(entry, toJson: false, indented: false);
        
        // Assert
        var expected = "[2023-10-27 15:30:00.000] [WRN] [FileLogger] [System.Health] Disk space is running low on C:";
        Assert.Equal(expected, formattedString);
    }
    
    [Fact]
    public void Format_AsPlainTextWithException_AppendsExceptionDetails()
    {
        // Arrange
        var exception = new System.IO.IOException("Disk full");
        var entry = new LogEntry(
            LoggerName: "FileLogger",
            Timestamp: DateTime.Now,
            Level: LogLevel.Error,
            MessageTemplate: "Failed to write file",
            Properties: new Dictionary<string, object?>(),
            Context: "IO",
            Exception: exception
        );

        // Act
        var formattedString = FileFormatter.Format(entry, toJson: false, indented: false);
        
        // Assert
        Assert.StartsWith("[", formattedString); // 确保开头的格式是正确的
        Assert.EndsWith(exception.ToString(), formattedString); // 确保结尾是完整的异常信息
        Assert.Contains(Environment.NewLine, formattedString); // 异常信息前应该有个换行
    }

    [Fact]
    public void Format_AsJson_ReturnsStandardJsonString()
    {
        // Arrange
        var entry = new LogEntry(
            LoggerName: "FileLogger",
            Timestamp: new DateTime(2023, 1, 1),
            Level: LogLevel.Info,
            MessageTemplate: "JSON output",
            Properties: new Dictionary<string, object?> { { "Data", 123 } },
            Context: "Json"
        );
        
        // Act
        // 注意，这里调用的是 FileFormatter.Format, 但内部会委托给 ToJson 扩展方法
        var jsonString = FileFormatter.Format(entry, toJson: true, indented: false);
        
        // Assert
        // 我们不重复测试 ToJson 的所有细节，因为在 LogEntryExtensionsTests 里已经测过了。
        // 这里我们只确保它返回的是一个有效的、不带颜色的 JSON 字符串。
        Assert.StartsWith("{", jsonString);
        Assert.EndsWith("}", jsonString);
        Assert.DoesNotContain("\x1b[", jsonString); // 确保不包含任何 ANSI 颜色代码
        Assert.Contains("\"Data\":123", jsonString);
    }
}