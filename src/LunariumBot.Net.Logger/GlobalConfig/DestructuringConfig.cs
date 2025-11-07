namespace LunariumBot.Net.Logger.GlobalConfig;

/// <summary>
/// 配置是否默认对复杂对象进行结构
/// </summary>
internal static class DestructuringConfig
{
    /// <summary>
    /// 标识是否默认对复杂对象进行结构
    /// </summary>
    internal static bool AutoDestructureCollections { get; private set; } = false;

    /// <summary>
    /// 设置默认对复杂对象进行结构
    /// </summary>
    internal static void EnableAutoDestructuring()
    {
        AutoDestructureCollections = true;
    }
}
