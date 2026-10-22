using System.Collections.Concurrent;

using Lunarium.Bot.Ticket.Model;
using Lunarium.Common.Caching;
using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>建单结果：成功或仍在冷却中。</summary>
/// <param name="Ticket">已落盘的工单；冷却时为 <see langword="null"/>。</param>
/// <param name="CooldownRemaining">距离可再次提交的剩余时间；成功时为 <see langword="null"/>。</param>
public sealed record TicketCreationResult(TicketData? Ticket, TimeSpan? CooldownRemaining)
{
    public bool Success => Ticket is not null;

    public static TicketCreationResult Ok(TicketData ticket) => new(ticket, null);

    public static TicketCreationResult CoolingDown(TimeSpan remaining) => new(null, remaining);
}

/// <summary>
/// 工单的业务编排层：发号、内存索引、状态落盘。
/// <para>
/// 持久化细节全部托给 <see cref="TicketLocalData"/>；本类只保证：
/// 工单号全局唯一且单调、内存索引与磁盘一致、所有写操作串行化。
/// </para>
/// <para>
/// 并发模型：工单操作是低频交互，统一用一把 <see cref="SemaphoreSlim"/> 串行化写，
/// 不做 per-ticket 锁——简单且不会死锁。读走 <see cref="ConcurrentDictionary"/>，无锁。
/// </para>
/// </summary>
public class TicketManager : IAsyncDisposable
{
    /// <summary>本地存储根目录名（相对程序目录）。</summary>
    public const string DataDirectory = "storage";

    private readonly ILogger _logger;
    private readonly TicketLocalData _localData;
    private readonly TicketCooldownStore _cooldowns;
    private readonly CacheStore _cacheStore;
    private readonly TicketId _ticketId = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TicketData> _byId = new();
    private readonly ConcurrentDictionary<ulong, int> _byChannel = new();
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();
    private volatile bool _isDisposed = false;

    public TicketManager(ILogger logger, TicketLocalData localData, TicketCooldownStore cooldowns)
    {
        _logger = logger.ForContext("TicketManager");
        _localData = localData;
        _cooldowns = cooldowns;
        _cacheStore = new(
            cacheNamespace: "TicketId",
            logError: HandleOnError,
            allowCorruptRebuild: false,
            storageDirectory: DataDirectory);

        _ticketId.LastTicketId = _cacheStore.Get<int>("TicketId", 0);
    }

    /// <summary>
    /// 启动时加载活跃工单、重建内存索引，并与磁盘对账工单号。
    /// 必须在 Bot 登录（即任何交互进来）之前调用一次。
    /// </summary>
    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _cooldowns.Load();

        // 旧版 4 位目录名迁移为 5 位（幂等）
        _localData.MigrateLegacyDirectoryNames();

        var tickets = _localData.LoadAllActive();
        foreach (var ticket in tickets)
        {
            // 崩溃在"已写 ArchiveInfo 但还没移动目录"之间：这里补完归档
            if (ticket.ArchiveInfo is not null)
            {
                _localData.MoveToArchive(ticket.Id);
                _localData.Save(ticket);
                _logger.Warning("工单 {TicketId} 处于已归档但仍在活跃目录，已补完归档移动", ticket.Id);
                continue;
            }

            _byId[ticket.Id] = ticket;
            if (ticket.ChannelId != 0)
            {
                _byChannel[ticket.ChannelId] = ticket.Id;
            }
        }

        // 发号器对账：CacheStore 是防抖落盘，崩溃可能丢最后一次自增 → 以磁盘最大号为准
        var maxId = _localData.GetMaxTicketId();
        if (maxId > _ticketId.LastTicketId)
        {
            _logger.Warning("发号器落后于磁盘（计数器 {Counter}, 磁盘最大 {MaxId}），已修正", _ticketId.LastTicketId, maxId);
            _ticketId.LastTicketId = maxId;
        }

        _cacheStore.Set<int>("TicketId", _ticketId.LastTicketId);
        await _cacheStore.FlushAsync();

