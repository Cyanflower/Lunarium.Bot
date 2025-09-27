// 引入你的代码命名空间和测试框架
using Xunit;
using System;
using LunariumBot.Net.Logger.Formatting;

namespace LunariumBot.Net.Logger.Tests.Formatting;

public class AnsiColorExtensionsTests
{
    // [Theory] 是 xUnit 的一个超棒特性，可以让你用一组数据跑同一个测试！
    // 我们用它来测试所有单前景色的情况，超高效！
    [Theory]
    [InlineData("Hello", ConsoleColor.Red, "\x1b[91mHello\x1b[0m")]
    [InlineData("Test", ConsoleColor.Green, "\x1b[92mTest\x1b[0m")]
    [InlineData("Debug", ConsoleColor.DarkGray, "\x1b[90mDebug\x1b[0m")]
    [InlineData("Message with spaces", ConsoleColor.Cyan, "\x1b[96mMessage with spaces\x1b[0m")]
    [InlineData("", ConsoleColor.White, "\x1b[97m\x1b[0m")] // 测试空字符串
    public void Color_WithSingleForegroundColor_ShouldReturnCorrectlyFormattedAnsiString(
        string inputText, ConsoleColor color, string expectedOutput)
    {
        // Act: 调用我们要测试的扩展方法
        var actualOutput = inputText.Color(color);

        // Assert: 验证结果是不是和我们预期的一样
        Assert.Equal(expectedOutput, actualOutput);
    }

    [Fact]
    public void Color_WithForegroundAndBackground_ShouldReturnCombinedAnsiString()
    {
        // Arrange
        var inputText = "CRITICAL";
        var foreground = ConsoleColor.White;
        var background = ConsoleColor.DarkRed;
        // 前景色 White: 97, 背景色 DarkRed: 41
        var expectedOutput = "\x1b[97;41mCRITICAL\x1b[0m";

        // Act
        var actualOutput = inputText.Color(foreground, background);

        // Assert
        Assert.Equal(expectedOutput, actualOutput);
    }

    [Fact]
    public void Color_WithInvalidColor_ShouldDefaultToGray()
    {
        // Arrange: -1 是一个无效的 ConsoleColor 枚举值
        var inputText = "Invalid";
        var invalidColor = (ConsoleColor)(-1);
        // 你的代码默认回退到灰色 (Gray)，ANSI code 是 37
        var expectedOutput = "\x1b[37mInvalid\x1b[0m";

        // Act
        var actualOutput = inputText.Color(invalidColor);

        // Assert
        Assert.Equal(expectedOutput, actualOutput);
    }
}