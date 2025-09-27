using Xunit;
using System;
using System.Collections.Generic;
using LunariumBot.Net.Logger.Formatting; // 引入 ConsoleFormatter 所在的命名空间
using LunariumBot.Net.Logger.Extensions; // 我们会用到 ToJson 和 Color 扩展方法

namespace LunariumBot.Net.Logger.Tests.Formatting;

// 由于 ConsoleFormatter 是 internal static，我们需要在测试项目中做一点小小的设置
// 才能访问它。在你的 LunariumBot.Net.Logger.csproj 文件中，添加下面这行：
// <ItemGroup>
//   <InternalsVisibleTo Include="LunariumBot.Net.Logger.Tests" />
// </ItemGroup>
// 这样，测试项目就能看到 internal 成员啦！

public class ConsoleFormatterTests
{
    // === 测试 FormatPlainText ===

    [Fact]
    public void FormatPlainText_WithSimpleMessage_FormatsCorrectly()
    {
        // Arrange
        var entry = new LogEntry(
            LoggerName: "Test",
            Timestamp: new DateTime(2023, 1, 1, 12, 0, 0),
            Level: LogLevel.Info,
            MessageTemplate: "Hello, world!",
            Properties: new Dictionary<string, object?>(),
            Context: "Main"
        );

        // Act
        var formattedString = ConsoleFormatter.FormatPlainText(entry);

        // Assert - ✨ 我们在这里做修改 ✨
        // 1. 我们把期望的纯文本时间戳，也用你的 AnsiColorExtensions.Color() 方法上色！
        //    根据你的 ConsoleFormatter 代码，时间戳的颜色是 ConsoleColor.Green。
        var expectedTimestampString = "2023-01-01 12:00:00".Color(ConsoleColor.Green);
        
        // 2. 你的代码还会给时间戳两边的方括号上色 (DarkGray)
        var prefix = "[".Color(ConsoleColor.DarkGray);
        var suffix = "]".Color(ConsoleColor.DarkGray);

        // 3. 现在我们断言的是一个完整的、带颜色的时间戳片段！
        Assert.Contains($"{prefix}{expectedTimestampString}{suffix}", formattedString);

        // 其他的断言保持不变，因为它们也应该是带颜色的，
        // 但只检查 INF 和 [Main] 这些片段是足够的，除非它们也有特定颜色。
        // 为了更精确，我们可以也给它们上色。
        // Info 级别是 Green, Context 是 Cyan.
        Assert.Contains("INF".Color(ConsoleColor.Green), formattedString);
        Assert.Contains("Main".Color(ConsoleColor.Cyan), formattedString);
        Assert.Contains("Hello, world!", formattedString); // 消息本身没有颜色
    }

    [Fact]
    public void FormatPlainText_WithMessageTemplate_RendersPropertiesWithColor()
    {
        // Arrange
        var entry = new LogEntry(
            LoggerName: "Test",
            Timestamp: DateTime.Now,
            Level: LogLevel.Info,
            MessageTemplate: "User {Username} logged in from {IPAddress}",
            Properties: new Dictionary<string, object?>
            {
                { "Username", "Alice" }, // string -> Magenta
                { "IPAddress", "127.0.0.1" }
            },
            Context: "Auth"
        );

        var formattedString = ConsoleFormatter.FormatPlainText(entry);

        // Assert
        // "Alice" 应该是品红色的 (Magenta, 95)
        Assert.Contains("Alice".Color(ConsoleColor.Magenta), formattedString);
        // "127.0.0.1" 也是字符串，同样是品红色
        Assert.Contains("127.0.0.1".Color(ConsoleColor.Magenta), formattedString);
        
        // 确保原始模板占位符不见了
        Assert.DoesNotContain("{Username}", formattedString);
        Assert.DoesNotContain("{IPAddress}", formattedString);
    }
    
    [Fact]
    public void FormatPlainText_WithDifferentTypedProperties_UsesCorrectColors()
    {
        // Arrange
        var entry = new LogEntry(
            LoggerName: "Test",
            Timestamp: DateTime.Now,
            Level: LogLevel.Debug,
            MessageTemplate: "Data: {Number}, {IsValid}, {Nullable}",
            Properties: new Dictionary<string, object?>
            {
                { "Number", 123 },         // number -> Yellow
                { "IsValid", true },       // bool   -> Blue
                { "Nullable", null }       // null   -> DarkBlue
            },
            Context: "Data"
        );

        var formattedString = ConsoleFormatter.FormatPlainText(entry);
        
        // Assert
        Assert.Contains("123".Color(ConsoleColor.Yellow), formattedString);
        Assert.Contains("True".Color(ConsoleColor.Blue), formattedString); // bool.ToString() is "True"
        Assert.Contains("null".Color(ConsoleColor.DarkBlue), formattedString);
    }
    
    [Fact]
    public void FormatPlainText_WithException_IncludesExceptionDetails()
    {
        // Arrange
        var exception = new Exception("Test Exception");
        var entry = new LogEntry(
            LoggerName: "Test",
            Timestamp: DateTime.Now,
            Level: LogLevel.Error,
            MessageTemplate: "An error occurred",
            Properties: new Dictionary<string, object?>(),
            Context: "ErrorHandling",
            Exception: exception
        );

        var formattedString = ConsoleFormatter.FormatPlainText(entry);
        
        // Assert
        Assert.Contains("An error occurred", formattedString);
        // 异常信息应该是红色的
        Assert.Contains("System.Exception: Test Exception".Color(ConsoleColor.Red), formattedString);
    }

    // === 测试 FormatJson ===

    [Fact]
    public void FormatJson_WithSimpleEntry_ColorizesKeysAndValues()
    {
        // Arrange
        var entry = new LogEntry(
            LoggerName: "JsonLogger",
            Timestamp: DateTime.Now,
            Level: LogLevel.Warning,
            MessageTemplate: "JSON test",
            Properties: new Dictionary<string, object?>
            {
                { "Count", 42 },
                { "IsEnabled", false },
                { "Name", "Test" }
            },
            Context: "JsonTest"
        );

        // Act
        var formattedJson = ConsoleFormatter.FormatJson(entry, indented: false);
        
        // Assert
        // 检查 JSON Key (例如 "LoggerName") 是否被染成了深青色 (DarkCyan)
        Assert.Contains("\"" + "LoggerName".Color(ConsoleColor.DarkCyan) + "\"", formattedJson);

        // 检查不同类型的值的颜色
        Assert.Contains("\"" + "JsonLogger".Color(ConsoleColor.Magenta) + "\"", formattedJson); // String
        Assert.Contains(": " + "42".Color(ConsoleColor.Yellow), formattedJson); // Number
        Assert.Contains(": " + "false".Color(ConsoleColor.Blue), formattedJson); // Boolean
    }
}