using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Lunarium.Bot.Logger;
using Lunarium.Bot.VoteBot.AuthUtils;
using Lunarium.Bot.VoteBot.InteractionsCache;
using Lunarium.Bot.VoteBot.Listener;
using Lunarium.Bot.VoteBot.Utils;
using Lunarium.Bot.VoteBot.VoteServices;
using Polly;
using Polly.Retry;

namespace Lunarium.Bot.VoteBot.AppInteractionService;


public class AdminInteractionService : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ILogger _auditLogger;
    private readonly ConfigLoader _configLoader;
    private readonly AuthUtil _authUtil;
    private readonly InteractionsCacheManager _interactionsCacheManager;
    private VoteService _voteService;
    private readonly ComponentUtils _componentUtils;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly ListenerService _listenerService;
    public AdminInteractionService(
        ILogger logger,
        ConfigLoader configLoader,
        AuthUtil authUtil,
        InteractionsCacheManager interactionsCacheManager,
        VoteService voteService,
        ListenerService listenerService
    )
    {
        _logger = logger.ForContext("AdminInteractionService");
        _auditLogger = logger.ForContext("CommandAuth");
        _authUtil = authUtil;
        _configLoader = configLoader;
        _interactionsCacheManager = interactionsCacheManager;
        _voteService = voteService;
        _componentUtils = new ComponentUtils(_logger);
        _httpRetryPolicy = Util.HttpRetryPolicyConfig(_logger);
        _listenerService = listenerService;
    }

    #region 斜杠命令

    [SlashCommand("发布投票入口面板", "发布投票入口面板")]
    public async Task PublishVotePanelAsync()
    {
        try
        {
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.HasRequiredPermission;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }

            var message = await Context.Channel.SendMessageAsync(components: Util.GetSubmitMessageComponent());
            _listenerService.SetMessage(message);

            var successComponent = new ComponentBuilderV2()
                .WithContainer(container => container
                    .WithTextDisplay("发布成功")
                ).Build();
            
            await RespondAsync(components: successComponent, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PublishEntryPanel Exception");
        }
    }

    [SlashCommand("结束报名", "结束报名")]
    public async Task EndEntryAsync()
    {
        try
        {
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.HasRequiredPermission;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }

            await _listenerService.MessageReceivedAsync();

            var successComponent = new ComponentBuilderV2()
                .WithContainer(container => container
                    .WithTextDisplay("结束报名成功")
                ).Build();

            await RespondAsync(components: successComponent, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PublishEntryPanel Exception");
        }
    }

    // TODO: 
    // 统计报名数据, 发布报名名单
    // 发布投票面板
    #endregion
}