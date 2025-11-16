// NuGet 包依赖
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Discord.Interactions;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Utils;
using LunariumBot.Net.SearchBot.Model;
using Polly;

namespace LunariumBot.Net.SearchBot.AppInteractionService;

public class AuthUtils
{
    private readonly ConfigLoader _configLoader;
    private readonly ILogger _logger;
    private readonly HashSet<string> _scanCategories;
    private readonly HashSet<ulong> _allScanChannelIds;
    public AuthUtils(ILogger logger, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("CommandAuth");
        _configLoader = configLoader;
        _scanCategories = _configLoader.Config.GuildsConfig.ScanChannelIDS.Keys.ToHashSet();
        _allScanChannelIds = _configLoader.Config.GuildsConfig.ScanChannelIDS.Values
            .SelectMany(ids => ids)
            .ToHashSet();
    }

    public async Task<AuthorizationResult> Authorization(AuthorizationConditions requiredConditions, IInteractionContext context)
    {
        AuthorizationConditions actualConditions = AuthorizationConditions.None;
        // --- 是否在配置的服务器内 ---
        if (InGuild(context.Guild.Id))
        {
            actualConditions |= AuthorizationConditions.IsInAuthorizedGuild;
            _logger.Info("用户 {Username}(UserId) 的交互 {Context} 鉴权未通过: 不在授权的服务器内{}",
                context.User.Username, context.User.Id, context.Interaction)
        }
        // --- 是否具有任一管理员身份 ---
        var guildUser = context.User as IGuildUser;
        if (guildUser is not null && IsAdmin(guildUser.RoleIds))
        {
            actualConditions |= AuthorizationConditions.HasRequiredPermission;
        }
        if (context.Channel is IThreadChannel threadChannel && threadChannel.CategoryId is not null && InPermittedChannel(threadChannel.CategoryId.Value))
        {
            actualConditions |= AuthorizationConditions.IsInPermittedChannel;
        }

        var result = new AuthorizationResult()
        {
            Status = Evaluate(requiredConditions, actualConditions),
            ActualConditions = actualConditions
        };

        return result;
    }

    public static MessageComponent GetFailedReason(AuthorizationResult authorizationResult)
    {
        var component = new ComponentBuilderV2();
        switch (authorizationResult.Status)
        {
            case AuthorizationStatus.NotAuthorizedGuild:
                component.WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("## ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互必须位于 BOT 已部署的服务器内使用", 1001)
                    );
                break;
            case AuthorizationStatus.PermissionDenied:
                component.WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("## ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互需要管理组权限", 1001)
                    );
                break;
            case AuthorizationStatus.NotPermittedChannel:
                component.WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("## ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互必须位于可搜索的论坛频道内使用", 1001)
                    );
                break;
        }
        return component.Build();
    }

    private bool IsAdmin(IEnumerable<ulong> userRoleIds)
    {
        return userRoleIds.Any(_configLoader.Config.AuthConfig.AdminRoleIds.Contains);
    }

    private bool InGuild(ulong guildId)
    {
        return _configLoader.Config.GuildsConfig.DeployGuildIds.Contains(guildId);
    }

    private bool InPermittedChannel(ulong guildId)
    {
        return _configLoader.Config.GuildsConfig.DeployGuildIds.Contains(guildId);
    }

    private AuthorizationStatus Evaluate(
        AuthorizationConditions requiredConditions,
        AuthorizationConditions actualConditions)
    {
        // --- 检查是否完全成功 ---
        // 使用位与操作检查 actualConditions 是否 包含 requiredConditions 的所有位。
        if ((actualConditions & requiredConditions) == requiredConditions)
        {
            return AuthorizationStatus.Success;
        }

        // --- 按优先级检查失败原因 ---
        // 这里的 if 语句顺序至关重要, 负责实现状态原因优先级

        // 优先级 1: 检查服务器授权
        // 条件：必需在授权 Guild 中, 但实际不在
        if (requiredConditions.HasFlag(AuthorizationConditions.IsInAuthorizedGuild) &&
            !actualConditions.HasFlag(AuthorizationConditions.IsInAuthorizedGuild))
        {
            return AuthorizationStatus.NotAuthorizedGuild;
        }

        // 优先级 2: 检查用户权限
        // 条件：必需特定权限, 但实际并未拥有
        if (requiredConditions.HasFlag(AuthorizationConditions.HasRequiredPermission) &&
            !actualConditions.HasFlag(AuthorizationConditions.HasRequiredPermission))
        {
            return AuthorizationStatus.PermissionDenied;
        }

        // 优先级 3: 检查频道限制
        // 条件：必需在允许的频道, 但实际不在
        if (requiredConditions.HasFlag(AuthorizationConditions.IsInPermittedChannel) &&
            !actualConditions.HasFlag(AuthorizationConditions.IsInPermittedChannel))
        {
            return AuthorizationStatus.NotPermittedChannel;
        }

        // --- 最终的保底返回 ---
        // 如果成功检查失败了, 但上面的所有具体失败检查都没匹配上
        // 这说明可能存在一个未处理的条件组合, 返回一个通用的内部错误
        return AuthorizationStatus.InternalError;
    }
}