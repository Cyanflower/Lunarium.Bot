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
using LunariumBot.Net.Logger;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.Extensions.DiscordNetExtension;

// 项目内命名空间
using LunariumBot.Net.DataCenter.DataProcess;
using LunariumBot.Net.DataCenter.Models;
using LunariumBot.Net.DataCenter.Utils;
using LunariumBot.Net.DataCenter.AppInteractionService;
using LunariumBot.Net.Scheduler;

namespace LunariumBot.Net.DataCenter.DataFetcher;

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
    private Dictionary<ulong, RestRoleV2> _rolesCache = new();
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

    public async Task SyncScanAsync()
    {
        await GetRolesCacheAsync();

        var allRoleDatas = GetAllRoleDatas();
        var allMemberDatas = await GetAllMemberDatas();
        await _dataProcessService.SyncMembersAndRolesAsync(allRoleDatas, allMemberDatas);

    }

    private async Task GetRolesCacheAsync()
    {
        // 重置缓存
        _rolesCache = new();
        // 获取各服务器身份组信息
        foreach (var guildId in DeployGuildIds)
        {
            if (ExcludeGuildIds.Contains(guildId)) continue;

            var guild = _client.GetGuild(guildId);

            if (guild is null) continue;

            using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
            if (!apiRateLimitToken.IsAcquired)
            {
                _logger.Error("_apiRequestRateLimiter 队列已满");
                // 只记录日志
                // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
            }
            
            var roles = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return await _client.GetGuildRolesAsync(guild);
            });

            foreach (var role in roles)
            {
                _rolesCache.Add(role.Id, role);
            }
        }
    }

    private async Task<List<UserData>> GetAllMemberDatas()
    {
        try
        {
            Dictionary<ulong, UserData> allMemberDatas = new();
            List<IGuildUser> allGuildUsers = new();
            foreach (var guildId in DeployGuildIds)
            {
                if (ExcludeGuildIds.Contains(guildId)) continue;
    
                var guild = _client.GetGuild(guildId);
    
                if (guild is null) continue;
    
                using var apiRateLimitToken = await _apiRequestRateLimiter.AcquireAsync(1);
                if (!apiRateLimitToken.IsAcquired)
                {
                    _logger.Error("_apiRequestRateLimiter 队列已满");
                    // 只记录日志
                    // 越过速率控制, 直接交给 Discord.Net 的速率桶控制
                }
    
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    var pages = guild.GetUsersAsync();
    
                    await foreach (var page in pages)
                    {
                        allGuildUsers.AddRange(page);
                    }
                });
            }
            
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
                        UserUsername = guildUser.Username is not null? guildUser.Username : "Unknown User",
                        UserNikcname = guildUser.Nickname,
                        UserGlobalName = guildUser.GlobalName,
                        UserAvatarUrl = guildUser.GetAvatarUrl(),
                        UserRoles = guildUser.RoleIds.ToList()
                    };
                }
            }
            return allMemberDatas.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
            throw;
        }
    }
    private List<RoleData> GetAllRoleDatas()
    {
        return _rolesCache.Select(kv =>
        {
            string roleName = "Unknown Role Name";

            if (kv.Value.Name is not null)
            {
                roleName = kv.Value.Name;
            }

            return new RoleData()
            {
                RoleId = kv.Value.Id,
                RoleName = roleName,
                RoleIconUrl = kv.Value.Icon,
                RoleEmoji = kv.Value.Emoji?.ToString(),
                RolePrimaryColor = kv.Value.Colors?.PrimaryColor.RawValue,
                RoleSecondaryColor = kv.Value.Colors?.SecondaryColor?.RawValue,
                RoleTertiaryColor = kv.Value.Colors?.TertiaryColor?.RawValue,
                IsDeleted = false,
                DeleteAt = null
            };
        }).ToList();
    }
}