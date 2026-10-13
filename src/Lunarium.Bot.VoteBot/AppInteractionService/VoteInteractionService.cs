using Discord;
using Discord.Net;
using Discord.WebSocket;
using Discord.Interactions;
using Polly;
using Polly.Retry;
using Lunarium.Bot.Logger;
using Lunarium.Bot.VoteBot.AuthUtils;
using Lunarium.Bot.VoteBot.InteractionsCache;
using Lunarium.Bot.VoteBot.VoteServices;
using Lunarium.Bot.VoteBot.Utils;

namespace Lunarium.Bot.VoteBot.AppInteractionService;


public class VoteInteractionService : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ILogger _auditLogger;
    private readonly ConfigLoader _configLoader;
    private readonly AuthUtil _authUtil;
    private readonly InteractionsCacheManager _interactionsCacheManager;
    private VoteService _voteService;
    private readonly ComponentUtils _componentUtils;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    public VoteInteractionService(
        ILogger logger,
        ConfigLoader configLoader,
        AuthUtil authUtil,
        InteractionsCacheManager interactionsCacheManager,
        VoteService voteService
    )
    {
        _logger = logger.ForContext("VoteInteractionService");
        _auditLogger = logger.ForContext("CommandAuth");
        _authUtil = authUtil;
        _configLoader = configLoader;
        _interactionsCacheManager = interactionsCacheManager;
        _voteService = voteService;
        _componentUtils = new ComponentUtils(_logger);
        _httpRetryPolicy = Util.HttpRetryPolicyConfig(_logger);
    }

    #region 斜杠命令

    
    [ComponentInteraction("Vote:*:*")]
    public async Task HandleAuditAsync(string action, ulong userId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Submit] Context.Interaction is Not SocketMessageComponent");
                return;
            }
            
            // ========================================
            // Auth
            // ========================================
            // ==== 投票前置需求 ====================
            // - 身份组
            var guildUser = (IGuildUser)Context.User;
            if (!_voteService.HasVoteRequiredRole(guildUser.RoleIds))
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("### ⛔️ 条件检查未通过: 无满足规则的身份组")
                        .WithTextDisplay("参选人须属于以下身份组之一：: 创作者 / 破限组 / 社区志愿者")
                    ).Build();
                await RespondAsync(components: component, ephemeral: true);
                return;
            }

            var cache = _interactionsCacheManager.RegisterView<VoteData>(Context.User.Id, new() { Voter = Context.User.Id });
            
            await RespondAsync(components: cache.Render(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleAuditAsync Exception");
        }
    }
    #endregion
}