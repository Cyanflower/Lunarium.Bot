using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using LunariumBot.Net.Logger.Interfaces;
using LunariumBot.Net.Logger.Formatting;

namespace LunariumBot.Net.Logger.Sink;

/// <summary>
/// 一个将日志写入文件的 Sink，并支持根据文件大小进行轮转。
/// 当文件达到指定大小时，会自动创建一个新的带时间戳的日志文件，并可以限制日志文件的最大数量。
/// </summary>
public class RotatingFileSink : ILogSink
{
    // 锁，虽然 Emit 是串行的，但在轮转等复杂操作时加个保险总是好的
    private readonly object _lock = new();

    // 定时刷新缓冲区
    private readonly Timer _flushTimer;
    private bool _disposed = false;

    // === 日志配置 ===
    // 日志文件的基础路径和文件名
    private string? _currentLogFilePath;
    private readonly string _logFilePath;
    // 单个日志文件的最大大小, 单位：字节, 0或负数为无限制
    private readonly long _maxFileSize;
    // 最大轮转文件数量, 0或负数为无限制
    private readonly int _maxFile;

    // === 当前文件信息 ===
    // 当前文件写入流
    private StreamWriter? _writer;
    // 当前文件大小
    private long _currentFileSize;

    /// <summary>
    /// 初始化一个新的、按大小轮转的文件日志 Sink。
    /// </summary>
    /// <param name="logFilePath">日志文件的基础路径。例如 "logs/app.log"。</param>
    /// <param name="maxFileSizeMB">
    /// 单个文件的最大大小 (MB)。当文件超过此大小时会触发轮转。
    /// 设置为 0 或负数则不限制大小。
    /// ⚠️ 注意：当 maxFile > 0 时，maxFileSizeMB 必须 > 0。
    /// </param>
    /// <param name="maxFile">
    /// 保留的最大日志文件数量。当文件轮转时，如果总文件数超过此值，最旧的文件将被删除。
    /// 设置为 0 或负数则不限制数量。
    /// </param>
    /// <exception cref="ArgumentException">
    /// 当配置了文件数量限制 (maxFile > 0) 却没有配置文件大小限制 (maxFileSizeMB ≤ 0) 时抛出。
    /// </exception>
    public RotatingFileSink(string logFilePath, double maxFileSizeMB = 10, int maxFile = 10)
    {
        _logFilePath = logFilePath;
        _maxFile = maxFile;
        // 将 MB 转换为字节
        _maxFileSize = maxFileSizeMB > 0 ? (long)(maxFileSizeMB * 1024 * 1024) : -1;

        // 检查逻辑上矛盾的配置：限制文件数量，但不限制文件大小
        if (_maxFile > 0 && _maxFileSize <= 0)
        {
            throw new ArgumentException("Invalid configuration: When maxFile is limited, maxFileSizeMB must also be greater than 0.");
        }

        _flushTimer = new Timer(
            callback: FlushTimerCallback, 
            state: null, 
            dueTime: TimeSpan.FromSeconds(10), 
            period: TimeSpan.FromSeconds(10));
    }

    private void FlushTimerCallback(object? state)
    {
        try
        {
            // 获取锁，防止和 Emit 方法争用
            lock (_lock)
            {
                // 检查一下是否已经被 Dispose 了，防止在对象销毁后定时器还在错误地执行
                if (_disposed)
                {
                    return;
                }
                // [Debug]
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer is flushing the log buffer...");

                // 检查文件存在性
                EnsureFileExists();

                // 直接调用 Flush，把缓冲区的内容写入 log 文件
                _writer?.Flush(); 
            }
        }
        catch (Exception ex)
        {
            var logErrMsg = $"[LunariumLogger.InternalError] Critical error in timer flush: {ex.Message}".Color(ConsoleColor.DarkRed);
            Console.WriteLine(logErrMsg);
        }
    }

    /// <summary>
    /// 将日志条目写入文件。在写入前会检查是否需要进行文件轮转。
    /// </summary>
    /// <param name="entry">要写入的日志条目。</param>
    public void Emit(LogEntry entry, bool toJson, bool indented)
    {
        lock (_lock)
        {
            // 检查是否需要轮转
            CheckForRotation();

            // 如果 writer 因为某些原因没创建成功，就放弃写入
            if (_writer == null)
            {
                var log_err_msg = "Log Writer FAILED: _writer is null".Color(ConsoleColor.DarkMagenta);
                Console.WriteLine(log_err_msg);
                return;
            }

            string formattedMessage = FileFormatter.Format(entry, toJson, indented);

            _writer.WriteLine(formattedMessage);

            // 分级刷新策略
            if (entry.Level >= LogLevel.Error)
            {
                // 检查文件存在性
                EnsureFileExists();
                _writer.Flush();
            }

            // 更新当前文件大小。使用 BaseStream.Position 更准确
            _currentFileSize = _writer.BaseStream.Position;
        }
    }