        _logger.Info("工单存储已加载: 活跃 {ActiveCount} 个, 下一编号 {NextId}", _byId.Count, _ticketId.LastTicketId + 1);
    }

    /// <summary>
    /// 发号并创建一张待审核工单（立即落盘）。
    /// <para>返回的工单已持久化；后续的频道/消息 ID 由调用方回写后再 <see cref="SaveAsync"/>。</para>
    /// </summary>
    public async Task<TicketCreationResult> CreateTicketAsync(
        TicketType ticketType,
        TicketCategory ticketCategory,
        TicketCreator ticketCreator,
        ulong guildId,
        string content,
        int autoApproveHours,
        string? secondaryContent = null,
        TimeSpan? cooldown = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _writeGate.WaitAsync();
        try
        {
            var createdAt = DateTimeOffset.UtcNow;

            // 冷却检查与发号在同一把锁内：两个弹窗同时提交时只会成功一个
            // 有有效临时许可时消耗掉本次提交的冷却限制（保存随后的 SaveAsync 一起落盘）
            if (cooldown is { } cd
                && !_cooldowns.TryConsumeTempPermission(guildId, ticketCreator.UserId, createdAt)
                && _cooldowns.GetRemaining(guildId, ticketCreator.UserId, cd, createdAt) is { } remaining)
            {
                _logger.Info("工单提交仍在冷却中: GuildId={GuildId} UserId={UserId} 剩余={Remaining}",
                    guildId, ticketCreator.UserId, remaining);
                return TicketCreationResult.CoolingDown(remaining);
            }

            var newId = _ticketId.Next();

            // 立刻刷盘：CacheStore 默认 5 秒防抖，崩溃会回退计数器 → 工单号复用
            _cacheStore.Set<int>("TicketId", newId);
            await _cacheStore.FlushAsync();

            // autoApproveHours = 0 表示不自动通过，用 null 表示"永不"
            var ticket = new TicketData
            {
                Id = newId,
                GuildId = guildId,
                ChannelId = 0,
                StatusChannelId = 0,
                StatusMessageId = 0,
                AuditChannelId = 0,
                AuditMessageId = 0,
                TicketType = ticketType,
                TicketCategory = ticketCategory,
                TicketCreator = ticketCreator,
                Content = content,
                SecondaryContent = secondaryContent,
                CreatedAt = createdAt,
                LastUpdatedAt = createdAt,
                AutoApproveTime = autoApproveHours > 0
                    ? createdAt.AddHours(autoApproveHours)
                    : null,
            };

            _localData.Save(ticket);
            _byId[newId] = ticket;

            // 工单已落盘才记冷却：写盘失败不应占用用户的提交额度
            _cooldowns.Record(guildId, ticketCreator.UserId, createdAt);
            try
            {
                await _cooldowns.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存冷却记录失败（内存中的冷却仍然生效，重启后会丢失）");
            }

            _logger.Info("工单已创建: {Ticket} GuildId={GuildId} Type={Type} Category={Category} 创建人={CreatorId}",
                ticket, guildId, ticketType, ticketCategory, ticketCreator.UserId);

            return TicketCreationResult.Ok(ticket);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>全量保存一张活跃工单并刷新内存索引。</summary>
    public async Task SaveAsync(TicketData ticket)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _writeGate.WaitAsync();
        try
        {
            _localData.Save(ticket);
            _byId[ticket.Id] = ticket;

            if (ticket.ChannelId != 0)
            {
                _byChannel[ticket.ChannelId] = ticket.Id;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>把工单移入归档（移动目录 + 写入终态），并从频道索引中移除。</summary>
    public async Task ArchiveAsync(TicketData ticket)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _writeGate.WaitAsync();
        try
        {
            // 先写终态，再移动目录：即使移动前崩溃，启动对账也能凭 ArchiveInfo 补完
            _localData.Save(ticket);
            _localData.MoveToArchive(ticket.Id);
            _localData.Save(ticket);

            // 归档后不再属于活跃集；后续按 ID 查询走磁盘
            _byId.TryRemove(ticket.Id, out _);
            if (ticket.ChannelId != 0)
            {
                _byChannel.TryRemove(ticket.ChannelId, out _);
            }

            _logger.Info("工单已归档: {Ticket}", ticket);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>按工单号查活跃工单。</summary>
    public bool TryGet(int id, out TicketData? ticket) => _byId.TryGetValue(id, out ticket);

    /// <summary>按工单号读取（活跃优先，回落到磁盘上的归档数据）。</summary>
    public TicketData? LoadAny(int id) => _byId.TryGetValue(id, out var ticket) ? ticket : _localData.Load(id);

    /// <summary>
    /// 尝试占用某张工单的处理权（审批/拒绝）。防止两个管理员同时点按钮时重复建频道。
    /// 单进程内有效；占用方必须在 finally 中调用 <see cref="EndTransition"/>。
    /// </summary>
    public bool TryBeginTransition(int ticketId) => _inFlight.TryAdd(ticketId, 0);

    /// <summary>释放 <see cref="TryBeginTransition"/> 占用的处理权。</summary>
    public void EndTransition(int ticketId) => _inFlight.TryRemove(ticketId, out _);

    /// <summary>按工单频道ID反查工单（启动时由活跃工单重建索引）。</summary>
    public bool TryGetByChannel(ulong channelId, out TicketData? ticket)
    {
        ticket = null;
        return _byChannel.TryGetValue(channelId, out var id) && _byId.TryGetValue(id, out ticket);
    }

    /// <summary>当前全部活跃工单。</summary>
    public IReadOnlyCollection<TicketData> GetActiveTickets() => _byId.Values.ToArray();

    /// <summary>取已到自动通过时刻、仍处于待审核的工单（后台任务用）。</summary>
    public IReadOnlyList<TicketData> GetDueAutoApprove(DateTimeOffset now)
    {
        return _byId.Values
            .Where(t => t.Status == TicketStatus.Pending
                        && t.AutoApproveTime is { } due
                        && due <= now)
            .OrderBy(t => t.AutoApproveTime)
            .ToArray();
    }

    private void HandleOnError(Exception ex)
    {
        _logger.Error(ex, "内部缓存储存发生错误");
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _writeGate.Dispose();
        await _cacheStore.DisposeAsync();
    }
}