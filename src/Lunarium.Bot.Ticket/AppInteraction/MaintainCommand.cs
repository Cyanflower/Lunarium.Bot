using Discord.Interactions;
using Discord;

using Lunarium.Logging;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Bot.Ticket.InteractionSession;
using Lunarium.Bot.Framework;
using Lunarium.Common.UID;
using Lunarium.Common;

namespace Lunarium.Bot.Ticket.AppInteraction;


[Group("维护", "维护配置命令")]
public sealed class MaintainCommand : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly SessionManager _sessionManager;

    public MaintainCommand(ILogger logger, ConfigLoader<TicketConfig> configLoader, SessionManager sessionManager)
    {
        _logger = logger.ForContext("AppInteraction.MaintainCommand");
        _configLoader = configLoader;
        _sessionManager = sessionManager;
    }

    [SlashCommand("编辑配置", "添加或修改BOT在本服务器的配置")]
    public async Task EditConfig()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            await DeferAsync();

            track.Info("收到编辑配置请求: GuildId={GuildId}, UserId={UserId}", Context.Guild?.Id, Context.User.Id);

            if (Context.Guild is null)
            {
                track.Warning("编辑配置失败: 无法解析服务器信息, UserId={UserId}", Context.User.Id);
                await FollowupAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"));
                return;
            }

            MessageComponent? validationResult;

            ConfigEditorContext context;
            if (_configLoader.Value.DeployGuildConfig.TryGetValue(Context.Guild.Id, out var config))
            {
                track.Info("服务器 {GuildId} 已存在配置, 进入修改模式", Context.Guild.Id);

                validationResult = await RuleEngine.ValidateAsync(
                    Context,
                    new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
                    new BaseRuleSet.RequireAdminRole(_configLoader.Value.DeployGuildConfig[Context.Guild.Id].AllowManageTicketAdminRoleIds.ToHashSet()));
                
                if (validationResult is not null)
                {
                    track.Warning("编辑配置权限校验未通过: GuildId={GuildId}, UserId={UserId}", Context.Guild.Id, Context.User.Id);
                    await FollowupAsync(components: validationResult);
                    return;
                }

                context = new ConfigEditorContext(config.GetDeepCopy(), Context.Guild, false);
            }
            else
            {
                track.Info("服务器 {GuildId} 暂无配置, 进入新建模式", Context.Guild.Id);

                validationResult = await RuleEngine.ValidateAsync(
                    Context,
                    new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()));
                
                if (validationResult is not null)
                {
                    track.Warning("创建配置权限校验未通过: GuildId={GuildId}, UserId={UserId}", Context.Guild.Id, Context.User.Id);
                    await FollowupAsync(components: validationResult);
                    return;
                }

                context = new ConfigEditorContext(new GuildConfig(), Context.Guild, false);
            }

            var session = _sessionManager.Register<ConfigEditorSession>(context, Context.User.Id.ToString());
            track.Info("配置编辑会话已创建: GuildId={GuildId}, UserId={UserId}, SessionId={SessionId}", Context.Guild.Id, Context.User.Id, session.SessionId);

            await FollowupAsync(components: session.Render());
        }
        catch (Exception ex)
        {
            track.Error(ex, "处理编辑配置命令时发生异常: GuildId={GuildId}, UserId={UserId}", Context.Guild?.Id, Context.User.Id);
        }
    }
}

