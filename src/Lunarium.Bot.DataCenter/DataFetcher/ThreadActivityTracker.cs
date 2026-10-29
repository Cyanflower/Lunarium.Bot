using System.Text.Json.Serialization;

using Lunarium.Common.Caching;
using Lunarium.Logging;

namespace Lunarium.Bot.DataCenter.DataFetcher;

/// <summary>
/// 需要深度刷新的帖子 ID 集合（脏集合）。
/// <para>
/// 触发源：Listener 的实时事件（消息/编辑/删除/帖子更新/反应）+ 10 分钟一次的活跃线程轮询。
/// 集合只保存 ID，不保存对象；周更/月更扫描按集合深挖首楼消息、附件与反应数。
/// </para>
/// <para>
/// 持久化使用 <see cref="JsonDocumentStore{T}"/>（原子写盘 + 防抖），进程重启后自动恢复。
/// <see cref="Add"/> 只写内存，<see cref="FlushAsync"/> 时整体落盘，避免每条事件都序列化整个文档。
/// </para>
/// </summary>
public sealed class ThreadActivityTracker : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly JsonDocumentStore<DirtyThreadDocument> _store;
    private readonly HashSet<ulong> _threadIds = [];
    private readonly object _gate = new();
    private bool _dirty;

    public ThreadActivityTracker(ILogger logger)
    {
        _logger = logger.ForContext("ThreadActivityTracker");
        _store = new JsonDocumentStore<DirtyThreadDocument>(
            documentName: "DirtyThreads",
            typeInfo: DirtyThreadDocumentJsonContext.Default.DirtyThreadDocument,
            logError: ex => _logger.Error(ex, "❌ 脏帖子集合持久化失败"),
            allowCorruptRebuild: true);

        foreach (var threadId in _store.Read().ThreadIds)
        {
            _threadIds.Add(threadId);
        }

        _logger.Info("📌 已加载待深挖帖子集合: {Count} 条", _threadIds.Count);
    }

    /// <summary>当前集合大小。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _threadIds.Count;
            }
        }
    }

    /// <summary>加入一个需要深挖的帖子 ID（幂等）。</summary>
    public void Add(ulong threadId)
    {
        lock (_gate)
        {
            if (_threadIds.Add(threadId))
            {
                _dirty = true;
            }
        }
    }

    /// <summary>批量加入（幂等）。</summary>
    public void AddRange(IEnumerable<ulong> threadIds)
    {
        lock (_gate)
        {
            foreach (var threadId in threadIds)
            {
                if (_threadIds.Add(threadId))
                {
                    _dirty = true;
                }
            }
        }
    }

    /// <summary>获取当前集合的快照。</summary>
    public IReadOnlyCollection<ulong> Snapshot()
    {
        lock (_gate)
        {
            return _threadIds.ToArray();
        }
    }

    /// <summary>移除已成功深挖并写入数据库的 ID。</summary>
    public void RemoveConsumed(IEnumerable<ulong> threadIds)
    {
        lock (_gate)
        {
            foreach (var threadId in threadIds)
            {
                if (_threadIds.Remove(threadId))
                {
                    _dirty = true;
                }
            }
        }
    }

    /// <summary>清空集合（月度全量扫描成功后调用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_threadIds.Count == 0)
            {
                return;
            }

            _threadIds.Clear();
            _dirty = true;
        }
    }

    /// <summary>把内存集合整体落盘（仅在集合发生变化时执行）。</summary>
    public async Task FlushAsync()
    {
        DirtyThreadDocument document;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            document = new DirtyThreadDocument { ThreadIds = [.. _threadIds] };
            _dirty = false;
        }

        _store.Set(document);
        await _store.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushAsync();
        }
        finally
        {
            await _store.DisposeAsync();
        }
    }
}

/// <summary>脏集合的持久化文档。</summary>
public sealed class DirtyThreadDocument
{
    public List<ulong> ThreadIds { get; set; } = [];
}

[JsonSerializable(typeof(DirtyThreadDocument))]
internal sealed partial class DirtyThreadDocumentJsonContext : JsonSerializerContext
{
}
