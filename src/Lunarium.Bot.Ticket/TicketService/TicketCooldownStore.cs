using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>冷却记录文件结构。</summary>
internal sealed class CooldownFile
{
    /// <summary>文件版本，便于以后迁移。</summary>
    public int Version { get; set; } = 1;

    /// <summary>键为 <c>{guildId}:{userId}</c>，值为最后一次提交时间。</summary>
    public Dictionary<string, DateTimeOffset> Entries { get; set; } = new();

    /// <summary>临时冷却豁免：键为 <c>{guildId}:{userId}</c>，值为过期时间（一次性）。</summary>
    public Dictionary<string, DateTimeOffset> TempPermissions { get; set; } = new();
}

/// <summary>冷却记录的源生成上下文。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CooldownFile))]
internal partial class CooldownJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 工单提交冷却（按 服务器 + 用户 记录最后一次提交时间）。
/// <para>
/// 只存一份小 JSON（<c>storage/cooldowns.json</c>）；读写全部发生在
/// <see cref="TicketManager"/> 的写锁内，因此内部用普通字典即可。
/// 启动时丢弃超过 31 天的记录（配置上限为 720 小时 = 30 天），避免文件无限增长。
/// </para>
/// </summary>
public sealed class TicketCooldownStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(31);

    // 不能用 TicketJson.Options：它已被 TicketJsonContext 封装并冻结，再传给另一个上下文会抛异常
    private static readonly CooldownJsonContext Context = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private readonly ILogger _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _tempPermissions = new(StringComparer.Ordinal);

    public TicketCooldownStore(ILogger logger, TicketLocalData localData)
    {
        _logger = logger.ForContext("TicketCooldownStore");
        _filePath = Path.Combine(localData.RootDirectory, "cooldowns.json");
    }

    /// <summary>启动时加载；文件损坏只记录并清空（冷却失效比启动失败更可接受）。</summary>
    public void Load()
    {
        lock (_gate)
        {
            _entries.Clear();
            _tempPermissions.Clear();
        }

        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(_filePath), Context.CooldownFile);
            if (file is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var cutoff = now - Retention;

            lock (_gate)
            {
                foreach (var (key, at) in file.Entries)
                {
                    if (at >= cutoff)
                    {
                        _entries[key] = at;
                    }
                }

                foreach (var (key, expireAt) in file.TempPermissions)
                {
                    if (expireAt > now)
                    {
                        _tempPermissions[key] = expireAt;
                    }
                }

                _logger.Info("冷却记录已加载: 提交 {Count} 条, 临时许可 {PermissionCount} 个",
                    _entries.Count, _tempPermissions.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "加载冷却记录失败，按空记录启动: {Path}", _filePath);
        }
    }

    /// <summary>距离下次可提交还剩多久；不受冷却限制时返回 <see langword="null"/>。</summary>
    public TimeSpan? GetRemaining(ulong guildId, ulong userId, TimeSpan cooldown, DateTimeOffset now)
    {
        if (cooldown <= TimeSpan.Zero)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(Key(guildId, userId), out var last))
            {
                return null;
            }

            var remaining = last + cooldown - now;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /// <summary>记录一次提交（内存）。调用方负责随后 <see cref="SaveAsync"/>。</summary>
    public void Record(ulong guildId, ulong userId, DateTimeOffset submittedAt)
    {
        lock (_gate)
        {
            _entries[Key(guildId, userId)] = submittedAt;
        }
    }

    /// <summary>
    /// 消耗一次临时许可；存在且未过期时返回 <see langword="true"/>（同时移除，保证只用一次）。
    /// </summary>
    public bool TryConsumeTempPermission(ulong guildId, ulong userId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var key = Key(guildId, userId);
            if (!_tempPermissions.TryGetValue(key, out var expireAt))
            {
                return false;
            }

            _tempPermissions.Remove(key);
            return expireAt > now;
        }
    }

    /// <summary>当前有效的临时许可过期时间；无有效许可返回 <see langword="null"/>。</summary>
    public DateTimeOffset? GetTempPermissionExpiry(ulong guildId, ulong userId, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _tempPermissions.TryGetValue(Key(guildId, userId), out var expireAt) && expireAt > now
                ? expireAt
                : null;
        }
    }

    /// <summary>授予一次性临时许可（1～72 小时），覆盖已有许可。</summary>
    public bool GrantTempPermission(
        ulong guildId,
        ulong userId,
        TimeSpan duration,
        DateTimeOffset now,
        out DateTimeOffset expireAt)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(72))
        {
            expireAt = default;
            return false;
        }

        expireAt = now + duration;

        lock (_gate)
        {
            _tempPermissions[Key(guildId, userId)] = expireAt;
        }

        return true;
    }

    /// <summary>原子写回磁盘。</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            CooldownFile snapshot;
            lock (_gate)
            {
                snapshot = new CooldownFile
                {
                    Entries = new Dictionary<string, DateTimeOffset>(_entries, StringComparer.Ordinal),
                    TempPermissions = new Dictionary<string, DateTimeOffset>(_tempPermissions, StringComparer.Ordinal),
                };
            }

            var json = JsonSerializer.Serialize(snapshot, Context.CooldownFile);

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

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
                    _logger.Warning("清理冷却临时文件失败: {Path} {Message}", tempPath, cleanupEx.Message);
                }

                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static string Key(ulong guildId, ulong userId) => $"{guildId}:{userId}";
}
