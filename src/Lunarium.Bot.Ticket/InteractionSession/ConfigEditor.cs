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
        var component = new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);
            container.WithTextDisplay($"""
            ## 工单系统配置编辑
            - 服务器: {Context.Guild.Name}
            - Id: {Context.Guild.Id}
            """);

            // ==== 身份组（两项合并为一个模态窗，组内不画分隔线） ====
            container.WithSeparator();
            container.WithTextDisplay($"""
            ### 身份组
            **允许管理工单**
            {FormatRoleList(_guildConfig.AllowManageTicketAdminRoleIds)}
            **允许提交工单**
            {FormatRoleList(_guildConfig.AllowTicketRoleIds)}
            """);
            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: "编辑身份组",
                    customId: CustomId("editRoles"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("👥"),
                    disabled: IsFinished
                );
            });

            // ==== 频道（四项合并为一个模态窗） ====
            container.WithSeparator();
            container.WithTextDisplay($"""
            ### 频道
            - 投诉入口面板: {FormatChannel(_guildConfig.EntryPaneChannel)}
            - 工单通知: {FormatChannel(_guildConfig.NotifierChannel)}
            - 工单日志: {FormatChannel(_guildConfig.TicketLogChannel)}
            - 工单频道分组: {FormatChannel(_guildConfig.TicketCategory)}
            """);
            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: "编辑频道",
                    customId: CustomId("editChannels"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("📁"),
                    disabled: IsFinished
                );
            });

            // ==== 自动化（数值合并为一个模态窗，开关用按钮原地切换） ====
            container.WithSeparator();
            container.WithTextDisplay($"""
            ### 自动化
            - 投诉冷却: {(_guildConfig.EnableTicketCd ? $"{_guildConfig.TicketCd} 小时" : "已关闭")}
            - 待审核自动通过: {(_guildConfig.AutoApproveHours > 0 ? $"{_guildConfig.AutoApproveHours} 小时" : "未启用")}
            - 自动安抚: {(_guildConfig.EnableAutoSoothe ? $"自动通过后 {_guildConfig.SootheDelayMinutes} 分钟无管理回复则发送" : "已关闭")}
            - 归档删除倒计时: {_guildConfig.ArchiveDeleteCountdownSeconds} 秒（归档时可选是否删除频道）
            """);
            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: "编辑数值",
                    customId: CustomId("editNumbers"),
                    style: ButtonStyle.Primary,
                    emote: new Emoji("🔢"),
                    disabled: IsFinished
                );
                row.WithButton(
                    label: _guildConfig.EnableTicketCd ? "冷却：开" : "冷却：关",
                    customId: CustomId("toggleTicketCd"),
                    style: _guildConfig.EnableTicketCd ? ButtonStyle.Success : ButtonStyle.Secondary,
                    disabled: IsFinished
                );
                row.WithButton(
                    label: _guildConfig.EnableAutoSoothe ? "自动安抚：开" : "自动安抚：关",
                    customId: CustomId("toggleSoothe"),
                    style: _guildConfig.EnableAutoSoothe ? ButtonStyle.Success : ButtonStyle.Secondary,
                    disabled: IsFinished
                );
            });

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
                        label: "应用",
                        customId: CustomId("apply"),
                        style: ButtonStyle.Success,
                        disabled: IsFinished
                    );
                    row.WithButton(
                        label: "取消",
                        customId: CustomId("cancel"),
                        style: ButtonStyle.Danger,
                        disabled: IsFinished
                    );
                });
            }
        });

        // 按"大分组"组织：身份组 / 频道 / 自动化 各占一个按钮 + 一个多字段模态窗，
        // 组件总数约 15（Discord 上限 40），为后续配置项留出余量。

        return component.Build();
    }

    /// <summary>当前配置的实时视图，供交互层预填模态窗（只读用途）。</summary>
    public GuildConfig Snapshot => _guildConfig;

    private static string FormatRoleList(IReadOnlyCollection<ulong> ids)
        => ids.Count == 0 ? "-# 未设置" : ids.JoinRoleMentions();

    private static string FormatChannel(ulong id)
        => id == 0 ? "-# 未设置" : id.ToChannelMention();

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

    public void SetArchiveDeleteCountdownSeconds(int seconds)
    {
        _guildConfig.ArchiveDeleteCountdownSeconds = seconds;
    }

    public void SetEnableAutoSoothe(bool enabled)
    {
        _guildConfig.EnableAutoSoothe = enabled;
    }

    public void SetSootheDelayMinutes(int minutes)
    {
        _guildConfig.SootheDelayMinutes = minutes;
    }
}