    /// <summary>
    /// 检查目标文件是否还存在
    /// </summary>
    private void EnsureFileExists()
    {
        // 如果当前没有 writer，或者文件被删了
        if (_writer == null || !File.Exists(_currentLogFilePath))
        {   
            // 和 CheckForRotation 相同的逻辑
            CloseCurrentWriter();
            OpenFile();
            if (_writer != null && _maxFile > 0)
            {
                CleanupOldFiles();
            }
        }
    }

    /// <summary>
    /// 检查是否满足文件轮转的条件。
    /// 轮转会在第一次写入时，或当前文件大小超过限制时触发。
    /// </summary>
    private void CheckForRotation()
    {
        // 如果写入器是空的（第一次写入），或者需要根据大小滚转
        if (_writer == null || (_maxFileSize > 0 && _currentFileSize >= _maxFileSize))
        {
            // 关闭当前的文件流
            CloseCurrentWriter();

            // 打开一个新的文件流
            OpenFile();

            // 只有在新文件成功打开后，才清理旧文件
            if (_writer != null && _maxFile > 0)
            {
                CleanupOldFiles();
            }
        }
    }
    
    /// <summary>
    /// 打开一个新的日志文件用于写入。
    /// 如果配置了大小限制，会寻找最新的未满文件或创建新文件。
    /// 如果未配置大小限制，则始终使用同一个文件。
    /// </summary>
    private void OpenFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string currentFilePath;

            // 如果不限制大小，就一直用同一个文件名
            if (_maxFileSize <= 0)
            {
                currentFilePath = _logFilePath;
            }
            else
            {
                // 有大小限制时，先尝试找到最新的未满文件，或创建新文件
                currentFilePath = FindLatestLogFileOrCreateNew();
            }

            // 创建可以追加写入的 StreamWriter
            _writer = new StreamWriter(currentFilePath, append: true, Encoding.UTF8)
            {
                AutoFlush = false
            };

            // 更新当前文件大小
            _currentFileSize = new FileInfo(currentFilePath).Exists ? new FileInfo(currentFilePath).Length : 0;

            _currentLogFilePath = currentFilePath;
        }
        catch (Exception ex)
        {
            var logErrMsg = $"[LunariumLogger.InternalError] Failed to open log file: {ex.Message}".Color(ConsoleColor.DarkRed);
            Console.WriteLine(logErrMsg);
            _writer = null;
        }
    }

    /// <summary>
    /// 查找最新的且未达到大小限制的日志文件。如果找不到，则生成一个带时间戳的新文件名。
    /// </summary>
    /// <returns>可用于写入的日志文件的完整路径。</returns>
    private string FindLatestLogFileOrCreateNew()
    {
        var directory = Path.GetDirectoryName(_logFilePath);
        var fileName = Path.GetFileNameWithoutExtension(_logFilePath);
        var extension = Path.GetExtension(_logFilePath);

        // 查找所有符合命名规则的日志文件, e.g., "app-2023-10-27-10-30-00.log"
        var pattern = new Regex($@"^{Regex.Escape(fileName)}-\d{{4}}-\d{{2}}-\d{{2}}-\d{{2}}-\d{{2}}-\d{{2}}{Regex.Escape(extension)}$");

        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            var latestFile = Directory.GetFiles(directory, $"{fileName}-*{extension}")
                                    .Where(f => pattern.IsMatch(Path.GetFileName(f)))
                                    .Select(f => new FileInfo(f))
                                    .OrderByDescending(f => f.Name) // 按名称排序即可（因为时间戳在名称里）
                                    .FirstOrDefault();

            // 检查最新的文件是否还有空间
            if (latestFile != null && latestFile.Length < _maxFileSize)
            {
                // 最新文件还没满，继续使用
                return latestFile.FullName;
            }
        }

        // 没有合适的现有文件，创建新文件
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        return Path.Combine(directory ?? "", $"{fileName}-{timestamp}{extension}");
    }

    /// <summary>
    /// 清理旧的日志文件。如果日志文件数量超过了 `_maxFile` 的限制，则会删除最旧的文件。
    /// </summary>
    private void CleanupOldFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            var fileName = Path.GetFileNameWithoutExtension(_logFilePath);
            var extension = Path.GetExtension(_logFilePath);

            // 查找所有符合命名规则的日志文件
            var pattern = new Regex($@"^{Regex.Escape(fileName)}-\d{{4}}-\d{{2}}-\d{{2}}-\d{{2}}-\d{{2}}-\d{{2}}{Regex.Escape(extension)}$");

            var files = Directory.GetFiles(directory, $"{fileName}-*{extension}")
                                .Where(f => pattern.IsMatch(Path.GetFileName(f)))
                                .Select(f => new FileInfo(f))
                                .OrderByDescending(f => f.Name)
                                .ToList();

            // 如果文件数量超过限制，删除最旧的
            if (files.Count > _maxFile)
            {
                // 跳过最新的 _maxFile 个文件，其余的都删除
                var filesToDelete = files.Skip(_maxFile);
                foreach (var file in filesToDelete)
                {
                    file.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            var logErrMsg = $"[LunariumLogger.InternalError] Failed to clean up old log files: {ex.Message}".Color(ConsoleColor.DarkRed);
            Console.WriteLine(logErrMsg);
        }
    }

    /// <summary>
    /// 安全地关闭并释放当前的 StreamWriter。
    /// </summary>
    private void CloseCurrentWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    /// <summary>
    /// 释放此 Sink 持有的所有资源，主要是确保文件写入流被正确关闭。
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            // 停止并释放定时器
            _flushTimer?.Change(Timeout.Infinite, 0);
            _flushTimer?.Dispose();

            // 关闭文件流
            CloseCurrentWriter();

            // 标记为已释放
            _disposed = true;
        }
    }
}

