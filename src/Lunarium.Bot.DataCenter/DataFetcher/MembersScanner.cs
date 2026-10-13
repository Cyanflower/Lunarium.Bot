// .NET 核心库
using System.Collections.Concurrent;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Rest;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using Lunarium.Bot.Logger;
using Lunarium.Bot.Extensions;
using Lunarium.Bot.Extensions.DiscordNetExtension;

// 项目内命名空间
using Lunarium.Bot.DataCenter.DataProcess;
using Lunarium.Bot.DataCenter.Models;
using Lunarium.Bot.DataCenter.Utils;
using Lunarium.Bot.DataCenter.AppInteractionService;
using Lunarium.Bot.Scheduler;

namespace Lunarium.Bot.DataCenter.DataFetcher;

public class MembersScanner
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly DataProcessService _dataProcessService;
    private readonly RateLimiter _apiRequestRateLimiter;
    private readonly RequestOptions _requestOptions;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly AsyncRetryPolicy _dbRetryPolicy;
    private Dictionary<ulong, RestRole> _rolesCache = new();
    private HashSet<ulong> DeployGuildIds => _configLoader.Config.DeployConfig.DeployGuildIds;
    private HashSet<ulong> ExcludeGuildIds => _configLoader.Config.DeployConfig.ExcludeGuildIds;
    public MembersScanner(
        ILogger logger,
        ConfigLoader configLoader,
        DiscordSocketClient discordSocketClient,
        DataProcessService dataProcessService,
        RateLimiter rateLimiter,
        RequestOptions requestOptions,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy,
        [FromKeyedServices("DbPolicy")] AsyncRetryPolicy dbRetryPolicy
    )
    {
        _logger = logger.ForContext("MembersScanner");
        _configLoader = configLoader;
        _client = discordSocketClient;
        _dataProcessService = dataProcessService;
        _apiRequestRateLimiter = rateLimiter;
        _requestOptions = requestOptions;
        _httpRetryPolicy = httpRetryPolicy;
        _dbRetryPolicy = dbRetryPolicy;
    }

    public async Task<string> SyncScanAsync(ScanProgressReporter? reporter = null)
    {
        var startTime = DateTimeOffset.UtcNow;
        _logger.Info("🚀 开始执行成员与身份组同步任务...");
        if (reporter is not null) await reporter.ReportStartAsync("🚀 开始执行成员与身份组同步任务...", Color.Blue);

        // 统计变量
        int scannedRoleGuilds = 0;
        int scannedMemberGuilds = 0;
        int rawMemberCount = 0;

        try
        {
            // ==================== 1. 获取身份组 ====================
            var phase1Start = DateTimeOffset.UtcNow;
            string GetElapsedTime()
            {
                var elapsed = DateTimeOffset.UtcNow - startTime;
                return $"[T+{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}]";
            }

            _logger.Info("{Time} 📥 第 1 阶段：获取身份组数据...", GetElapsedTime());
            if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 📥 第 1 阶段：获取身份组数据...");

            // 获取身份组并返回扫描的服务器数量
            scannedRoleGuilds = await GetRolesCacheAsync();
            var allRoleDatas = GetAllRoleDatas();

            _logger.Info("{Time} ✅ 获取到 {GuildCount} 个服务器的 {RoleCount} 个身份组", 
                GetElapsedTime(), scannedRoleGuilds, allRoleDatas.Count);
            if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 获取到 {allRoleDatas.Count} 个身份组 (来自 {scannedRoleGuilds} 个服务器)");

            // ==================== 2. 获取成员数据 ====================
            var phase2Start = DateTimeOffset.UtcNow;
            _logger.Info("{Time} 📥 第 2 阶段：获取成员数据...", GetElapsedTime());
            if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 📥 第 2 阶段：获取成员数据...");

            // 获取成员数据 (返回元组以便统计)
            var (allMemberDatas, guildCount, rawCount) = await GetAllMemberDatas();
            scannedMemberGuilds = guildCount;
            rawMemberCount = rawCount;

            _logger.Info("{Time} ✅ 获取到 {RawCount} 条原始记录, 合并为 {MemberCount} 个唯一成员",
                GetElapsedTime(), rawMemberCount, allMemberDatas.Count);
            if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} ✅ 获取到 {allMemberDatas.Count} 个唯一成员 (原始记录 {rawMemberCount} 条)");

            // ==================== 3. 写入数据库 ====================
            var phase3Start = DateTimeOffset.UtcNow;
            _logger.Info("{Time} 💾 第 3 阶段：写入数据库...", GetElapsedTime());
            if (reporter is not null) await reporter.ReportProgressAsync($"> {GetElapsedTime()} 💾 第 3 阶段：写入数据库...");

            await _dataProcessService.SyncMembersAndRolesAsync(allRoleDatas, allMemberDatas);
            
            var phase3Duration = DateTimeOffset.UtcNow - phase3Start;
            _logger.Info("{Time} ⏱️ 第 3 阶段完成, 耗时: {Duration:F2} 秒", GetElapsedTime(), phase3Duration.TotalSeconds);
            
            // ==================== 总结 ====================
            var endTime = DateTimeOffset.UtcNow;
            var totalDuration = endTime - startTime;

            _logger.Info("✅ 成员同步任务完成！总耗时: {TotalDuration:F2} 秒", totalDuration.TotalSeconds);

            // 构建统计报告字符串
            var statsBuilder = new StringBuilder();
            statsBuilder.AppendLine("📊 **成员与身份组同步统计报告**");
            statsBuilder.AppendLine();
            statsBuilder.AppendLine($"▶️ 开始时间: <t:{startTime.ToUnixTimeSeconds()}:f>");
            statsBuilder.AppendLine($"⏹ 完成时间: <t:{endTime.ToUnixTimeSeconds()}:f>");
            statsBuilder.AppendLine($"⏱️ 总耗时: {totalDuration.TotalSeconds:F2} 秒");
            statsBuilder.AppendLine();
            statsBuilder.AppendLine("📥 **数据统计:**");
            statsBuilder.AppendLine($"> - 扫描服务器: {Math.Max(scannedRoleGuilds, scannedMemberGuilds)} 个");
            statsBuilder.AppendLine($"> - 获取身份组: {allRoleDatas.Count} 个");
            statsBuilder.AppendLine($"> - 原始成员记录: {rawMemberCount} 条");
            statsBuilder.AppendLine($"> - 去重成员数据: {allMemberDatas.Count} 人");
            statsBuilder.AppendLine();
            statsBuilder.AppendLine("⚡ **阶段耗时:**");
            statsBuilder.AppendLine($"> - 身份组获取: {(phase2Start - phase1Start).TotalSeconds:F2} 秒");
            statsBuilder.AppendLine($"> - 成员获取: {(phase3Start - phase2Start).TotalSeconds:F2} 秒");
            statsBuilder.AppendLine($"> - 数据库写入: {phase3Duration.TotalSeconds:F2} 秒");

            return statsBuilder.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 成员与身份组同步任务失败");
            
            var errorMsg = $"❌ 任务失败\n原因: {ex.Message}";
            if (reporter is not null) await reporter.ReportProgressAsync(errorMsg, Color.Red);
            
            throw; // 抛出异常让上层捕获处理
        }
    }

    private async Task<int> GetRolesCacheAsync()
    {
        try
        {
            // 重置缓存
            _rolesCache = new();
            int scannedGuilds = 0;
            // 获取各服务器身份组信息
            foreach (var guildId in DeployGuildIds)
            {
                if (ExcludeGuildIds.Contains(guildId)) continue;
    
                var guild = _client.GetGuild(guildId);

                if (guild is null) continue;
                
                scannedGuilds++;
    
                using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
                
                var roles = await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    return await _client.GetGuildRolesAsync(guild);
                });
    
                foreach (var role in roles)
                {
                    _rolesCache[role.Id] = role;
                }
            }
            _logger.Info("📥 身份组扫描完成: 覆盖 {GuildCount} 个服务器, 获取 {RoleCount} 个身份组", 
                scannedGuilds, _rolesCache.Count);
            return scannedGuilds;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取身份组缓存时发生错误");
            throw;
        }
    }

    private async Task<(List<UserData> Data, int ScannedGuilds, int RawCount)> GetAllMemberDatas()
    {
        try
        {
            Dictionary<ulong, UserData> allMemberDatas = new();
            List<IGuildUser> allGuildUsers = new();
            int scannedGuilds = 0;

            foreach (var guildId in DeployGuildIds)
            {
                if (ExcludeGuildIds.Contains(guildId)) continue;
    
                var guild = _client.GetGuild(guildId);
    
                if (guild is null) continue;

                scannedGuilds++;

                using var token = await _apiRequestRateLimiter.AcquireWithLoggingAsync(_logger, 1);
    
                // 使用临时变量 fetchedUsers 在闭包内收集数据, 成功后再合并到主列表, 防止部分失败重试导致的数据重复累加
                var fetchedUsers = await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    var tempUsers = new List<IGuildUser>(); 
                    var pages = guild.GetUsersAsync();
    
                    await foreach (var page in pages)
                    {
                        tempUsers.AddRange(page);
                    }
                    return tempUsers;
                });

                // 只有当 ExecuteAsync 成功返回后，才添加到总表中
                if (fetchedUsers is not null)
                {
                    allGuildUsers.AddRange(fetchedUsers);
                }
            }

            _logger.Info("📥 成员原始数据获取完成: 覆盖 {GuildCount} 个服务器, 原始记录 {RawCount} 条",
                scannedGuilds, allGuildUsers.Count);

            foreach (var guildUser in allGuildUsers)
            {
                if (allMemberDatas.TryGetValue(guildUser.Id, out var userData))
                {
                    if (userData.UserUsername == "Unknown User")
                    {
                        userData.UserUsername = guildUser.Username is not null ? guildUser.Username : "Unknown User";
                    }
                    if (userData.UserNikcname is null) userData.UserNikcname = guildUser.Nickname;
                    if (userData.UserGlobalName is null) userData.UserGlobalName = guildUser.GlobalName;
                    if (userData.UserAvatarUrl is null) userData.UserAvatarUrl = guildUser.GetAvatarUrl();

                    userData.UserRoles.AddRange(guildUser.RoleIds);
                }
                else
                {
                    userData = new()
                    {
                        UserId = guildUser.Id,
                        UserUsername = guildUser.Username is not null ? guildUser.Username : "Unknown User",
                        UserNikcname = guildUser.Nickname,
                        UserGlobalName = guildUser.GlobalName,
                        UserAvatarUrl = guildUser.GetAvatarUrl(),
                        UserRoles = guildUser.RoleIds.ToList()
                    };
                }
                allMemberDatas[guildUser.Id] = userData;
            }
            
            return (allMemberDatas.Values.ToList(), scannedGuilds, allGuildUsers.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 获取成员数据时发生错误");
            throw;
        }
    }
    private List<RoleData> GetAllRoleDatas()
    {
        List<RoleData> roleDatas = new ();
        foreach ((var _, var role) in _rolesCache)
        {
            try
            {
                string roleName = "Unknown Role Name";
                if (role.Name is not null)
                {
                    roleName = role.Name;
                }
                roleDatas.Add(new RoleData()
                {
                    RoleId = role.Id,
                    RoleName = roleName,
                    RolePosition = role.Position,
                    // RoleIconUrl = role.Icon,
                    RoleIconUrl = role.GetIconUrl(),
                    RoleEmoji = role.Emoji?.ToString(),
                    RolePrimaryColor = role.Colors.PrimaryColor.RawValue,
                    RoleSecondaryColor = role.Colors.SecondaryColor?.RawValue,
                    RoleTertiaryColor = role.Colors.TertiaryColor?.RawValue,
                    IsDeleted = false,
                    DeleteAt = null
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 转换 RestRole 对象到 RoleData 对象时出错");
            }
        }
        return roleDatas;
    }
}