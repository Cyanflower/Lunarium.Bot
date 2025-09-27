using Xunit;
using Moq;
using LunariumBot.Net.Logger.Interfaces; // For ILogger
using LunariumBot.Net.Logger.Extensions; // For ForContext()

namespace LunariumBot.Net.Logger.Tests;

public class LoggerWrapperTests
{
    [Fact]
    public void ForContext_WithString_ShouldWrapLoggerAndPassCorrectContext()
    {
        // Arrange
        var mockInnerLogger = new Mock<ILogger>();
        string capturedContext = "";

        // 设置模拟的内部 Logger：当 Log 方法被调用时，捕获第三个参数 (context)
        mockInnerLogger
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(), 
                It.IsAny<string>(), 
                It.IsAny<string>(), // 我们关心的是这个参数
                It.IsAny<Exception>(), 
                It.IsAny<object?[]>()))
            .Callback<LogLevel, string, string, Exception, object?[]>((_, _, ctx, _, _) => capturedContext = ctx);

        // Act
        // 使用 ForContext 扩展方法来创建包装器
        var wrappedLogger = mockInnerLogger.Object.ForContext("MyModule");
        wrappedLogger.Info("Test message");

        // Assert
        // 验证被包装的 Logger 接收到的上下文是不是我们设置的 "MyModule"
        Assert.Equal("MyModule", capturedContext);
    }
    
    [Fact]
    public void ForContext_WithNestedWrapping_ShouldChainContexts()
    {
        // Arrange
        var mockInnerLogger = new Mock<ILogger>();
        string capturedContext = "";

        mockInnerLogger
            .Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<object?[]>()))
            .Callback<LogLevel, string, string, Exception, object?[]>((_, _, ctx, _, _) => capturedContext = ctx);
            
        // Act
        // 进行两次包装
        var moduleLogger = mockInnerLogger.Object.ForContext("Api");
        var userControllerLogger = moduleLogger.ForContext("Users"); // 在包装器上再次调用 ForContext
        
        // 最终的 Logger 是一个 LoggerWrapper 包装着另一个 LoggerWrapper
        userControllerLogger.Info("Get all users");

        // Assert
        // 验证上下文是否被正确地用点连接起来了
        Assert.Equal("Api.Users", capturedContext);
    }

    [Fact]
    public void ForContext_GenericVersion_ShouldUseTypeNameAsContext()
    {
        // Arrange
        var mockInnerLogger = new Mock<ILogger>();
        string capturedContext = "";

        mockInnerLogger
            .Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<object?[]>()))
            .Callback<LogLevel, string, string, Exception, object?[]>((_, _, ctx, _, _) => capturedContext = ctx);

        // Act
        // 使用泛型版本
        var typedLogger = mockInnerLogger.Object.ForContext<LoggerWrapperTests>();
        typedLogger.Warning("A test warning");

        // Assert
        // 验证上下文是否是这个测试类的全名
        Assert.Equal(typeof(LoggerWrapperTests).FullName, capturedContext);
    }
}