using Discord;
using Discord.WebSocket;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Common.Caching;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Framework;

namespace Lunarium.Bot.Ticket.InteractionSession;

public record ConfigEditorContext(GuildConfig Config, SocketGuild Guild, bool IsNew) : ISessionContext<ConfigEditorSession>
{
    public ConfigEditorSession CreateSession(string cacheId, DateTimeOffset expiresAt) => new ConfigEditorSession(cacheId, expiresAt, this);
}

public sealed class ConfigEditorSession : BaseInteractionsSession<ConfigEditorContext>
{
    private bool _isCancel = false;
    private bool _isComplete = false;
    private bool IsFinished => _isCancel || _isComplete;
    protected override string Scope { get; } = "editor";
    private readonly GuildConfig _guildConfig;

    /// <summary>归档后是否删除频道（当前会话值，供面板切换按钮显示）。</summary>
    public bool DeleteChannelOnArchive => _guildConfig.DeleteChannelOnArchive;

    public ConfigEditorSession(string sessionId, DateTimeOffset expiresAt, ConfigEditorContext context)
        : base(sessionId, expiresAt, context)
    {
        if (context.IsNew)
        {
            _guildConfig = new();
        }
        else
        {
            _guildConfig = context.Config;
        }
    }

    public override MessageComponent Render()
    {
        // 1
        var component = new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);
            // 2
            container.WithTextDisplay($"""
            ## 投诉系统配置编辑
            - 服务器: {Context.Guild.Name}
            - Id: {Context.Guild.Id}
            """);

