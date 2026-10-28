namespace Lunarium.Bot.DataCenter.DataFetcher;

/// <summary>
/// 扫描模式。
/// </summary>
public enum ScanMode
{
    /// <summary>
    /// 增量：第 1 阶段全量列举，第 2/3 阶段只处理脏集合与新增帖子。
    /// </summary>
    Incremental,

    /// <summary>
    /// 全量：对所有帖子执行深度扫描（月度对账，兜底漏事件与反应/编辑漂移）。
    /// </summary>
    Full,
}
