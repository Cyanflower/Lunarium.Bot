
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

using Dapper;
using Discord;

using SearchBot.Model;

namespace SearchBot.Database;

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
            _logger.LogError(ex, "执行ulong列表查询时出错. SQL: {sqlQuery}", sql);
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
            _logger.LogWarning("当前数据列表为空，将清空数据库中的所有帖子。");

            // 准备: 创建数据库连接
            using var _connection = new SqliteConnection(_connectionString);

            // 执行: 清空所有帖子 (ON DELETE CASCADE将自动清理关联数据)
            int allPostsCount = await _connection.ExecuteAsync("DELETE FROM posts_main;");
            _logger.LogInformation("已清空数据库中的所有 {Count} 个帖子。", allPostsCount);
            return null;
        }

        // 准备: 提取当前数据列表中的所有帖子ID
        var currentThreadIds = currentDataList.Select(data => data.ThreadId).ToHashSet();
        _logger.LogDebug("当前数据列表包含 {Count} 个帖子。", currentThreadIds.Count);

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
            _logger.LogDebug("数据库中现有 {Count} 个帖子。", dbThreadIdsList.Count);

            // 计算: 找出需要删除的过时帖子ID (存在于数据库但不在当前列表中)
            var threadsToDelete = dbThreadIdsList.Where(id => !currentThreadIds.Contains(id)).ToList();

            if (threadsToDelete.Any())
            {
                _logger.LogInformation("发现 {Count} 个过时帖子需要移除。", threadsToDelete.Count);
                return threadsToDelete;
            }
            else
            {
                // 成功: 提交事务（虽然没有删除操作）
                await transaction.CommitAsync();
                _logger.LogInformation("无过时帖子需要移除，数据库已是最新状态。");
                return null;
            }
        }
        catch (Exception ex)
        {
            // 失败: 回滚事务
            await transaction.RollbackAsync();
            _logger.LogError(ex, "移除过时帖子时发生错误，事务已回滚。");
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
            _logger.LogError(ex, "执行所有唯一标签名查询时出错. SQL: {sqlQuery}", getAllUniqueTagNamesSql);
            // 出错时返回一个空列表，保证程序不会崩溃
            return new List<string>();
        }
    }
}
