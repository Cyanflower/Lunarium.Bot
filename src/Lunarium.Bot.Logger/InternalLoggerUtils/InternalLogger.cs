using System.Text;

namespace Lunarium.Bot.Logger.InternalLoggerUtils;

internal static class InternalLogger
{
    internal static readonly object _fileLock = new object();

    #region 公开方法

    internal static void Error(string message) => ErrorHandler(message, null);
    internal static void Error(Exception exception) => ErrorHandler("", exception);
    internal static void Error(Exception exception, string message) => ErrorHandler(message, exception);
    internal static void Error(string message, Exception exception) => ErrorHandler(message, exception);

    #endregion

    #region 内部方法实现

    private static void ErrorHandler(string message, Exception? exception = null)
    {
        var now = DateTimeOffset.Now;
        string formatted;
        if (exception is null)
        {
            formatted = $"[{now:yyyy-MM-dd HH:mm:ss.fff zzz}] [LunariumLogger.InternalError] {message}";
        }
        else
        {
            formatted = $"[{now:yyyy-MM-dd HH:mm:ss.fff zzz}] [LunariumLogger.InternalError] {message}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
        }

        ConsoleHandler(formatted);
        FileHandler(formatted);
    }

    private static void ConsoleHandler(string errorMsg)
    {
        lock (_fileLock)
        {
            var originalBg = Console.BackgroundColor;
            var originalFg = Console.ForegroundColor;

            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine(errorMsg); // 写入标准错误流更合适
            // Console.ResetColor();

            // 恢复原始颜色，避免影响程序的其他部分
            Console.BackgroundColor = originalBg;
            Console.ForegroundColor = originalFg;
        }
    }

    private static void FileHandler(string errorMsg)
    {
        try
        {
            // 构造文件名: LunariumLogger-internal-20251101.log
            string dateStamp = DateTimeOffset.Now.ToString("yyyyMMdd");
            string fileName = $"LunariumLogger-internal-{dateStamp}.log";

            // 获取程序基目录
            string baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(baseDirectory))
            {
                // 如果基目录为空, 则无法继续, 直接放弃
                return;
            }

            string filePath = Path.Combine(baseDirectory, fileName);

            // 加锁，确保线程安全
            lock (_fileLock)
            {
                // File.AppendAllText 会自动创建、打开、写入并关闭文件，非常方便
                File.AppendAllText(filePath, errorMsg, Encoding.UTF8);
            }
        }
        catch
        {
            // 写入文件失败则放弃, 不抛出任何异常
            // 这是 SelfLog 的核心原则: 不能因为记录内部错误而导致程序崩溃
        }
    }

    #endregion
}