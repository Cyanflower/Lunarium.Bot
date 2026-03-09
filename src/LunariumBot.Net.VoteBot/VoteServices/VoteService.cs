
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using LunariumBot.Net.Logger;
using LunariumBot.Net.VoteBot.Utils;

namespace LunariumBot.Net.VoteBot.VoteServices;

public class VoteService
{
    private readonly ConcurrentDictionary<ulong, SubmitData> _submits = new();
    private readonly ConcurrentDictionary<ulong, VoteData> _votes = new();
    private readonly HashSet<ulong> _activeUsers = new();
    private readonly HashSet<ulong> _submitRequireRole = new([
        1456020166659018845, // 测试用
        1336817752844796016, // 创作者
        1375746524574650418, // 破限组
        1377171135229329508  // 社区志愿者
        ]);

    private readonly HashSet<ulong> _voteRequireRole = new([
        1336817752844796016, // 创作者
        1375746524574650418, // 破限组
        1376078089024573570, // 鉴赏家
        1419687446152745071, // 枫叶
        1377171135229329508  // 社区志愿者
        ]);
        
    private ILogger _logger;
    // private string _basePath = AppContext.BaseDirectory;
    private string _basePath = Directory.GetCurrentDirectory();
    private string _submitsDataPath = Path.Combine("Data", "SubmitData.json");
    private string __activeUsersDataPath = Path.Combine("Data", "ActiveUsersData.json");
    private string __votesDataPath = Path.Combine("Data", "VotesData.json");
    public VoteService(ILogger logger)
    {
        _logger = logger.ForContext("VoteService");
        LoadAllData();
    }

    private class ActiveUserImportDto
    {
        [JsonPropertyName("author_id")]
        public ulong AuthorId { get; set; }
    }

    public bool IsActive(ulong userId)
    {
        if (_activeUsers.Contains(userId)) return true;
        else return false;
    }

    public bool HasSubmitRequiredRole(IReadOnlyCollection<ulong> userRoleIds)
    {
        if (userRoleIds.Any(_submitRequireRole.Contains)) return true;
        else return false;
    }

