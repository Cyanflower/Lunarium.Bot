// Interfaces/IBotLoggerConfiguration.cs
using Serilog;

public interface IBotLoggerConfiguration
{
    /// <summary>
    /// 将此Bot的专属日志配置应用到全局LoggerConfiguration上
    /// </summary>
    /// <param name="loggerConfig">全局的LoggerConfiguration实例</param>
    /// <returns>配置完成后的LoggerConfiguration</returns>
    LoggerConfiguration Configure(LoggerConfiguration loggerConfig);
}