/// <summary>
/// 一个将日志写入文件的 Sink，并支持根据日期进行轮转（每天一个新文件）。
/// 还可以配置保留的最大日志文件数量。
/// </summary>
public class TimedRotatingFileSink : ILogSink
{
    // 锁，用于在轮转等操作时保证线程安全
    private readonly object _lock = new();

    // 定时刷新缓冲区
    private readonly Timer _flushTimer;
    private bool _disposed = false;

    // === 日志配置 ===
    // 日志文件的基础路径和文件名
    private string? _currentLogFilePath;
    private readonly string _logFilePath;
    // 最大轮转文件数量, 0或负数为无限制
    private readonly int _maxFile;

    // === 当前文件信息 ===
    // 当前文件写入流
    private StreamWriter? _writer;
    // 当前文件的日期，用于判断是否需要轮转
    private DateTime? _currentFileDate;
    
    /// <summary>
    /// 初始化一个新的、按天轮转的文件日志 Sink。
    /// 文件名将自动附加日期，例如 "app.log" 会变为 "app-2023-10-27.log"。
    /// </summary>
    /// <param name="logFilePath">日志文件的基础路径。</param>
    /// <param name="maxFile">
    /// 保留的最大日志文件数量。0 或负数表示不限制数量，不会删除旧文件。
    /// </param>
    public TimedRotatingFileSink(string logFilePath, int maxFile = 0)
    {
        _logFilePath = logFilePath;
        _maxFile = maxFile;
        _flushTimer = new Timer(
            callback: FlushTimerCallback,
            state: null,
            dueTime: TimeSpan.FromSeconds(10),
            period: TimeSpan.FromSeconds(10));
    }

    private void FlushTimerCallback(object? state)
    {
        try
        {
            // 获取锁，防止和 Emit 方法争用
            lock (_lock)
            {
                // 检查一下是否已经被 Dispose 了，防止在对象销毁后定时器还在错误地执行
                if (_disposed)
                {
                    return;
                }
                // [Debug]
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timer is flushing the log buffer...");
                
                // 检查文件存在性
                EnsureFileExists();

                // 直接调用 Flush，把缓冲区的内容写入 log 文件
                _writer?.Flush(); 
            }
        }
        catch (Exception ex)
        {
            var logErrMsg = $"[LunariumLogger.InternalError] Critical error in timer flush: {ex.Message}".Color(ConsoleColor.DarkRed);
            Console.WriteLine(logErrMsg);
        }
    }

    /// <summary>
    /// 将日志条目写入当天的日志文件。在写入前会检查是否需要切换到新一天的文件。
    /// </summary>
    /// <param name="entry">要写入的日志条目。</param>
    public void Emit(LogEntry entry, bool toJson, bool indented)
    {
        lock (_lock)
        {
            // 检查是否需要轮转
            CheckForRotation(entry.Timestamp);

            // 如果 writer 因为某些原因没创建成功，就放弃写入
            if (_writer == null)
            {
                var log_err_msg = "Log Writer FAILED: _writer is null".Color(ConsoleColor.DarkMagenta);
                Console.WriteLine(log_err_msg);
                return;
            }

            string formattedMessage = FileFormatter.Format(entry, toJson, indented);
            
            _writer.WriteLine(formattedMessage);

            // 分级刷新策略
            if (entry.Level >= LogLevel.Error)
            {
                // 检查文件存在性
                EnsureFileExists();
                _writer.Flush();
            }
        }
    }
    
