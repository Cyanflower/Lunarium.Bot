using Xunit;
using System;
using System.Text.Json;
using System.Collections.Generic;
using LunariumBot.Net.Logger.Extensions; // 引入你的扩展方法所在的命名空间

namespace LunariumBot.Net.Logger.Tests.Extensions;

public class LogEntryExtensionsTests
{
    [Fact]
    public void ToJson_WithSimpleLogEntry_ShouldSerializeCorrectly()
    {
        // Arrange
        var logEntry = new LogEntry(
            LoggerName: "TestLogger",
            Timestamp: new DateTime(2023, 10, 27, 10, 0, 0, DateTimeKind.Utc),
            Level: LogLevel.Info,
            MessageTemplate: "User {Username} logged in",
            Properties: new Dictionary<string, object?> { { "Username", "Alice" } },
            Context: "Authentication"
        );

        // Act
        var jsonString = logEntry.ToJson();

        // Assert
        // 为了避免因为空格或换行导致测试失败，我们可以反序列化回来再比较，或者只检查关键部分
        using var jsonDoc = JsonDocument.Parse(jsonString);
        var root = jsonDoc.RootElement;

        Assert.Equal("TestLogger", root.GetProperty("LoggerName").GetString());
        Assert.Equal(LogLevel.Info.ToString(), root.GetProperty("Level").GetString()); // 验证枚举是否转为字符串
        Assert.Equal("Authentication", root.GetProperty("Context").GetString());
        Assert.Equal("Alice", root.GetProperty("Properties").GetProperty("Username").GetString());
    }

    [Fact]
    public void ToJson_WithIndentedOption_ShouldProduceFormattedJson()
    {
        // Arrange
        var logEntry = new LogEntry(
            LoggerName: "Test",
            Timestamp: DateTime.Now,
            Level: LogLevel.Debug,
            MessageTemplate: "Test",
            Properties: new Dictionary<string, object?>(),
            Context: "Test"
        );

        // Act
        var jsonString = logEntry.ToJson(writeIndented: true);

        // Assert
        // 格式化的 JSON 应该包含换行符
        Assert.Contains("\n", jsonString);
        // ... 并且应该以 "{" 开头和 "}" 结尾，中间有换行
        Assert.StartsWith("{\n", jsonString);
    }

    [Fact]
    public void ToJson_WithException_ShouldSerializeExceptionDetails()
    {
        // Arrange
        Exception exception;
        try
        {
            // 创建一个真实的异常，这样它才会有堆栈跟踪信息
            throw new InvalidOperationException("Something went wrong!");
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        var logEntry = new LogEntry(
            LoggerName: "App",
            Timestamp: DateTime.Now,
            Level: LogLevel.Error,
            MessageTemplate: "An error occurred",
            Properties: new Dictionary<string, object?>(),
            Context: "Main",
            Exception: exception
        );

        // Act
        var jsonString = logEntry.ToJson();
        
        // Assert
        using var jsonDoc = JsonDocument.Parse(jsonString);
        var exElement = jsonDoc.RootElement.GetProperty("Exception");

        Assert.Equal("System.InvalidOperationException", exElement.GetProperty("Type").GetString());
        Assert.Equal("Something went wrong!", exElement.GetProperty("Message").GetString());
        Assert.NotNull(exElement.GetProperty("StackTrace").GetString()); // 堆栈跟踪不应为空
    }

    [Fact]
    public void ToJson_WithNestedInnerException_ShouldSerializeRecursively()
    {
        // Arrange
        var innerException = new ArgumentNullException("paramName");
        var outerException = new Exception("Outer exception", innerException);

        var logEntry = new LogEntry(
            LoggerName: "App",
            Timestamp: DateTime.Now,
            Level: LogLevel.Critical,
            MessageTemplate: "A critical failure",
            Properties: new Dictionary<string, object?>(),
            Context: "Core",
            Exception: outerException
        );

        // Act
        var jsonString = logEntry.ToJson();
        
        // Assert
        using var jsonDoc = JsonDocument.Parse(jsonString);
        var exElement = jsonDoc.RootElement.GetProperty("Exception");
        var innerExElement = exElement.GetProperty("InnerException");

        Assert.Equal("System.Exception", exElement.GetProperty("Type").GetString());
        Assert.Equal("System.ArgumentNullException", innerExElement.GetProperty("Type").GetString());
        Assert.Equal("Value cannot be null. (Parameter 'paramName')", innerExElement.GetProperty("Message").GetString());
    }
}