using System.Globalization;
using System.Text.Json;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>
/// 工单的本地文件存储层：只负责路径解析、读写与归档移动，不持有任何业务状态。
/// <para>目录结构（相对 <see cref="AppContext.BaseDirectory"/>）：</para>
/// <code>
/// storage/
/// ├── TicketId.json                    发号器（CacheStore 维护）
/// ├── tickets/ticket-0001/ticket.json  活跃工单
/// └── archive/ticket-0001/ticket.json  归档工单（归档时整体移动）
/// </code>
/// <para>
/// 写入一律"同目录 .tmp + 原子改名"，避免进程崩在中途留下半截文件；
/// 归档用 <see cref="Directory.Move(string, string)"/>（同盘 rename），
/// 一次操作同时完成"移出活跃集 + 进入归档"，不需要额外的索引文件。
/// </para>
/// </summary>
public sealed class TicketLocalData
{
    private const string ActiveDirectoryName = "tickets";
    private const string ArchiveDirectoryName = "archive";
    private const string TicketDirectoryPrefix = "ticket-";

    /// <summary>工单数据文件名（在工单目录根下）。</summary>
    public const string TicketFileName = "ticket.json";

    private readonly ILogger _logger;
    private readonly string _root;
    private readonly string _activeRoot;
    private readonly string _archiveRoot;