    public bool HasVoteRequiredRole(IReadOnlyCollection<ulong> userRoleIds)
    {
        if (userRoleIds.Any(_voteRequireRole.Contains)) return true;
        else return false;
    }
    public void Submit(ulong userId, SubmitData submitModel)
    {
        try
        {
            if (_submits.TryGetValue(key: userId, out var existData))
            {
                existData.Submit(submitModel);
            }
            else
            {
                _submits.TryAdd(userId, submitModel);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public SubmitData? GetSubmit(ulong userId)
    {
        try
        {
            if (_submits.TryGetValue(key: userId, out var existData))
            {
                return existData;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public SubmitData? CancelSubmit(ulong userId)
    {
        try
        {
            _submits.TryGetValue(key: userId, out var data);
            data?.Cancel();
            return data ?? null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public bool Vote(ulong userId, ulong candidate, Choice choice)
    {
        try
        {
            if (_votes.TryGetValue(userId, out var voteData))
            {
                switch (choice)
                {
                    case Choice.ResourcesAdmin:
                        if (voteData.ResourcesAdmin.Count < 8)
                        {
                            voteData.ResourcesAdmin.Add(candidate);
                            return true;
                        }
                        else return false;
                    case Choice.ChatsAdmin:
                        if (voteData.ChatsAdmin.Count < 3)
                        {
                            voteData.ChatsAdmin.Add(candidate);
                            return true;
                        }
                        else return false;
                    case Choice.Press:
                        if (voteData.Press.Count < 2)
                        {
                            voteData.Press.Add(candidate);
                            return true;
                        }
                        else return false;
                    case Choice.CompetitionAdmin:
                        if (voteData.CompetitionAdmin.Count < 3)
                        {
                            voteData.CompetitionAdmin.Add(candidate);
                            return true;
                        }
                        else return false;
                }
                return false;
            }
            else
            {
                VoteData newVoteData = new();
                
                switch (choice)
                {
                    case Choice.ResourcesAdmin:
                        newVoteData.ResourcesAdmin.Add(candidate);
                        break;
                    case Choice.ChatsAdmin:
                        newVoteData.ChatsAdmin.Add(candidate);
                        break;
                    case Choice.Press:
                        newVoteData.Press.Add(candidate);
                        break;
                    case Choice.CompetitionAdmin:
                        newVoteData.CompetitionAdmin.Add(candidate);
                        break;
                }
                _votes.TryAdd(userId, newVoteData);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }
    
    public void DeleteVote(ulong userId, ulong candidate, Choice choice)
    {
        try
        {
            if (_votes.TryGetValue(userId, out var voteData))
            {
                switch (choice)
                {
                    case Choice.ResourcesAdmin:
                        if (voteData.ResourcesAdmin.Contains(candidate))
                        {
                            voteData.ResourcesAdmin.Remove(candidate);
                        }
                        return;
                    case Choice.ChatsAdmin:
                        if (voteData.ChatsAdmin.Contains(candidate))
                        {
                            voteData.ChatsAdmin.Remove(candidate);
                        }
                        return;
                    case Choice.Press:
                        if (voteData.Press.Contains(candidate))
                        {
                            voteData.Press.Remove(candidate);
                        }
                        return;
                    case Choice.CompetitionAdmin:
                        if (voteData.CompetitionAdmin.Contains(candidate))
                        {
                            voteData.CompetitionAdmin.Remove(candidate);
                        }
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }

    public void LoadAllData()
    {
        try
        {
            _logger.Info("开始加载数据...");

            // 1. 加载 Active Users (白名单)
            // 拼接完整路径
            var activeUsersPath = Path.Combine(_basePath, __activeUsersDataPath);
            if (File.Exists(activeUsersPath))
            {
                var json = File.ReadAllText(activeUsersPath);
                // 使用特定的 DTO 解析 [{"author_id": 123}] 格式
                var activeUsersList = JsonSerializer.Deserialize<List<ActiveUserImportDto>>(json, JsonSerializerConfig.Options);
                
                if (activeUsersList != null)
                {
                    _activeUsers.Clear(); // 清空现有以防重复加载
                    foreach (var user in activeUsersList)
                    {
                        _activeUsers.Add(user.AuthorId);
                    }
                    _logger.Info($"已加载活跃用户名单: {_activeUsers.Count} 条");
                }
            }
            else
            {
                _logger.Warning($"活跃用户数据文件不存在: {activeUsersPath}, 将跳过加载。");
            }

            // 2. 加载 Submits (报名数据)
            var submitsPath = Path.Combine(_basePath, _submitsDataPath);
            if (File.Exists(submitsPath))
            {
                var json = File.ReadAllText(submitsPath);
                // 先反序列化为普通 Dictionary
                var dict = JsonSerializer.Deserialize<Dictionary<ulong, SubmitData>>(json, JsonSerializerConfig.Options);
                
                if (dict != null)
                {
                    _submits.Clear();
                    foreach (var kvp in dict)
                    {
                        _submits.TryAdd(kvp.Key, kvp.Value);
                    }
                    _logger.Info($"已加载报名数据: {_submits.Count} 条");
                }
            }
            else
            {
                _logger.Warning($"报名数据文件不存在: {submitsPath}, 将创建新数据。");
            }

            // 3. 加载 Votes (投票数据)
            var votesPath = Path.Combine(_basePath, __votesDataPath);
            if (File.Exists(votesPath))
            {
                var json = File.ReadAllText(votesPath);
                var dict = JsonSerializer.Deserialize<Dictionary<ulong, VoteData>>(json, JsonSerializerConfig.Options);

                if (dict != null)
                {
                    _votes.Clear();
                    foreach (var kvp in dict)
                    {
                        _votes.TryAdd(kvp.Key, kvp.Value);
                    }
                    _logger.Info($"已加载投票数据: {_votes.Count} 条");
                }
            }
            else
            {
                _logger.Warning($"投票数据文件不存在: {votesPath}, 将创建新数据。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载数据时发生严重错误");
            // 这里不 throw，允许 Bot 在数据损坏或部分加载的情况下启动，
            // 但建议实际运营中如果是格式错误应该报警
        }
    }

    public void SaveSubmits()
    {
        try
        {
            var fullPath = Path.Combine(_basePath, _submitsDataPath);
            EnsureDirectoryExists(fullPath);

            // 序列化时使用缩进选项，方便人工查阅和调试
            var json = JsonSerializer.Serialize(_submits, JsonSerializerConfig.OptionsWithIndented);
            
            // 直接写入文件 (原子写入在简单场景下可忽略，直接覆盖)
            File.WriteAllText(fullPath, json);
            
            _logger.Info($"已保存报名数据: {_submits.Count} 条"); 
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存报名数据失败");
        }
    }

    public void SaveVote()
    {
        try
        {
            var fullPath = Path.Combine(_basePath, __votesDataPath);
            EnsureDirectoryExists(fullPath);

            var json = JsonSerializer.Serialize(_votes, JsonSerializerConfig.OptionsWithIndented);
            File.WriteAllText(fullPath, json);

            _logger.Info($"已保存投票数据: {_votes.Count} 条");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存投票数据失败");
        }
    }
    
    /// <summary>
    /// 确保文件所在的目录存在
    /// </summary>
    private void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}