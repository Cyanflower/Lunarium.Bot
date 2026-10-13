namespace Lunarium.Bot.Logger.GlobalConfig;

/// <summary>
/// 全局配置锁，用于确保全局配置只能被设置一次
/// </summary>
internal static class GlobalConfigLock
{
    /// <summary>
    /// 标识全局配置是否已完成
    /// </summary>
    internal static bool Configured = false;

    /// <summary>
    /// 完成全局配置并锁定
    /// </summary>
    internal static void CompleteConfig()
    {
        Configured = true;
    }
}

/// <summary>
/// 全局构建锁，用于日志记录器实例全局只能被构建一次
/// </summary>
internal static class GlobalBuildLock
{
    /// <summary>
    /// 标识日志记录器实例是否已完成构建
    /// </summary>
    internal static bool Completed = false;

    /// <summary>
    /// 完成构建并锁定
    /// </summary>
    internal static void CompleteBuild()
    {
        Completed = true;
    }
}