using System.Text.Json;

// NuGet 包依赖
using Npgsql;
using Dapper;

// 项目内命名空间
using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.Extensions;
using Lunarium.Bot.DataCenter.Models.Database;

namespace Lunarium.Bot.DataCenter.Database;

/// <summary>
/// 数据分析相关的查询方法
/// </summary>
public partial class DatabaseService
{
    public async Task<List<UrlSignedInfo>> GetAllUrlInfoAsync()
    {
        const string SqlCommand = """
        SELECT 
            thread_id AS ChannelId,
            first_message_id AS MessageId,
            attachment_urls::jsonb AS Url
        FROM posts_main
        WHERE
            is_deleted = FALSE
            AND attachment_urls IS NOT NULL
            AND attachment_urls != '[]'
            AND attachment_urls != 'null';
        """;

        try
        {
            await using var connection = GetConnection();

            var result = await connection.QueryAsync<UrlSignedInfoDb>(
                new CommandDefinition(SqlCommand, commandTimeout: 300)
            );

            List<UrlSignedInfo> urlSignedInfos = new(result.AsList().Count);

            foreach (var urlSignedInfoDb in result)
            {
                try
                {
                    if (urlSignedInfoDb.TryToDomain(out var domainModel))
                    {
                        // TryToDomain 保证了 Try 为 true 时 out 不为 null
                        urlSignedInfos.Add(domainModel!);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 转换失败: ChannelID: {ChannelId}, MessageId: {MessageId}, Url: {@Url}",
                        urlSignedInfoDb.ChannelId, urlSignedInfoDb.MessageId, urlSignedInfoDb.Url);
                }
            }
            return urlSignedInfos;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ GetAllUrlInfoAsync: 查询所有帖子的附件url信息失败");
            return new List<UrlSignedInfo>();
        }
    }
    
    public async Task UpdateUrlInfoAsync(NpgsqlTransaction transaction, List<(ulong threadId, Jsonb<List<string>> urls)> urlSignedInfos)
    {
        const string SqlCommand = """
            UPDATE posts_main AS p
            SET
                attachment_urls = t.new_urls_json::jsonb
            FROM (
                SELECT 
                    unnest(@ThreadIds) AS thread_id, 
                    unnest(@NewJsonStrings) AS new_urls_json
            ) AS t
            WHERE p.thread_id = t.thread_id;
            """;

        var threadIds = urlSignedInfos.Select(x => x.threadId.ToDbId()).ToArray();
        var newJsonUrl = urlSignedInfos.Select(x => x.urls.Value is null ? null : JsonSerializer.Serialize(x.urls.Value)).ToArray();

        var parameters = new
        {
            ThreadIds = threadIds,
            NewJsonStrings = newJsonUrl
        };
        
        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            int affectedRows = await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
            _logger.Info("✅ UpdateUrlInfoAsync: 批量更新了 {Count} 条帖子的附件URL信息", affectedRows);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ UpdateUrlInfoAsync: 批量更新帖子附件Url信息时数据库操作失败！涉及数量: {Count}", urlSignedInfos.Count);
            throw;
        }
    }
}
