using Discord.Interactions;
using Discord;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.AI;
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
    private readonly IAiKeyConfigurator _aiKeyConfigurator;

    public MaintainCommand(
        ILogger logger,
        ConfigLoader<TicketConfig> configLoader,
        SessionManager sessionManager,
        IAiKeyConfigurator aiKeyConfigurator)
    {
        _logger = logger.ForContext("AppInteraction.MaintainCommand");
        _configLoader = configLoader;
        _sessionManager = sessionManager;
        _aiKeyConfigurator = aiKeyConfigurator;
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

    /// <summary>
    /// 在进程内存中设置 AI API Key。不落盘、不写环境变量，重启后自动失效。
    /// </summary>
    [SlashCommand("设置ai密钥", "在进程内存中设置 AI API Key（重启后失效，不影响环境变量）")]
    public async Task SetAiKeyAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (!await EnsureDeployerAsync(track, "设置ai密钥"))
            {
                return;
            }

            await RespondWithModalAsync(CreateAiKeyModal());

            track.Info("已打开 AI 密钥设置模态窗: GuildId={GuildId}, UserId={UserId}",
                Context.Guild?.Id, Context.User.Id);
        }
        catch (Exception ex)
        {
            track.Error(ex, "打开 AI 密钥设置模态窗异常: GuildId={GuildId}, UserId={UserId}",
                Context.Guild?.Id, Context.User.Id);
        }
    }

    /// <summary>
    /// 密钥输入消息上的按钮：仅部署者/维护者可打开设置模态窗，其他人只收到 ephemeral 拒绝。
    /// </summary>
    [ComponentInteraction(AiKeyEntry.CustomIdString)]
    public async Task AiKeyEntryButtonAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (!await EnsureDeployerAsync(track, "ai密钥入口按钮"))
            {
                return;
            }

            await RespondWithModalAsync(CreateAiKeyModal());

            track.Info("已通过按钮打开 AI 密钥设置模态窗: GuildId={GuildId}, UserId={UserId}",
                Context.Guild?.Id, Context.User.Id);
        }
        catch (Exception ex)
        {
            track.Error(ex, "处理 AI 密钥入口按钮异常: GuildId={GuildId}, UserId={UserId}",
                Context.Guild?.Id, Context.User.Id);
        }
    }

    /// <summary>清除运行期设置的 Key；环境变量与 AiOptions.ApiKey 不受影响。</summary>
    [SlashCommand("清除ai密钥", "清除运行期设置的 AI API Key（环境变量不受影响）")]
    public async Task ClearAiKeyAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (!await EnsureDeployerAsync(track, "清除ai密钥"))
            {
                return;
            }

            var hadRuntimeKey = _aiKeyConfigurator.HasRuntimeKey;
            _aiKeyConfigurator.SetApiKey(null);

            track.Info("运行期 AI 密钥已清除: HadRuntimeKey={HadRuntimeKey}, GuildId={GuildId}, UserId={UserId}",
                hadRuntimeKey, Context.Guild?.Id, Context.User.Id);

            await RespondAsync(
                components: MessagePrefab.OperateSuccess(hadRuntimeKey
                    ? "已清除运行期设置的 AI API Key，重启后仍会按环境变量重新读取。"
                    : "当前没有运行期设置的 AI API Key。"),
                ephemeral: true);
        }
        catch (Exception ex)
        {
            track.Error(ex, "清除 AI 密钥异常: GuildId={GuildId}, UserId={UserId}",
                Context.Guild?.Id, Context.User.Id);
        }
    }

    [ModalInteraction(AiKeyModal.CustomId)]
    public async Task SetAiKeyModalAsync(AiKeyModal modal)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            if (!await EnsureDeployerAsync(track, "设置ai密钥(模态窗)"))
            {
                return;
            }

            if (Context.Interaction is not SocketModal socketModal)
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("当前交互不是模态窗交互"), ephemeral: true);
                return;
            }

            var key = socketModal.Data.Components
                .FirstOrDefault(component => component.CustomId == AiKeyModal.KeyField)?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                await RespondAsync(components: MessagePrefab.AnyFail("API Key 不能为空", "### 输入无效"), ephemeral: true);
                return;
            }

            _aiKeyConfigurator.SetApiKey(key);

            // 只记录长度，绝不记录 Key 本身
            track.Info("AI API Key 已在运行期设置: Length={Length}, GuildId={GuildId}, UserId={UserId}",
                key.Length, Context.Guild?.Id, Context.User.Id);

            var content = $"AI API Key 已设置（{key.Length} 字符），仅当前进程有效。";

            // 来自密钥输入消息时，把原消息原地改成"已输入"；来自斜杠命令时用 ephemeral 回执
            if (socketModal.Message is not null)
            {
                await socketModal.UpdateAsync(message => message.Components = MessagePrefab.AfterEntryKey(content));
            }
            else
            {
                await RespondAsync(
                    components: MessagePrefab.OperateSuccess(content),
                    ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            track.Error(ex, "设置 AI 密钥异常: GuildId={GuildId}, UserId={UserId}",
                Context.Guild?.Id, Context.User.Id);
        }
    }

    /// <summary>校验当前用户是否是部署者/维护者；失败时已发送回执。</summary>
    private async Task<bool> EnsureDeployerAsync(TrackScope track, string action)
    {
        var failure = await RuleEngine.ValidateAsync(
            Context,
            new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()));

        if (failure is null)
        {
            return true;
        }

        track.Warning("鉴权未通过: Action={Action}, GuildId={GuildId}, UserId={UserId}",
            action, Context.Guild?.Id, Context.User.Id);
        await RespondAsync(components: failure, ephemeral: true);
        return false;
    }

    private static Modal CreateAiKeyModal()
    {
        return new ModalBuilder()
            .WithTitle("设置 AI API Key")
            .WithCustomId(AiKeyModal.CustomId)
            .AddTextInput(
                label: "API Key",
                customId: AiKeyModal.KeyField,
                style: TextInputStyle.Short,
                minLength: 1,
                maxLength: 512,
                required: true,
                description: "仅保存在进程内存，重启后自动失效")
            .Build();
    }
}

/// <summary>密钥输入消息的按钮标识（<c>scope:action:session</c>）。</summary>
public static class AiKeyEntry
{
    public const string CustomIdString = "ai:key:entry";

    public static CustomId ButtonCustomId => new("ai", "key", "entry");
}

/// <summary>
///     InteractionService 要求 <c>[ModalInteraction]</c> 方法的最后一个参数实现 <see cref="IModal"/>。
///     这里仅用于满足该契约；实际字段值由 <see cref="MaintainCommand"/> 从 <c>modal.Data.Components</c> 读取。
/// </summary>
public sealed class AiKeyModal : IModal
{
    public const string CustomId = "ai-key-set";
    public const string KeyField = "key";

    public string Title => "设置 AI API Key";
}

