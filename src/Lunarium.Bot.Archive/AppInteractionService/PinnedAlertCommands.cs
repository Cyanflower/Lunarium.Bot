using System.Globalization;

using Discord.Interactions;

using Lunarium.Bot.Archive.Archiver;
using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Logging;

namespace Lunarium.Bot.Archive.AppInteractionService;

/// <summary>
/// 置顶告警卡片上的按钮。customId 格式：<c>pinned:{restore|discard}:{guildId}:{channelId}</c>。
/// <para>
/// 掉置顶：恢复置顶 / 忽略；换置顶：恢复旧置顶 / 保持新置顶。
/// 两者的决定都落到 <see cref="PinnedDecision.Restored"/> / <see cref="PinnedDecision.Discarded"/>。
/// </para>
/// </summary>
public sealed class PinnedAlertCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly PinnedMonitoring _pinnedMonitoring;

    public PinnedAlertCommands(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        PinnedMonitoring pinnedMonitoring)
    {
        _logger = logger.ForContext("AppInteraction.PinnedAlert");
        _configLoader = configLoader;
        _pinnedMonitoring = pinnedMonitoring;
    }

    [ComponentInteraction("pinned:*:*:*")]
    public async Task HandleAsync(string action, string guildIdRaw, string channelIdRaw)
    {
        try
        {
            if (!ulong.TryParse(guildIdRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var guildId)
                || !ulong.TryParse(channelIdRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId))
            {
                await RespondAsync(components: MessagePrefab.FunctionFail("告警标识无效"), ephemeral: true);
                return;
            }

            if (Context.Guild is null || Context.Guild.Id != guildId)
            {
                await RespondAsync(components: MessagePrefab.AuthFail("该提醒不属于当前服务器"), ephemeral: true);
                return;
            }

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guildId, out var guildConfig))
            {
                await RespondAsync(components: MessagePrefab.ConfigFail("当前配置中已没有该服务器"), ephemeral: true);
                return;
            }

            PinnedDecision decision;
            switch (action)
            {
                case "restore":
                    decision = PinnedDecision.Restored;
                    break;
                case "discard":
                    decision = PinnedDecision.Discarded;
                    break;
                default:
                    await RespondAsync(components: MessagePrefab.FunctionFail($"未知的操作: {action}"), ephemeral: true);
                    return;
            }

            var bypassIds = _configLoader.Value.DeployerConfig.DeployerIds
                .Append(_configLoader.Value.DeployerConfig.MaintainerId)
                .ToHashSet();

            var failure = await RuleEngine.ValidateAsync(
                Context,
                new BaseRuleSet.MaintainerBypass(bypassIds),
                new BaseRuleSet.RequireRole(guildConfig.StaffRoleIds.ToHashSet()));

            if (failure is not null)
            {
                _logger.Warning("置顶告警鉴权失败: GuildId={GuildId}, ChannelId={ChannelId}, Action={Action}, UserId={UserId}",
                    guildId, channelId, action, Context.User.Id);
                await RespondAsync(components: failure, ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var result = await _pinnedMonitoring.ApplyDecisionAsync(
                guildId, channelId, decision, Context.User.Id, CancellationToken.None);

            _logger.Info("置顶告警已处理: GuildId={GuildId}, ChannelId={ChannelId}, Action={Action}, UserId={UserId}, Success={Success}",
                guildId, channelId, action, Context.User.Id, result.Success);

            await FollowupAsync(
                components: result.Success
                    ? MessagePrefab.OperateSuccess(result.Message)
                    : MessagePrefab.AnyFail(result.Message),
                ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理置顶告警按钮失败: Action={Action}, UserId={UserId}", action, Context.User.Id);

            try
            {
                if (Context.Interaction.HasResponded)
                {
                    await FollowupAsync(components: MessagePrefab.FunctionFail("处理置顶告警时发生内部错误"), ephemeral: true);
                }
                else
                {
                    await RespondAsync(components: MessagePrefab.FunctionFail("处理置顶告警时发生内部错误"), ephemeral: true);
                }
            }
            catch (Exception followupEx)
            {
                _logger.Warning(followupEx, "回报置顶告警处理失败时再次失败");
            }
        }
    }
}
