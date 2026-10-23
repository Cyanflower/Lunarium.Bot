using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>待删除的归档频道。</summary>
public sealed class PendingDeletion
{
    /// <summary>所属服务器ID。</summary>
    public ulong GuildId { get; set; }

    /// <summary>要删除的频道ID。</summary>
    public ulong ChannelId { get; set; }

    /// <summary>对应工单号（仅用于日志/排查）。</summary>
    public int TicketId { get; set; }

    /// <summary>计划删除时间。</summary>
    public DateTimeOffset DeleteAt { get; set; }

    /// <summary>已尝试次数（失败后按 5 分钟退避，最多 3 次）。</summary>
    public int Attempts { get; set; }
}

/// <summary>删除队列文件结构。</summary>
internal sealed class DeletionFile
{
    /// <summary>文件版本。</summary>
    public int Version { get; set; } = 1;

    /// <summary>待删除项。</summary>
    public List<PendingDeletion> Entries { get; set; } = new();
}

/// <summary>删除队列的源生成上下文。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DeletionFile))]
internal partial class DeletionJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 归档频道删除队列（<c>storage/pending-deletions.json</c>）。
/// <para>
/// 写入磁盘是为了让"归档后延时删除频道"能跨重启继续执行；
/// 由 <see cref="TicketMaintenanceService"/> 每 10 秒扫描一次。
/// </para>
/// </summary>
public sealed class TicketDeletionQueue
{
    /// <summary>失败重试的最大次数与退避间隔。</summary>
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>超过 30 天的残留项直接丢弃（避免文件无限增长）。</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    // 独立的 options：不能复用已被其它上下文封装的实例
    private static readonly DeletionJsonContext Context = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private readonly ILogger _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly List<PendingDeletion> _entries = new();

    public TicketDeletionQueue(ILogger logger, TicketLocalData localData)
    {
        _logger = logger.ForContext("TicketService.TicketDeletionQueue");
        _filePath = Path.Combine(localData.RootDirectory, "pending-deletions.json");
    }

    /// <summary>启动时加载；文件损坏只记录并清空。</summary>
    public void Load()
    {
        lock (_gate)
        {
            _entries.Clear();

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                var file = JsonSerializer.Deserialize(File.ReadAllText(_filePath), Context.DeletionFile);
                if (file is null)
                {
                    return;
                }

                var cutoff = DateTimeOffset.UtcNow - Retention;
                foreach (var entry in file.Entries)
                {
                    if (entry.ChannelId != 0 && entry.DeleteAt >= cutoff)
                    {
                        _entries.Add(entry);
                    }
                }

                _logger.Info("删除队列已加载: {Count} 项", _entries.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "加载删除队列失败，按空队列启动: {Path}", _filePath);
            }
        }
    }

    /// <summary>加入队列；同一频道已存在时覆盖。</summary>
    public void Enqueue(ulong guildId, ulong channelId, int ticketId, DateTimeOffset deleteAt)
    {
        lock (_gate)
        {
            _entries.RemoveAll(entry => entry.ChannelId == channelId);
            _entries.Add(new PendingDeletion
            {
                GuildId = guildId,
                ChannelId = channelId,
                TicketId = ticketId,
                DeleteAt = deleteAt,
            });
        }
    }

    /// <summary>取已到时间的待删除项（快照）。</summary>
    public IReadOnlyList<PendingDeletion> GetDue(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _entries.Where(entry => entry.DeleteAt <= now).ToArray();
        }
    }

    /// <summary>移除一项（删除成功、频道已不存在或放弃重试）。</summary>
    public void Remove(ulong channelId)
    {
        lock (_gate)
        {
            _entries.RemoveAll(entry => entry.ChannelId == channelId);
        }
    }

    /// <summary>记录一次失败：未达上限时按 5 分钟退避，达到上限则放弃。</summary>
    /// <returns>是否已放弃（已从队列移除）。</returns>
    public bool RecordFailure(ulong channelId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(item => item.ChannelId == channelId);
            if (entry is null)
            {
                return true;
            }

            entry.Attempts++;
            if (entry.Attempts >= MaxAttempts)
            {
                _entries.Remove(entry);
                return true;
            }

            entry.DeleteAt = now + RetryDelay;
            return false;
        }
    }

    /// <summary>原子写回磁盘。</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            DeletionFile snapshot;
            lock (_gate)
            {
                snapshot = new DeletionFile
                {
                    Entries = _entries.Select(entry => new PendingDeletion
                    {
                        GuildId = entry.GuildId,
                        ChannelId = entry.ChannelId,
                        TicketId = entry.TicketId,
                        DeleteAt = entry.DeleteAt,
                        Attempts = entry.Attempts,
                    }).ToList(),
                };
            }

            var json = JsonSerializer.Serialize(snapshot, Context.DeletionFile);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var tempPath = _filePath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    _logger.Warning("清理删除队列临时文件失败: {Path} {Message}", tempPath, cleanupEx.Message);
                }

                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