    public TicketLocalData(ILogger logger)
    {
        _logger = logger.ForContext("TicketLocalData");

        _root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(TicketManager.DataDirectory, AppContext.BaseDirectory));
        _activeRoot = Path.Combine(_root, ActiveDirectoryName);
        _archiveRoot = Path.Combine(_root, ArchiveDirectoryName);
    }

    /// <summary>存储根目录（<c>storage</c>）。</summary>
    public string RootDirectory => _root;

    /// <summary>活跃工单目录（<c>storage/tickets</c>）。</summary>
    public string ActiveRoot => _activeRoot;

    /// <summary>归档工单目录（<c>storage/archive</c>）。</summary>
    public string ArchiveRoot => _archiveRoot;

    /// <summary>
    /// 读取全部活跃工单。启动时调用一次；单个文件损坏只记录并跳过，不阻断启动。
    /// </summary>
    public IReadOnlyList<TicketData> LoadAllActive()
    {
        if (!Directory.Exists(_activeRoot))
        {
            return [];
        }

        var tickets = new List<TicketData>();
        foreach (var directory in Directory.EnumerateDirectories(
                     _activeRoot, $"{TicketDirectoryPrefix}*", SearchOption.TopDirectoryOnly))
        {
            var ticket = TryLoad(Path.Combine(directory, TicketFileName));
            if (ticket is not null)
            {
                tickets.Add(ticket);
            }
        }

        return tickets;
    }

    /// <summary>按工单号读取（先活跃目录，再归档目录）。</summary>
    public TicketData? Load(int id)
    {
        return TryLoad(Path.Combine(TicketDirectory(_activeRoot, id), TicketFileName))
            ?? TryLoad(Path.Combine(TicketDirectory(_archiveRoot, id), TicketFileName));
    }

    /// <summary>
    /// 全量原子覆盖写。
    /// 目录按现状选择：活跃目录存在就写活跃目录，否则归档目录存在就写归档目录，都没有则新建活跃目录。
    /// </summary>
    public void Save(TicketData ticket)
    {
        ticket.LastUpdatedAt = DateTimeOffset.UtcNow;

        var directory = ResolveDirectory(ticket.Id);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, TicketFileName);
        var tempPath = path + ".tmp";

        // 先在内存完成序列化，再动文件（避免持锁/半途失败）
        var json = JsonSerializer.Serialize(ticket, TicketJson.Context.TicketData);

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 把活跃工单目录整体移动到归档目录（同盘 rename）。
    /// 源目录不存在时视为已移动（幂等）。
    /// </summary>
    public void MoveToArchive(int id)
    {
        var source = TicketDirectory(_activeRoot, id);
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(_archiveRoot);
        var destination = TicketDirectory(_archiveRoot, id);

        if (Directory.Exists(destination))
        {
            // 理论上不会发生（工单号全局唯一）；保留现场并报错，交给人处理
            throw new IOException($"归档目录已存在，拒绝覆盖: {destination}");
        }

        Directory.Move(source, destination);
        _logger.Info("工单 {TicketId} 已移入归档目录: {Destination}", id, destination);
    }

    /// <summary>工单目录内的附件根目录（原图/原文件，打包时不会改动这里的原件）。</summary>
    public string GetAttachmentDirectory(int id)
    {
        return Path.Combine(ResolveDirectory(id), "attachment");
    }

    /// <summary>工单目录的绝对路径（活跃优先，其次归档）。</summary>
    public string GetTicketDirectory(int id)
    {
        return ResolveDirectory(id);
    }

    /// <summary>归档 zip 的缓存路径（只在归档目录下）。</summary>
    public string GetArchiveZipPath(int id)
    {
        return Path.Combine(TicketDirectory(_archiveRoot, id), "archive.zip");
    }

    /// <summary>
    /// 活跃与归档目录中出现过的最大工单号；没有工单时返回 0。
    /// 启动时用于与发号器对账（CacheStore 防抖落盘可能丢最后一次自增）。
    /// </summary>
    public int GetMaxTicketId()
    {
        var max = 0;
        max = Math.Max(max, GetMaxTicketIdIn(_activeRoot));
        max = Math.Max(max, GetMaxTicketIdIn(_archiveRoot));
        return max;
    }

    private static int GetMaxTicketIdIn(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var max = 0;
        foreach (var directory in Directory.EnumerateDirectories(
                     root, $"{TicketDirectoryPrefix}*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (name.Length > TicketDirectoryPrefix.Length
                && int.TryParse(name.AsSpan(TicketDirectoryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                max = Math.Max(max, id);
            }
        }

        return max;
    }

    private string ResolveDirectory(int id)
    {
        var active = TicketDirectory(_activeRoot, id);
        if (Directory.Exists(active))
        {
            return active;
        }

        var archive = TicketDirectory(_archiveRoot, id);
        return Directory.Exists(archive) ? archive : active;
    }

    private static string TicketDirectory(string root, int id)
    {
        return Path.Combine(root, $"{TicketDirectoryPrefix}{id:D5}");
    }

    /// <summary>
    /// 把旧版 4 位目录名（<c>ticket-0001</c>）迁移为 5 位（<c>ticket-00001</c>）。
    /// 目标已存在时跳过并记录，避免覆盖。幂等，可在每次启动时调用。
    /// </summary>
    public void MigrateLegacyDirectoryNames()
    {
        MigrateLegacyDirectoryNames(_activeRoot, ActiveDirectoryName);
        MigrateLegacyDirectoryNames(_archiveRoot, ArchiveDirectoryName);
    }

    private void MigrateLegacyDirectoryNames(string root, string label)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     root, $"{TicketDirectoryPrefix}*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (!int.TryParse(name.AsSpan(TicketDirectoryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var expectedName = $"{TicketDirectoryPrefix}{id:D5}";
            if (string.Equals(name, expectedName, StringComparison.Ordinal))
            {
                continue;
            }

            var target = Path.Combine(root, expectedName);
            if (Directory.Exists(target))
            {
                _logger.Warning("跳过 {Label} 目录迁移（目标已存在）: {From}", label, directory);
                continue;
            }

            Directory.Move(directory, target);
            _logger.Info("已迁移 {Label} 工单目录: {From} -> {Expected}", label, name, expectedName);
        }
    }

    private TicketData? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), TicketJson.Context.TicketData);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 单张力损不应拖死整个 Bot：记录后跳过，人工修复
            _logger.Error(ex, "加载工单文件失败，已跳过: {Path}", path);
            return null;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不应掩盖原始异常
            _logger.Warning("清理临时文件失败: {Path}, {Message}", path, ex.Message);
        }
    }
}
