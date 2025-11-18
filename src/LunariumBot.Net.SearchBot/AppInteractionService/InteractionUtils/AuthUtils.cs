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
        // 1. 【准备阶段】提取上下文关键信息 (用于日志和逻辑)
        //    这样做的好处是：如果 context 是 null 或者某些属性为空，我们在这里统一处理，
        //    后续逻辑和日志都不用再担心空引用。
        var interactionName = GetInteractionIdentifier(context);
        var userId = context.User.Id;
        var userName = context.User.Username;
        var guildId = context.Guild?.Id;
        var guildName = context.Guild?.Name ?? "DM (私聊)";
        var channelId = context.Channel?.Id;
        var channelName = context.Channel?.Name ?? "Unknow Channel";

        // 2. 【计算阶段】计算当前满足的所有条件
        AuthorizationConditions actualConditions = AuthorizationConditions.None;

        // check: 是否在授权服务器
        if (guildId.HasValue && InGuild(guildId.Value))
        {
            actualConditions |= AuthorizationConditions.IsInAuthorizedGuild;
        }

        // check: 是否有管理员权限
        // 注意：只有在 Guild 中才检查 Role，私聊中 User 不是 IGuildUser
        if (context.User is IGuildUser guildUser && IsAdmin(guildUser.RoleIds))
        {
            actualConditions |= AuthorizationConditions.HasRequiredPermission;
        }

        // check: 是否在允许的频道 (使用之前讨论的修正逻辑)
        // 这里假设 context.Channel 包含了足够的信息
        if (context.Channel is not null && IsChannelPermitted(context.Channel)) 
        {
            actualConditions |= AuthorizationConditions.IsInPermittedChannel;
        }

        // 3. 【评估阶段】对比 需求条件 vs 实际条件
        var status = Evaluate(requiredConditions, actualConditions);
        var result = new AuthorizationResult()
        {
            Status = status,
            ActualConditions = actualConditions
        };

        // 4. 【日志阶段】根据结果进行详细记录
        // 使用结构化日志模板，方便后续在日志系统中检索
        if (status == AuthorizationStatus.Success)
        {
            // 成功日志：通常 Info 级别即可，记录 谁、在哪、做了什么
            _logger.Info(
                "鉴权通过 ✅ | 用户: {UserName}({UserId}) | 交互: {Interaction} | 位置: {GuildName}/{ChannelName}",
                userName, userId, interactionName, guildName, channelName);
        }
        else
        {
            // 失败日志：使用 Warning 级别，重点记录 缺什么条件 (Missing)
            // 计算缺失的条件以便打印
            var missingConditions = requiredConditions & ~actualConditions;

            _logger.Warning(
                "鉴权拒绝 ⛔️ | 原因: {Reason} | 用户: {UserName}({UserId}) | 交互: {Interaction} | " +
                "位置: {GuildName}({GuildId}) - {ChannelName}({ChannelId}) | " +
                "需求: {Required} | 实际: {Actual} | 缺失: {Missing}",
                // 参数列表
                status.ToString(),              // Reason
                userName, userId,               // User
                interactionName,                // Interaction
                guildName, guildId, channelName, channelId, // Location
                requiredConditions,             // Required
                actualConditions,               // Actual
                missingConditions               // Missing (最关键的调试信息)
            );
        }

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
    private bool IsChannelPermitted(IMessageChannel channel)
    {
        ulong? ParentChannelId = channel switch
        {
            SocketThreadChannel socketThreadChannel => socketThreadChannel.ParentChannel is not null ? socketThreadChannel.ParentChannel.Id : null,
            RestThreadChannel restThreadChannel => restThreadChannel.ParentChannelId,
            SocketTextChannel socketTextChannel => socketTextChannel.Id,
            RestTextChannel restTextChannel => restTextChannel.Id,
            _ => null
        };
        // 逻辑修正：直接检查频道ID是否在允许列表中
        // 如果是帖子(Thread)，通常需要检查其父频道ID
        if (channel is IThreadChannel threadChannel)
        {
            // 检查帖子的父频道(论坛频道)是否在白名单
            return _allScanChannelIds.Contains(threadChannel.CategoryId);
            // 或者如果你有 threadChannel.CategoryId 的逻辑，也可以加在这里
        }
        
        // 普通频道直接检查ID
        return _allScanChannelIds.Contains(channel.Id);
    }
    /*
    * =====================================================================================
    *  [技术债警告 - Technical Debt Warning]
    * =====================================================================================
    *  当前方法: InPermittedChannel
    *  问题:      实现逻辑完全错误，是一个临时的占位符实现。
    *  上下文:    编写此代码时，ConfigLoader 部分对于 "ScanChannelIDS" (可扫描频道/分类ID)
    *             的加载和解析逻辑尚未完成。为了让 AuthUtils 类能够先行编译通过，
    *             临时复制了 InGuild() 方法的逻辑。
    *
    *  预期功能 (Intended Functionality):
    *  ---------------------------------
    *  此方法的正确功能应该是接收一个 `ulong` 类型的【频道ID】或【分类ID】，然后判断
    *  该ID是否存在于从配置中加载的 `_allScanChannelIds` 或 `_scanCategories` 集合中。
    *  它是实现 `AuthorizationConditions.IsInPermittedChannel` 鉴权的关键。
    *
    *  修复步骤 (Action Plan):
    *  -----------------------
    *  1.  在 `ConfigLoader` 中，完成对配置文件中 `GuildsConfig.ScanChannelIDS` 节点的
    *      解析，并能提供一个扁平化的、包含所有可扫描频道ID的集合。
    *  2.  确保 `AuthUtils` 的构造函数能够正确地从 `ConfigLoader` 获取这个ID集合，
    *      并初始化 `_allScanChannelIds` 字段。
    *  3.  重写此方法的主体逻辑，改为使用 `_allScanChannelIds.Contains(channelOrCategoryId)`。
    *  4.  【重要】修改此方法的签名，将参数 `ulong guildId` 改为更具描述性的
    *      `ulong channelOrCategoryId`。
    *  5.  更新 `Authorization` 方法中对本方法的调用，确保传递的是正确的ID。
    * =====================================================================================
    */
    private bool InPermittedChannel(ulong guildId)
    {
        return _configLoader.Config.GuildsConfig.DeployGuildIds.Contains(guildId);
    }

    private string GetInteractionIdentifier(IInteractionContext context)
    {
        return context.Interaction switch
        {
            // 1. 斜线命令 (Slash Command)
            ISlashCommandInteraction slashCommand => $"/ {slashCommand.Data.Name}",
            
            // 2. 组件交互 (按钮、下拉菜单等)
            IComponentInteraction component => $"[Component] {component.Data.CustomId}",
            
            // 3. 模态弹窗提交 (Modal Submit)
            IModalInteraction modal => $"[Modal] {modal.Data.CustomId}",
            
            // 4. 用户上下文命令 (右键用户 -> Apps)
            IUserCommandInteraction userCommand => $"[UserCommand] {userCommand.Data.Name}",
            
            // 5. 消息上下文命令 (右键消息 -> Apps)
            IMessageCommandInteraction messageCommand => $"[MessageCommand] {messageCommand.Data.Name}",
            
            // 默认情况
            _ => "Unknown Interaction"
        };
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