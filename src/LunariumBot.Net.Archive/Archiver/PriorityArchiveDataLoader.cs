// .NET 核心库
using System.Text.Json;
using System.Text;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.Archive.Model;


namespace LunariumBot.Net.Archive.Archiver;

public class PriorityArchiveDataLoader
{
    private readonly object _lockObject = new();
    private readonly ILogger _logger;
    private readonly string _dataFilePath;

    public PriorityArchiveDataLoader(ILogger logger)
    {
        _logger = logger.ForContext("PriorityArchiveDataLoader");
        _dataFilePath = Path.Combine(AppContext.BaseDirectory, "data","priority-archive-data.json");
    }
    
    /// <summary>
    /// 将优先归档数据保存到文件
    /// </summary>
    /// <param name="priorityThreadData">要保存的数据字典</param>
    /// <returns>成功返回 true，失败返回 false</returns>
    public bool SaveData(Dictionary<ulong, PriorityThreadInfo> priorityThreadData)
    {
        _logger.Info("准备保存优先归档数据，共 {count} 条...", priorityThreadData.Count);
        lock (_lockObject)
        {
            try
            {
                // 确保目录存在，这和 LoadData 里的逻辑一样，防止在加载后、保存前目录被删除
                var directory = Path.GetDirectoryName(_dataFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Info("数据目录不存在，已自动创建: {directory}", directory);
                }

                // 配置JSON序列化选项，让输出的文件更易读
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                
                // 将字典序列化为JSON字符串
                string jsonContent = JsonSerializer.Serialize(priorityThreadData, options);
                
                // 写入文件
                File.WriteAllText(_dataFilePath, jsonContent);

                _logger.Info("✅ 优先归档数据已成功保存到文件！");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 保存优先归档数据时发生严重错误！");
                return false;
            }
        }
    }
    
    /// <summary>
    /// 从文件加载优先归档数据。如果文件或目录不存在，则会自动创建并返回一个空字典。
    /// </summary>
    /// <returns>加载到的数据字典</returns>
    public Dictionary<ulong, PriorityThreadInfo> LoadData()
    {
        _logger.Info("正在加载优先归档数据...");
        lock (_lockObject)
        {
            try
            {
                // 1. 检查并创建目录
                // Path.GetDirectoryName 可以安全地从完整文件路径中提取出目录部分
                var directory = Path.GetDirectoryName(_dataFilePath);
                // 必须检查 directory 是否为 null，以防路径格式有问题
                if (directory != null && !Directory.Exists(directory))
                {
                    _logger.Info("数据目录不存在，正在自动创建: {directory}", directory);
                    Directory.CreateDirectory(directory);
                }

                // 2. 检查文件是否存在
                if (!File.Exists(_dataFilePath))
                {
                    _logger.Info("优先归档数据文件不存在，将返回一个新的空数据集合。");
                    // 文件不存在是正常情况（比如第一次启动），直接返回一个空的字典
                    return new Dictionary<ulong, PriorityThreadInfo>();
                }

                // 3. 读取并反序列化文件内容
                string jsonContent = File.ReadAllText(_dataFilePath);

                // 4. 处理空文件的情况
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    _logger.Warning("优先归档数据文件是空的，将返回一个新的空数据集合。");
                    return new Dictionary<ulong, PriorityThreadInfo>();
                }

                // 反序列化JSON，如果结果是null（比如文件内容是"null"），也返回一个空字典
                var data = JsonSerializer.Deserialize<Dictionary<ulong, PriorityThreadInfo>>(jsonContent)
                           ?? new Dictionary<ulong, PriorityThreadInfo>();

                _logger.Info("✅ 成功加载 {count} 条优先归档数据！", data.Count);
                return data;
            }
            catch (JsonException jsonEx)
            {
                // JSON格式错误是比较常见的，单独捕获并给出明确错误信息
                _logger.Error(jsonEx, "加载优先归档数据失败: JSON 格式错误！");
                // 向上抛出，让调用者知道加载失败了
                throw new InvalidOperationException($"加载优先归档数据失败: JSON格式错误 - {jsonEx.Message}", jsonEx);
            }
            catch (Exception ex)
            {
                // 捕获其他所有可能的异常（如IO权限问题）
                _logger.Error(ex, "加载优先归档数据时发生未知错误！");
                throw new InvalidOperationException($"加载优先归档数据失败: {ex.Message}", ex);
            }
        }
    }
}
