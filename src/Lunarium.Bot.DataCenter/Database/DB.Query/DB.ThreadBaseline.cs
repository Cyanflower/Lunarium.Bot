using Dapper;

using Lunarium.Bot.Framework;
using Lunarium.Bot.DataCenter.Models;

namespace Lunarium.Bot.DataCenter.Database;

public partial class DatabaseService
{
    /// <summary>
    /// 读取增量扫描所需的帖子基线（只取元数据，不含正文/附件）。
    /// </summary>
    /// <returns>键为帖子 ID，值为基线数据。</returns>
    public async Task<Dictionary<ulong, ThreadScanBaseline>> GetThreadScanBaselinesAsync()
    {
        const string sql = """
            SELECT
                thread_id AS ThreadId,
                title AS Title,
                tags AS Tags,
                is_locked AS IsLocked,
                is_valid AS IsValid,
                is_deleted AS IsDeleted
            FROM posts_main;
            """;

        await using var connection = GetConnection();
        await connection.OpenAsync();

        var rows = await connection.QueryAsync<ThreadScanBaselineRow>(sql);

        return rows.ToDictionary(
            row => row.ThreadId.ToDiscordId(),
            row => new ThreadScanBaseline(row.Title, row.Tags, row.IsLocked, row.IsValid, row.IsDeleted));
    }

    /// <summary>Dapper 映射行（long 为数据库 ID）。</summary>
    private sealed class ThreadScanBaselineRow
    {
        public long ThreadId { get; set; }
        public string Title { get; set; } = "";
        public string? Tags { get; set; }
        public bool IsLocked { get; set; }
        public bool IsValid { get; set; }
        public bool IsDeleted { get; set; }
    }
}
