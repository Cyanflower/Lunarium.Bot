using Xunit;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using LunariumBot.Net.Logger.Interfaces;
using LunariumBot.Net.Logger.Extensions;

namespace LunariumBot.Net.Logger.Tests;

public class LunariumLoggerTests
{
    [Fact]
    public async Task Log_WhenMessageIsSent_ShouldCallEmitOnSink()
    {
        // Arrange
        var mockSink = new Mock<ILogSink>();
        var config = new SinkConfiguration { LogMinLevel = LogLevel.Debug };
        var sinks = new List<(ILogSink, SinkConfiguration)> { (mockSink.Object, config) };
        var loggerInstance = new Logger(sinks, "TestLogger");

        // Act & Assert
        // 把 Act 和 Assert 都包裹在 using 块里
        await using (loggerInstance)
        {
            var logger = loggerInstance as ILogger;
            logger.Info("Hello, Mock!");

        } // <-- DisposeAsync() 在这里被自动调用，它会等待后台任务处理完所有消息

        // 在 using 块外面进行验证，此时可以确保所有异步操作都已完成
        mockSink.Verify(
            sink => sink.Emit(It.IsAny<LogEntry>(), config.ToJson, config.Indented),
            Times.Once);
    }

    [Fact]
    public async Task Log_WithProperties_ShouldCapturePropertiesCorrectly()
    {
        // Arrange
        var mockSink = new Mock<ILogSink>();
        LogEntry? capturedEntry = null;
        mockSink.Setup(s => s.Emit(It.IsAny<LogEntry>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<LogEntry, bool, bool>((entry, _, __) => capturedEntry = entry);
        var config = new SinkConfiguration();
        var sinks = new List<(ILogSink, SinkConfiguration)> { (mockSink.Object, config) };
        var loggerInstance = new Logger(sinks, "TestLogger");

        // Act
        await using (loggerInstance)
        {
            var logger = loggerInstance as ILogger;
            logger.Info("User {Name} with ID {UserId} logged in", "Bob", 123);
        }

        // Assert
        Assert.NotNull(capturedEntry);
        Assert.Equal(2, capturedEntry.Value.Properties.Count);
        Assert.Equal("Bob", capturedEntry.Value.Properties["Name"]);
        Assert.Equal(123, capturedEntry.Value.Properties["UserId"]);
    }

    [Fact]
    public async Task Log_WhenLevelIsBelowMinimum_ShouldNotEmit()
    {
        // Arrange
        var mockSink = new Mock<ILogSink>();
        var config = new SinkConfiguration { LogMinLevel = LogLevel.Info };
        var sinks = new List<(ILogSink, SinkConfiguration)> { (mockSink.Object, config) };
        var loggerInstance = new Logger(sinks, "TestLogger");

        // Act
        await using (loggerInstance)
        {
            var logger = loggerInstance as ILogger;
            logger.Debug("This should not be logged.");
        }

        // Assert
        mockSink.Verify(
            sink => sink.Emit(It.IsAny<LogEntry>(), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Log_WithContextFilter_ShouldOnlyEmitMatchingContext()
    {
        // Arrange
        var mockSink = new Mock<ILogSink>();
        var config = new SinkConfiguration 
        { 
            LogMinLevel = LogLevel.Debug,
            ContextFilterIncludes = new List<string> { "Api." }
        };
        var sinks = new List<(ILogSink, SinkConfiguration)> { (mockSink.Object, config) };
        var loggerInstance = new Logger(sinks, "TestLogger");

        // Act
        await using (loggerInstance)
        {
            var logger = loggerInstance as ILogger;
            logger.ForContext("Api.Users").Info("User endpoint called");
            logger.ForContext("Database.Query").Debug("DB query executed");
            logger.ForContext("Api.Orders").Warning("Order endpoint called");
        }
        
        // Assert
        mockSink.Verify(
            sink => sink.Emit(It.IsAny<LogEntry>(), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Exactly(2));
        mockSink.Verify(
            sink => sink.Emit(It.Is<LogEntry>(e => e.Context == "Api.Users"), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Once);
        mockSink.Verify(
            sink => sink.Emit(It.Is<LogEntry>(e => e.Context == "Api.Orders"), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Once);
        mockSink.Verify(
            sink => sink.Emit(It.Is<LogEntry>(e => e.Context == "Database.Query"), It.IsAny<bool>(), It.IsAny<bool>()),
            Times.Never);
    }
}