            // 3
            container.WithSeparator();
            // 4
            container.WithTextDisplay($"""
            ### 允许管理投诉的管理组身份组
            {_guildConfig.AllowManageTicketAdminRoleIds.JoinRoleMentions()}
            """);
            // 5
            container.WithActionRow(row =>
            {
                // 6
                row.WithButton(
                    label: "编辑",
                    customId: CustomId("editAdminRoleIds"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });

            // 7
            container.WithSeparator();
            // 8
            container.WithTextDisplay($"""
            ### 允许投诉的身份组
            {_guildConfig.AllowTicketRoleIds.JoinRoleMentions()}
            """);
            // 9
            container.WithActionRow(row =>
            {
                // 10
                row.WithButton(
                    label: "编辑",
                    customId: CustomId("editAllowTicketRoleIds"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });

            // 11
            container.WithSeparator();
            // 12
            container.WithTextDisplay($"""
            ### 投诉入口面板发布频道
            {_guildConfig.EntryPaneChannel.ToChannelMention()}
            """);
            // 13
            container.WithActionRow(row =>
            {
                // 14
                row.WithButton(
                    label: "编辑",
                    customId: CustomId("editEntryPaneChannel"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });

            // 15
            container.WithSeparator();
            // 16
            container.WithTextDisplay($"""
            ### 工单通知频道
            {_guildConfig.NotifierChannel.ToChannelMention()}
            """);
            // 17
            container.WithActionRow(row =>
            {
                // 18
                row.WithButton(
                    label: "编辑",
                    customId: CustomId("editNotifierChannel"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });

            // 19
            container.WithSeparator();
            // 20
            container.WithTextDisplay($"""
            ### 工单日志频道
            {_guildConfig.TicketLogChannel.ToChannelMention()}
            """);
            // 21
            container.WithActionRow(row =>
            {
                // 22
                row.WithButton(
                    label: "编辑",
                    customId: CustomId("editTicketLogChannel"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });

            // 23
            container.WithSeparator();
            // 24
            container.WithTextDisplay($"""
            ### 创建投诉频道的分组
            {_guildConfig.TicketCategory.ToChannelMention()}
            """);
            // 25
            container.WithActionRow(row =>
            {
                // 26
                row.WithButton(
                    label: "编辑",
                    customId: CustomId("editTicketCategory"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });

            // 27
            // 时间类设置合并为一段：Discord 单条消息组件上限 40，拆成两段会超限
            container.WithSeparator();
            // 28
            container.WithTextDisplay($"""
            ### 自动处理设置
            - 投诉冷却: {(_guildConfig.EnableTicketCd ? $"{_guildConfig.TicketCd} 小时" : "已关闭")}
            - 待审核自动通过: {(_guildConfig.AutoApproveHours > 0 ? $"{_guildConfig.AutoApproveHours} 小时" : "未启用")}
            - 归档后频道: {(_guildConfig.DeleteChannelOnArchive ? $"{_guildConfig.ArchiveDeleteCountdownSeconds} 秒后删除" : "保留（仅管理组可见）")}
            """);
            // 29
            container.WithActionRow(row =>
            {
                // 30
                row.WithButton(
                    label: "启用冷却",
                    customId: CustomId("enableTicketCD"),
                    style: _guildConfig.EnableTicketCd ? ButtonStyle.Success : ButtonStyle.Primary,
                    disabled: IsFinished
                );
                // 31
                row.WithButton(
                    label: "关闭冷却",
                    customId: CustomId("disableTicketCD"),
                    style: _guildConfig.EnableTicketCd ? ButtonStyle.Primary : ButtonStyle.Danger,
                    disabled: IsFinished
                );
                // 32
                row.WithButton(
                    label: "编辑冷却",
                    customId: CustomId("editTicketCD"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
                // 33
                row.WithButton(
                    label: "编辑自动通过",
                    customId: CustomId("editAutoApprove"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
                // 34
                row.WithButton(
                    label: _guildConfig.DeleteChannelOnArchive ? "归档后保留频道" : "归档后删除频道",
                    customId: CustomId("toggleDeleteChannelOnArchive"),
                    style: _guildConfig.DeleteChannelOnArchive ? ButtonStyle.Danger : ButtonStyle.Primary,
                    disabled: IsFinished
                );
            });

            // 36
            container.WithSeparator();
            if (_isCancel)
            {
                // 37
                container.WithTextDisplay("配置编辑已取消");
            }
            else if (_isComplete)
            {
                // 37
                container.WithTextDisplay("配置已应用");
            }
            else
            {
                // 37
                container.WithActionRow(row =>
                {
                    // 38
                    row.WithButton(
                        label: "应用",
                        customId: CustomId("apply"),
                        style: ButtonStyle.Success,
                        disabled: IsFinished
                    );
                    // 39
                    row.WithButton(
                        label: "取消",
                        customId: CustomId("cancel"),
                        style: ButtonStyle.Danger,
                        disabled: IsFinished
                    );
                });
            }
        });

        // 共 38 个组件, discord 上限 40
        // 计算方法: 最大可能组合

        return component.Build();
    }

    public void Complete()
    {
        _isComplete = true;
    }

    public GuildConfig BuildConfig()
    {
        return _guildConfig.GetDeepCopy();
    }

    public void Cancel()
    {
        _isCancel = true;
    }

    public void SetAdminRoleIds(IReadOnlyList<ulong> ids)
    {
        _guildConfig.AllowManageTicketAdminRoleIds = ids.ToList();
    }

    public void SetAllowTicketRoleIds(IReadOnlyList<ulong> ids)
    {
        _guildConfig.AllowTicketRoleIds = ids.ToList();
    }

    public void SetEntryPaneChannel(ulong channelId)
    {
        _guildConfig.EntryPaneChannel = channelId;
    }

    public void SetNotifierChannel(ulong channelId)
    {
        _guildConfig.NotifierChannel = channelId;
    }

    public void SetTicketLogChannel(ulong channelId)
    {
        _guildConfig.TicketLogChannel = channelId;
    }

    public void SetTicketCategory(ulong categoryId)
    {
        _guildConfig.TicketCategory = categoryId;
    }

    public void SetEnableTicketCd(bool enabled)
    {
        _guildConfig.EnableTicketCd = enabled;
    }

    public void SetTicketCd(int hours)
    {
        _guildConfig.TicketCd = hours;
    }

    public void SetAutoApproveHours(int hours)
    {
        _guildConfig.AutoApproveHours = hours;
    }

    public void SetDeleteChannelOnArchive(bool enabled)
    {
        _guildConfig.DeleteChannelOnArchive = enabled;
    }
}