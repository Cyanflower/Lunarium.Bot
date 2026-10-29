using Discord;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.Interaction;

namespace Lunarium.Bot.Archive.InteractionSession;

public sealed record ArchiveConfigEditorContext(
    GuildConfig Config,
    SocketGuild Guild,
    bool IsNew,
    int IntervalMin) : ISessionContext<ArchiveConfigEditorSession>
{
    public ArchiveConfigEditorSession CreateSession(string cacheId, DateTimeOffset expiresAt)
        => new(cacheId, expiresAt, this);
}

/// <summary>
/// 归档配置面板的会话状态。面板本身只负责暂存；点“应用”时才走
/// <see cref="Lunarium.Bot.Framework.Configuration.ConfigLoader{T}.TryUpdate"/> 落盘。
/// </summary>
public sealed class ArchiveConfigEditorSession : BaseInteractionsSession<ArchiveConfigEditorContext>
{
    private readonly GuildConfig _guildConfig;
    private bool _isCancel;
    private bool _isComplete;

    protected override string Scope { get; } = "archive-editor";

    public ArchiveConfigEditorSession(
        string sessionId,
        DateTimeOffset expiresAt,
        ArchiveConfigEditorContext context)
        : base(sessionId, expiresAt, context)
    {
        _guildConfig = context.IsNew ? new GuildConfig() : context.Config.GetDeepCopy();
    }

    private bool IsFinished => _isCancel || _isComplete;

    /// <summary>当前配置的实时视图，供交互层预填模态窗（只读）。</summary>
    public GuildConfig Snapshot => _guildConfig;

    public override MessageComponent Render()
    {
        var component = new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);
            container.WithTextDisplay($"""
                ## 归档配置编辑
                - 服务器: {Context.Guild.Name}
                - Id: {Context.Guild.Id}
                """);

            container.WithSeparator();
            container.WithTextDisplay($"""
                ### 归档
                - 活跃线程上限: **{_guildConfig.ArchiveLimit}**
                - 全局归档间隔: **{Context.IntervalMin} 分钟**
                > 全局间隔使用 `/维护 重设间隔` 修改
                """);

            container.WithSeparator();
            container.WithTextDisplay($"""
                ### 置顶监控
                - 监控频道: {FormatChannels(_guildConfig.PinnedMonitoringChannelIds)}
                - 告警频道: {FormatChannel(_guildConfig.NotifierChannelId)}
                - 管理身份组: {FormatRoles(_guildConfig.StaffRoleIds)}
                """);

            container.WithSeparator();

            if (_isCancel)
            {
                container.WithTextDisplay("配置编辑已取消");
            }
            else if (_isComplete)
            {
                container.WithTextDisplay("配置已应用");
            }
            else
            {
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "编辑配置",
                        customId: CustomId("edit"),
                        style: ButtonStyle.Primary,
                        emote: new Emoji("⚙️"),
                        disabled: IsFinished);
                    row.WithButton(
                        label: "应用",
                        customId: CustomId("apply"),
                        style: ButtonStyle.Success,
                        disabled: IsFinished);
                    row.WithButton(
                        label: "取消",
                        customId: CustomId("cancel"),
                        style: ButtonStyle.Danger,
                        disabled: IsFinished);
                });
            }
        });

        return component.Build();
    }

    public void Complete() => _isComplete = true;

    public void Cancel() => _isCancel = true;

    public GuildConfig BuildConfig() => _guildConfig.GetDeepCopy();

    public void SetArchiveLimit(int limit) => _guildConfig.ArchiveLimit = limit;

    public void SetPinnedMonitoringChannels(IReadOnlyList<ulong> channelIds)
        => _guildConfig.PinnedMonitoringChannelIds = channelIds.ToList();

    public void SetNotifierChannel(ulong channelId) => _guildConfig.NotifierChannelId = channelId;

    public void SetStaffRoles(IReadOnlyList<ulong> roleIds)
        => _guildConfig.StaffRoleIds = roleIds.ToList();

    private static string FormatChannels(IReadOnlyCollection<ulong> ids)
        => ids.Count == 0 ? "-# 未设置" : ids.JoinChannelMentions(", ");

    private static string FormatRoles(IReadOnlyCollection<ulong> ids)
        => ids.Count == 0 ? "-# 未设置（仅部署者/维护者）" : ids.JoinRoleMentions(", ");

    private static string FormatChannel(ulong id)
        => id == 0 ? "-# 未设置" : id.ToChannelMention();
}
