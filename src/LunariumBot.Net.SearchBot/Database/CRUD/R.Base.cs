// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;

// 解决方案内引用项目
using LunariumBot.Net.SearchBot.Model;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义 ===
    // SQL模板: 查询所有频道ID
    const string getAllChannelIdsSql = "SELECT channel_id AS ChannelId FROM channels;";
    // SQL模板: 查询所有标签ID
    const string getAllTagIdsSql = "SELECT tag_id AS TagId FROM tags;";
    // SQL模板: 根据帖子ID在标签关系表中查询标签信息
    const string getTagsByThreadIdSql = "SELECT tag_name AS TagName FROM post_tags_junction WHERE thread_id = @threadId;";
    // SQL模板: 查询全部标签名字 (去重)
    const string getAllUniqueTagNamesSql = "SELECT DISTINCT tag_name AS TagName FROM tags;";
    // 新增: 查询所有标签的详细信息
    const string getAllTagsWithDetailsSql = "SELECT tag_name AS TagName, channel_name AS ChannelName, usage_count AS UsageCount, channel_id AS ChannelId FROM tags;";
    // === 私有辅助方法 ===

    /// <summary>
    /// 执行一个查询并返回 ulong 列表的通用辅助方法
    /// </summary>
    /// <param name="sql">要执行的SQL语句</param>
    /// <param name="param">查询参数 (可选)</param>
    /// <returns>ulong 结果列表</returns>
    private async Task<List<ulong>> QueryUlongListAsync(string sql, object? param = null)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var ids = await connection.QueryAsync<ulong>(sql, param);
            return ids.ToList();
        }
        catch (Exception ex)
        {
            // 这里可以更具体地记录是哪个SQL出了问题
            _logger.Error($"执行ulong列表查询时出错. SQL: {sql}", ex);
            return new List<ulong>();
        }
    }

    // === 公开查询方法 ===

    /// <summary>
    /// 获取数据库中所有现有的频道ID列表
    /// </summary>
    /// <returns>频道ID列表</returns>
    public Task<List<ulong>> GetAllChannelIdsAsync()
    {
        return QueryUlongListAsync(getAllChannelIdsSql);
    }

    /// <summary>
    /// 获取数据库中所有现有的标签ID列表
    /// </summary>
    /// <returns>标签ID列表</returns>
    public Task<List<ulong>> GetAllTagIdsAsync()
    {
        return QueryUlongListAsync(getAllTagIdsSql);
    }

    /// <summary>
    /// 查询数据库中的所有帖子，移除不存在于当前数据列表中的帖子。
    /// 该方法用于清理过时的帖子数据，确保数据库与最新扫描结果保持同步。
    /// </summary>
    /// <param name="currentDataList">当前最新的帖子数据列表（来自InsertPostAsync的返回值）。</param>
    /// <returns>待移除的帖子列表。</returns>
    public async Task<List<ulong>?> QueryObsoletePostsAsync(List<ThreadDataModel> currentDataList)
    {
        // 校验: 若当前数据列表为空，则清空所有帖子
        if (currentDataList == null || !currentDataList.Any())
        {
            _logger.Warning("当前数据列表为空，将清空数据库中的所有帖子。");

            // 准备: 创建数据库连接
            using var _connection = new SqliteConnection(_connectionString);

            // 执行: 清空所有帖子 (ON DELETE CASCADE将自动清理关联数据)
            int allPostsCount = await _connection.ExecuteAsync("DELETE FROM posts_main;");
            _logger.Info($"已清空数据库中的所有 {allPostsCount} 个帖子。");
            return null;
        }

        // 准备: 提取当前数据列表中的所有帖子ID
        var currentThreadIds = currentDataList.Select(data => data.ThreadId).ToHashSet();
        _logger.Debug($"当前数据列表包含 {currentThreadIds.Count} 个帖子。");

        // 准备: 创建数据库连接并开启事务
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 查询: 从数据库获取所有现有的帖子ID
            var dbThreadIds = await connection.QueryAsync<ulong>(
                "SELECT thread_id AS ThreadId FROM posts_main;",
                transaction: transaction
            );
            var dbThreadIdsList = dbThreadIds.ToList();
            _logger.Debug($"数据库中现有 {dbThreadIdsList.Count} 个帖子。");

            // 计算: 找出需要删除的过时帖子ID (存在于数据库但不在当前列表中)
            var threadsToDelete = dbThreadIdsList.Where(id => !currentThreadIds.Contains(id)).ToList();

            if (threadsToDelete.Any())
            {
                _logger.Info($"发现 {threadsToDelete.Count} 个过时帖子需要移除。");
                return threadsToDelete;
            }
            else
            {
                // 成功: 提交事务（虽然没有删除操作）
                await transaction.CommitAsync();
                _logger.Info("无过时帖子需要移除，数据库已是最新状态。");
                return null;
            }
        }
        catch (Exception ex)
        {
            // 失败: 回滚事务
            await transaction.RollbackAsync();
            _logger.Error("移除过时帖子时发生错误，事务已回滚。", ex);
            return null;
        }
    }

    /// <summary>
    /// 获取数据库中所有唯一的标签名称列表。
    /// </summary>
    /// <returns>一个包含不重复标签名的列表。</returns>
    public async Task<List<string>> GetAllUniqueTagNamesAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            // 使用 DISTINCT 关键字，数据库会直接帮我们完成去重，效率超高！
            var names = await connection.QueryAsync<string>(getAllUniqueTagNamesSql);
            return names.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行所有唯一标签名查询时出错. SQL: {getAllUniqueTagNamesSql}", ex);
            // 出错时返回一个空列表，保证程序不会崩溃
            return new List<string>();
        }
    }
    
    /// <summary>
    /// 获取数据库中所有标签的详细信息列表。
    /// </summary>
    /// <returns>一个包含标签详细信息的列表 (TagInfo)。</returns>
    public async Task<List<TagInfo>> GetAllTagsWithDetailsAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            var tags = await connection.QueryAsync<TagInfo>(getAllTagsWithDetailsSql);
            return tags.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行所有标签详情查询时出错. SQL: {getAllTagsWithDetailsSql}", ex);
            return new List<TagInfo>();
        }
    }
}
