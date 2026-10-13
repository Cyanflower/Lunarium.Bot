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
using Lunarium.Bot.VoteBot.Listener;

namespace Lunarium.Bot.VoteBot.AppInteractionService;


public class SubmitInteractionService : InteractionModuleBase<SocketInteractionContext>
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
    public SubmitInteractionService(
        ILogger logger,
        ConfigLoader configLoader,
        AuthUtil authUtil,
        InteractionsCacheManager interactionsCacheManager,
        VoteService voteService,
        ListenerService listenerService
    )
    {
        _logger = logger.ForContext("SubmitInteractionService");
        _auditLogger = logger.ForContext("CommandAuth");
        _authUtil = authUtil;
        _configLoader = configLoader;
        _interactionsCacheManager = interactionsCacheManager;
        _voteService = voteService;
        _listenerService = listenerService;
        _componentUtils = new ComponentUtils(_logger);
        _httpRetryPolicy = Util.HttpRetryPolicyConfig(_logger);
    }

    // ==================================================================
    // 审核部分
    // ==================================================================
    #region 审核部分

    [ComponentInteraction("Audit:*:*")]
    public async Task HandleAuditAsync(string action, ulong userId)
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

            _auditLogger.Info($"[Audit] Admin: {Context.User.GlobalName}({Context.User.Id}); SubmitterId : {userId}; Action: {action}");

            var data = _voteService.GetSubmit(userId);
            
            if (data == null)
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("用户数据不存在", "HandleAuditAsync: 用户id 未查询到相应数据"), ephemeral: true);
                return;
            }

            switch (action)
            {
                case "Disqualified":
                    {
                        var modal = new ModalBuilder()
                            .WithTitle("拒绝并禁止继续报名")
                            .WithCustomId($"Audit:Modal:Disqualified:{userId}")
                            .AddTextInput(
                                label: "原因",
                                customId: "Input",
                                placeholder: "原因...",
                                maxLength: 50,
                                minLength: 1,
                                required: true,
                                style: TextInputStyle.Short
                            );
                        await RespondWithModalAsync(modal.Build());
                    }
                    return;
                case "ChangesRequested":
                    data.Status = SubmissionStatus.ChangesRequested;
                    {
                        var modal = new ModalBuilder()
                            .WithTitle("需修改")
                            .WithCustomId($"Audit:Modal:ChangesRequested:{userId}")
                            .AddTextInput(
                                label: "原因",
                                customId: "Input",
                                placeholder: "原因...",
                                maxLength: 50,
                                minLength: 1,
                                required: true,
                                style: TextInputStyle.Short
                            );
                        await RespondWithModalAsync(modal.Build());
                    }
                    return;
                // case "Approved":
                //     data.Status = SubmissionStatus.Approved;
                //     await DeferAsync(true);
                //     {
                //         var component = new ComponentBuilderV2()
                //             .WithContainer(c => c
                //                 .WithAccentColor(Color.Red)
                //                 .WithTextDisplay("### ✅ 旅程 2026 第一季度管理组换届普选")
                //                 .WithTextDisplay("您的报名已通过审核")
                //             ).Build();
                //         await DMNotify(userId, component, "Approved");
                //     }
                //     await ApplyAuditToMessage(data);
                //     return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleAuditAsync");
        }
    }

    [ModalInteraction("Audit:Modal:*:*")]
    public async Task HandleAuditModalAsync(string action, ulong userId, TextInputModal modal)
    {
        try
        {
            if (Context.Interaction is not IModalInteraction modalInteraction)
            {
                _logger.Warning("交互回调: [Submit:Modal:*:*] Context.Interaction is Not modalInteraction");
                return;
            }
            var data = _voteService.GetSubmit(userId);

            if (data == null)
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("用户数据不存在", "HandleAuditAsync: 用户id 未查询到相应数据"), ephemeral: true);
                return;
            }

            var content = modal.Text.Trim();

            await DeferAsync(ephemeral: true);

            switch (action)
            {
                case "Disqualified":
                    data.Status = SubmissionStatus.Disqualified;
                    data.RefuseReason = content;
                    await ApplyAuditToMessage(data);
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(c => c
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay("### ❌ 旅程 2026 第一季度管理组换届普选")
                                .WithTextDisplay($"""
                                    您的报名未通过审核
                                    原因:
                                    {content}
                                    """)
                            ).Build();
                        await DMNotify(userId, component, "Disqualified");
                    }
                    return;
                case "ChangesRequested":
                    data.Status = SubmissionStatus.ChangesRequested;
                    data.RefuseReason = content;
                    await ApplyAuditToMessage(data);
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(c => c
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay("### ⚠️ 旅程 2026 第一季度管理组换届普选")
                                .WithTextDisplay($"""
                                    您的报名未通过审核, 可在修改后尝试重新报名
                                    原因:
                                    {content}
                                    """)
                            ).Build();
                        await DMNotify(userId, component, "ChangesRequested");
                    }
                    return;
            }
            var respondComponent = new ComponentBuilderV2()
                .WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay($"""
                        ### ✅ 成功
                        {(action == "Disqualified" ? "已成功拒绝, 并禁止继续在本次普选中进行投递" : "已成功拒绝本次申请, 申请人可重新投递")}
                        """)
                ).Build();
            await FollowupAsync(components: respondComponent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleSubmitModalsAsync Exception");
        }
    }

    [ComponentInteraction("PublicNotify:*:*")]
    public async Task HandlePublicNotifyAsync(string action, ulong userId)
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

            var data = _voteService.GetSubmit(userId);

            if (data == null)
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("用户数据不存在", "HandleAuditAsync: 用户id 未查询到相应数据"), ephemeral: true);
                return;
            }

            var publicMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IMessageChannel)await Context.Client.GetChannelAsync(_configLoader.Config.DeployConfig.PublicChannel);
            });

            await DeferAsync(ephemeral: true);

            switch (action)
            {
                case "Disqualified":
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(c => c
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay("### ❌ 旅程 2026 第一季度管理组换届普选")
                                .WithTextDisplay($"""
                                    <@{userId}> 的报名未通过审核
                                    原因:
                                    {data.RefuseReason}
                                    """)
                            ).Build();
                        await publicMessageChannel.SendMessageAsync(components: component);
                    }
                    return;
                case "ChangesRequested":
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(c => c
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay("### ⚠️ 旅程 2026 第一季度管理组换届普选")
                                .WithTextDisplay($"""
                                    <@{userId}> 的报名未通过审核, 可在修改后尝试重新报名
                                    原因:
                                    {data.RefuseReason}
                                    """)
                            ).Build();
                        await publicMessageChannel.SendMessageAsync(components: component);
                    }
                    return;
            }
            var respondComponent = new ComponentBuilderV2()
                .WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay($"""
                        ### ✅ 成功
                        已成功在公开频道重新通知用户
                        """)
                ).Build();
            await FollowupAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleAuditAsync");
        }
    }

    #endregion

    // ==================================================================
    // 用户交互部分
    // ==================================================================
    #region 用户交互部分

    [ComponentInteraction("Submit:*:*")]
    public async Task HandleSubmitAsync(string action, string cacheId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Submit:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var cache = _interactionsCacheManager.GetView<SubmitCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            switch (action)
            {
                case "AboutMe":
                    var aboutMeModal = new ModalBuilder()
                        .WithTitle("自我介绍")
                        .WithCustomId($"Submit:Modal:EditAboutMe:{cacheId}")
                        .AddTextInput(
                            label: "自我介绍 (1 到 500 字)",
                            customId: "Input",
                            placeholder: "介绍一下自己...",
                            maxLength: 500,
                            minLength: 1,
                            required: true,
                            style: TextInputStyle.Paragraph
                        );
                    await RespondWithModalAsync(aboutMeModal.Build());
                    return;

                case "Preferences":
                    {
                        var selectedValues = interactionComponent.Data.Values;

                        cache.Data.Preferences.Clear();

                        foreach (var val in selectedValues)
                        {
                            if (Enum.TryParse<PreferenceType>(val, out var enumVal))
                            {
                                cache.Data.Preferences.Add(enumVal);
                            }
                        }
                    }
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;

                case "FirstChoice":
                    {
                        var selectValue = interactionComponent.Data.Values.First();
                        var enumValue = Enum.Parse<Choice>(selectValue);
                        cache.Data.FirstChoice = enumValue;

                        if (cache.Data.SecondChoice != Choice.None && enumValue == cache.Data.SecondChoice)
                        {
                            var conflictComponent = new ComponentBuilderV2()
                                .WithContainer(c => c
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay("### 第一志愿和第二志愿不能相同")
                                ).Build();

                            await FollowupAsync(components: conflictComponent, ephemeral: true);
                            return;
                        }

                    }
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;

                case "SecondChoice":
                    {
                        var selectValue = interactionComponent.Data.Values.First();
                        var enumValue = Enum.Parse<Choice>(selectValue);

                        if (cache.Data.FirstChoice != Choice.None && enumValue == cache.Data.FirstChoice)
                        {
                            var conflictComponent = new ComponentBuilderV2()
                                .WithContainer(c => c
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay("### 第二志愿和第一志愿不能相同")
                                ).Build();

                            await FollowupAsync(components: conflictComponent, ephemeral: true);
                            return;
                        }
                        
                        cache.Data.SecondChoice = enumValue;
                    }
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    return;

                case "Manifesto":
                    var manifestoModal = new ModalBuilder()
                        .WithTitle("竞选宣言")
                        .WithCustomId($"Submit:Modal:EditManifesto:{cacheId}")
                        .AddTextInput(
                            label: "竞选宣言 (1 到 500 字)",
                            customId: "Input",
                            placeholder: "您的竞选宣言...",
                            maxLength: 500,
                            minLength: 1,
                            required: true,
                            style: TextInputStyle.Paragraph
                        );
                    await RespondWithModalAsync(manifestoModal.Build());
                    return;

                case "PressRelease":
                    var modal = new ModalBuilder()
                        .WithTitle("新闻试稿")
                        .WithCustomId($"Submit:Modal:EditPressRelease:{cacheId}")
                        .AddTextInput(
                            label: "新闻试稿 (1 到 1000 字)",
                            customId: "Input",
                            placeholder: "新闻试稿...",
                            maxLength: 1000,
                            minLength: 1,
                            required: true,
                            style: TextInputStyle.Paragraph
                        );
                    await RespondWithModalAsync(modal.Build());
                    return;

                case "Confirm":

                    await DeferAsync(ephemeral: true);

                    cache.Data.UserAvatarsUrl = Context.User.GetAvatarUrl();
                    cache.Data.Status = SubmissionStatus.Approved;

                    var publicMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        return (IMessageChannel)await Context.Client.GetChannelAsync(_configLoader.Config.DeployConfig.PublicChannel);
                    });

                    var auditMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        return (IMessageChannel)await Context.Client.GetChannelAsync(_configLoader.Config.DeployConfig.AuditChannel);
                    });

                    var publicMessage = await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        return await publicMessageChannel.SendMessageAsync(components: cache.Data.ToPublicMessageComponent());
                    });

                    var auditMessage = await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        return await auditMessageChannel.SendMessageAsync(components: cache.Data.ToAuditMessageComponent());
                    });

                    var data = cache.Complete(publicMessage.Id, auditMessage.Id);

                    _voteService.Submit(Context.User.Id, data);

                    await ModifyOriginalResponseAsync(msg => msg.Components = cache.Render());

                    var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Green)
                            .WithTextDisplay("### ✅ 报名成功")
                        ).Build();
                        
                    await FollowupAsync(components: component, ephemeral: true);
                    return;

                case "Cancel":
                    cache.Cancel();
                    await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                    _interactionsCacheManager.TryRemove(cacheId);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleSubmitAsync Exception");
        }
    }
    
    [ModalInteraction("Submit:Modal:*:*")]
    public async Task HandleSubmitModalsAsync(string type, string cacheId, TextInputModal modal)
    {
        try
        {
            if (Context.Interaction is not IModalInteraction modalInteraction)
            {
                _logger.Warning("交互回调: [Submit:Modal:*:*] Context.Interaction is Not modalInteraction");
                return;
            }

            var cache = _interactionsCacheManager.GetView<SubmitCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }
            
            var content = modal.Text.Trim();

            switch (type)
            {
                case "EditAboutMe":
                    cache.Data.AboutMe = content;
                    await modalInteraction.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "EditManifesto":
                    cache.Data.Manifesto = content;
                    await modalInteraction.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
                case "EditPressRelease":
                    cache.Data.PressRelease = content;
                    await modalInteraction.UpdateAsync(msg => msg.Components = cache.Render());
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleSubmitModalsAsync Exception");
        }
    }

    [ComponentInteraction("Submit")]
    public async Task HandleSubmitAsync()
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
            // ==== 报名前置需求 ====================
            // - 活跃
            if (!_voteService.IsActive(Context.User.Id))
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("### ⛔️ 条件检查未通过: 无满足规则的作品贴")
                        .WithTextDisplay(">参选人须属于【资源区 / 教程区】有满足条件的有效发帖记录: 在 2025.9.1 至 2025.12.31 时间段内发布且仍然存在的未锁定帖子")
                    ).Build();
                await RespondAsync(components: component, ephemeral: true);
                return;
            }
            // - 身份组
            var guildUser = (IGuildUser)Context.User;
            if (!_voteService.HasSubmitRequiredRole(guildUser.RoleIds))
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
            
            // ==== 状态检查 ====================
            var data = _voteService.GetSubmit(Context.User.Id);
            
            if (data is not null)
            {
                // if (data.Status == SubmissionStatus.PendingAudit || data.Status == SubmissionStatus.Approved)
                if (data.Status == SubmissionStatus.Approved)
                {
                    var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("### ℹ️ 您已报名")
                            .WithTextDisplay("请勿重复报名, 如需修改已报名内容, 请先撤回后再重新报名")
                        ).Build();
                    await RespondAsync(components: component, ephemeral: true);
                    return;
                }
                if (data.Status == SubmissionStatus.Disqualified)
                {
                    var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("### ⚠️ 您已被拒绝")
                            .WithTextDisplay("您在本次普选中未通过审核")
                        ).Build();
                    await RespondAsync(components: component, ephemeral: true);
                    return;
                }
            }

            var cache = _interactionsCacheManager.RegisterView<SubmitData>(Context.User.Id, new() { SubmitterId = Context.User.Id});

            await RespondAsync(components: cache.Render(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PublishEntryPanel Exception");
        }
    }

    [ComponentInteraction("Cancel")]
    public async Task HandleCancelAsync()
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Cancel] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            // ========================================
            // Auth
            // ========================================
            // ==== 状态检查 ====================
            var data = _voteService.GetSubmit(Context.User.Id);

            if (data is not null)
            {
                if (data.Status == SubmissionStatus.Disqualified)
                {
                    var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("### ⚠️ 您无需撤销报名")
                            .WithTextDisplay("您在本次普选中未通过审核")
                        ).Build();
                    await RespondAsync(components: component, ephemeral: true);
                    return;
                }
                if (data.Status == SubmissionStatus.ChangesRequested)
                {
                    var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("### ⚠️ 您无需撤销报名")
                            .WithTextDisplay("如需修改已报名内容, 直接重新报名即可")
                        ).Build();
                    await RespondAsync(components: component, ephemeral: true);
                }
                else
                {
                    await DeferAsync(ephemeral: true);
                    var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("### ℹ️ 您已成功撤销报名")
                            .WithTextDisplay("如需修改已报名内容, 可以重新报名")
                        ).Build();
                    data.Status = SubmissionStatus.CancelledByUser;
                    await ApplyAuditToMessage(data);
                    await FollowupAsync(components: component, ephemeral: true);
                    return;
                }
            }
            else
            {
                var component = new ComponentBuilderV2()
                        .WithContainer(c => c
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay("### ⚠️ 您尚未报名")
                            .WithTextDisplay("请先进行报名")
                        ).Build();
                await RespondAsync(components: component, ephemeral: true);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PublishEntryPanel Exception");
        }
    }

    #endregion

    // ==================================================================
    // 斜杠命令
    // ==================================================================
    #region 斜杠命令

    [SlashCommand("发布报名入口面板", "发布报名入口面板")]
    public async Task PublishEntryPanelAsync()
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
            await DeferAsync(true);

            var message = await Context.Channel.SendMessageAsync(components: Util.GetSubmitMessageComponent());

            _listenerService.SetMessage(message);

            await RespondAsync("成功", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PublishEntryPanel Exception");
        }
    }

    #endregion

    // ==================================================================
    // 私有辅助方法
    // ==================================================================
    #region 私有辅助方法

    private async Task DMNotify(ulong userId, MessageComponent messageComponent, string action)
    {
        try
        {
            var user = await Context.Client.GetUserAsync(userId);
            await user.SendMessageAsync(components: messageComponent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DMNotify Exception");
            var auditMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IMessageChannel)await Context.Client.GetChannelAsync(_configLoader.Config.DeployConfig.AuditChannel);
            });

            var component = new ComponentBuilderV2()
                .WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⚠️ 私信通知失败")
                    .WithTextDisplay($"原因:\n{ex}")
                    .WithActionRow(row => row
                        .WithButton("在公开频道通知", $"PublicNotify:{action}:{userId}", ButtonStyle.Primary)
                    )
                ).Build();

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await auditMessageChannel.SendMessageAsync(components: component);
            });
        }
    }


    private async Task ApplyAuditToMessage(SubmitData submitData)
    {
        try
        {
            var publicMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IMessageChannel)await Context.Client.GetChannelAsync(_configLoader.Config.DeployConfig.PublicChannel);
            });

            var auditMessageChannel = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IMessageChannel)await Context.Client.GetChannelAsync(_configLoader.Config.DeployConfig.AuditChannel);
            });

            var publicMessage = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IUserMessage)await publicMessageChannel.GetMessageAsync(submitData.PublicMessageId);
            });

            var auditMessage = await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                return (IUserMessage)await auditMessageChannel.GetMessageAsync(submitData.AuditMessageId);
            });

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await publicMessage.ModifyAsync(msg => msg.Components = submitData.ToPublicMessageComponent());
            });

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await auditMessage.ModifyAsync(msg => msg.Components = submitData.ToAuditMessageComponent());
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ApplyAuditToMessage Exception");
        }
    }
    
    #endregion
}

public class TextInputModal : IModal
{
    public string Title => "TextInput";

    [ModalTextInput("Input")]
    public string Text { get; set; } = string.Empty;
}