    /// <summary>
    /// 检查目标文件是否还存在
    /// </summary>
    private void EnsureFileExists()
    {
        // 如果当前没有 writer，或者文件被删了
        if (_writer == null || !File.Exists(_currentLogFilePath))
        {
            // 和 CheckForRotation 相同的逻辑
            CloseCurrentWriter();
            OpenFile(DateTime.Now);
            if (_writer != null && _maxFile > 0)
            {
                CleanupOldFiles();
            }
        }
    }
    
    /// <summary>
    /// 检查是否需要根据日期进行文件轮转。
    /// 当第一次写入或日志条目的日期与当前文件日期不一致时触发。
    /// </summary>
    /// <param name="timestamp">当前日志条目的时间戳。</param>
    private void CheckForRotation(DateTime timestamp)
    {
        // 如果是第一次写入，或者当前日志的日期与记录的文件日期不符（新的一天开始了）
        if (_writer == null || timestamp.Date != _currentFileDate)
        {
            CloseCurrentWriter();

            // 尝试打开新一天的文件
            OpenFile(timestamp.Date);

            // 只有在新文件成功打开后，才清理旧文件
            if (_writer != null && _maxFile > 0)
            {
                CleanupOldFiles();
            }
        }
    }
    
    /// <summary>
    /// 根据给定的日期打开一个新的日志文件。
    /// </summary>
    /// <param name="date">要创建的日志文件的日期。</param>
    private void OpenFile(DateTime date)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileName = Path.GetFileNameWithoutExtension(_logFilePath);
            var extension = Path.GetExtension(_logFilePath);
            var dateString = date.ToString("yyyy-MM-dd");
            var currentFilePath = Path.Combine(directory ?? "", $"{fileName}-{dateString}{extension}");

            _writer = new StreamWriter(currentFilePath, append: true, Encoding.UTF8, bufferSize: 2048)
            {
                AutoFlush = false
            };

            // 更新当前文件的日期
            _currentFileDate = date;
            
            _currentLogFilePath = currentFilePath;
        }
        catch (Exception ex)
        {
            var logErrMsg = $"[LunariumLogger.InternalError] Failed to open timed log file: {ex.Message}".Color(ConsoleColor.DarkRed);
            Console.WriteLine(logErrMsg);
            _writer = null;
        }
    }

    /// <summary>
    /// 清理旧的日志文件。如果按天生成的日志文件数量超过了 `_maxFile` 的限制，则会删除最旧的文件。
    /// </summary>
    private void CleanupOldFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            var fileName = Path.GetFileNameWithoutExtension(_logFilePath);
            var extension = Path.GetExtension(_logFilePath);
            
            // 查找所有符合命名规则的日志文件, e.g., "app-2023-10-27.log"
            var pattern = new Regex($@"^{Regex.Escape(fileName)}-\d{{4}}-\d{{2}}-\d{{2}}{Regex.Escape(extension)}$");

            var files = Directory.GetFiles(directory, $"{fileName}-*{extension}")
                                .Where(f => pattern.IsMatch(Path.GetFileName(f)))
                                .Select(f => new FileInfo(f))
                                .OrderByDescending(f => f.Name)
                                .ToList();
            
            // 如果文件数量超过限制，删除最旧的
            if (files.Count > _maxFile)
            {
                var filesToDelete = files.Skip(_maxFile);
                foreach (var file in filesToDelete)
                {
                    file.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            var logErrMsg = $"[LunariumLogger.InternalError] Failed to clean up old timed log files: {ex.Message}".Color(ConsoleColor.DarkRed);
            Console.WriteLine(logErrMsg);
        }
    }

    /// <summary>
    /// 安全地关闭并释放当前的 StreamWriter。
    /// </summary>
    private void CloseCurrentWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    /// <summary>
    /// 释放此 Sink 持有的所有资源，主要是确保文件写入流被正确关闭。
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            // 停止并释放定时器
            _flushTimer?.Change(Timeout.Infinite, 0);
            _flushTimer?.Dispose();

            // 关闭文件流
            CloseCurrentWriter();

            // 标记为已释放
            _disposed = true;
        }
    }
}