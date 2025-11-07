using System.Text;
using System.Collections;
using LunariumBot.Net.Logger.Parser;

namespace LunariumBot.Net.Logger.Writer;

internal abstract class LogWriter : IDisposable
{
    protected readonly StringBuilder _stringBuilder;
    private bool _disposed;
    private const int DefaultMaxCapacity = 4 * 1024; // 默认4KB阈值

    public LogWriter()
    {
        _stringBuilder = new StringBuilder();
    }

    #region --- 池化管理 API ---

    /// <summary>
    /// 获取内部 StringBuilder 的当前容量, 供对象池进行健康度检查
    /// </summary>
    internal virtual int GetBufferCapacity() => _stringBuilder.Capacity;

    /// <summary>
    /// 尝试重置写入器以供重用, 如果对象因状态异常 (如缓冲区过大) 而不适合重用, 则返回 false
    /// </summary>
    /// <param name="maxCapacity">允许的最大缓冲区容量, 超过此容量的对象将被视为不健康, 默认4KB阈值</param>
    /// <returns>如果对象已成功重置并适合归还池中, 则为 true；否则为 false</returns>
    internal virtual bool TryReset(int maxCapacity = DefaultMaxCapacity)
    {
        if (_stringBuilder.Capacity > maxCapacity)
        {
            // 对象被污染, 不应归还
            return false;
        }

        // 对象健康, 清理并准备重用
        _stringBuilder.Clear();
        _disposed = false;
        // 如果派生类有其他状态, 它们应该重写此方法并调用 base.TryReset()
        return true;
    }

    #region --- 对象池归还 ---
    /// <summary>
    /// 将此 Writer 归还到对象池, 非 using 语句时手动调用
    /// </summary>
    public void Return()
    {
        Dispose();
    }

    /// <summary>
    /// 实现 IDisposable, 支持 using 语句自动归还
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // 调用子类的归还逻辑
        ReturnToPool();
        
        GC.SuppressFinalize(this);
    }

    // 抽象方法: 子类负责归还到自己的池
    protected abstract void ReturnToPool();

    #endregion
    #endregion

    #region --- 公共渲染入口 ---
    
    /// <summary>
    /// 渲染日志条目到内部缓冲区
    /// </summary>
    internal void Render(LogEntry logEntry)
    {
        BeginEntry();
        WriteTimestamp(logEntry.Timestamp);
        WriteLevel(logEntry.LogLevel);
        WriteContext(logEntry.Context);
        
        // 🎣 钩子：允许子类在渲染消息前插入额外逻辑(如 JSON 的 OriginalMessage)
        BeforeRenderMessage(logEntry);
        
        WriteRenderedMessage(logEntry.MessageTemplate._messageTemplateTokens, logEntry.Properties);
        
        // 🎣 钩子：允许子类在渲染消息后插入额外逻辑(如 JSON 的 PropertyValue)
        AfterRenderMessage(logEntry);
        
        WriteException(logEntry.Exception);
        EndEntry();
        _stringBuilder.AppendLine();
    }

    internal void FlushTo(TextWriter output)
    {
        // .NET Core/.NET 5+ 支持直接写StringBuilder，避免ToString()分配
        output.Write(_stringBuilder);
    }
    
    public override string ToString()
    {
        return _stringBuilder.ToString();
    }
    
    #endregion

    #region --- 抽象方法：子类必须实现 ---
    
    protected abstract LogWriter WriteTimestamp(DateTimeOffset timestamp);
    protected abstract LogWriter WriteLevel(LogLevel level);
    protected abstract LogWriter WriteContext(string? context);
    protected abstract LogWriter WriteRenderedMessage(MessageTemplateTokens[] tokens, object?[] propertys);
    protected abstract LogWriter WriteException(Exception? exception);
    
    #endregion

    #region --- 钩子方法：子类可选重写 ---
    
    protected virtual void BeforeRenderMessage(LogEntry logEntry) { }
    protected virtual void AfterRenderMessage(LogEntry logEntry) { }
    protected virtual LogWriter BeginEntry() { return this; }
    protected virtual LogWriter EndEntry() { return this; }

    #endregion

    #region --- 钩子方法：子类可选重写 ---
    // 判断对象是否为C#核心库中常见的集合类型
    protected static bool IsCommonCollectionType(object? obj)
    {
        if (obj == null)
            return false;

        // 排除string（虽然它实现了IEnumerable，但通常不被视为集合）
        if (obj is string)
            return false;

        Type type = obj.GetType();

        // 检查是否为数组
        if (type.IsArray)
            return true;

        // 检查是否实现了常见的集合接口
        return obj is IEnumerable ||
               obj is ICollection ||
               obj is IList ||
               obj is IDictionary;
    }
    #endregion
}