
using System.Text;
using Discord;
using Lunarium.Bot.Archive.Archiver;

namespace Lunarium.Bot.Archive.Notifier;

public static class NotifierComponent
{
    public static MessageComponent PinnedScanFailed(string? reason = null, Exception? ex = null)
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithTextDisplay("论坛置顶帖监控失败");
            container.WithSeparator();
            if (reason is not null)
            {
                container.WithTextDisplay(reason);
                container.WithSeparator();
            }
            if (ex is not null)
            {
                container.WithTextDisplay(ex.Message);
                container.WithSeparator();
            }
        }).Build();
    }

    public static MessageComponent PinnedAdded(IReadOnlyList<PinnedChange> changes, string? reason = null)
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithTextDisplay("论坛置顶帖监控");
            container.WithSeparator();
            var sb = new StringBuilder();
            int i = 0;
            foreach (var change in changes)
            {
                sb.AppendLine($"- <#{change.ChannelId}> : <#{change.ThreadId}>");
                i++;
                if (i >= 30)
                {
                    sb.AppendLine($"其余 {changes.Count - 30} 个已省略....");
                    break;
                }
            }
            container.WithSeparator();
            if (reason is not null)
            {
                container.WithTextDisplay(reason);
                container.WithSeparator();
            }
            container.WithTextDisplay($"""
            新增置顶帖 {changes.Count} 个
            {sb.ToString()}
            """);
        }).Build();
    }

    /// <summary>告警卡片按钮的 customId：pinned:{restore|discard}:{guildId}:{channelId}。</summary>
    public static string DecisionCustomId(string decision, ulong guildId, ulong channelId)
        => $"pinned:{decision}:{guildId}:{channelId}";

    /// <summary>掉置顶告警：父频道里一个置顶帖都没有了。</summary>
    public static MessageComponent PinnedLost(ulong guildId, ulong channelId, ulong threadId, DateTimeOffset expirationDate)
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithTextDisplay("论坛置顶帖监控");
            container.WithSeparator();
            container.WithTextDisplay($"论坛 <#{channelId}> 置顶帖 <#{threadId}> 丢失置顶状态");
            container.WithSeparator();
            container.WithTextDisplay($"-# 到期将自动选择**恢复置顶帖** | 到期时间: <t:{expirationDate.ToUnixTimeSeconds()}:R>");
            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: "恢复置顶",
                    customId: DecisionCustomId("restore", guildId, channelId),
                    style: ButtonStyle.Success,
                    emote: new Emoji("📌"));
                row.WithButton(
                    label: "忽略",
                    customId: DecisionCustomId("discard", guildId, channelId),
                    style: ButtonStyle.Secondary,
                    emote: new Emoji("🗑️"));
            });
        }).Build();
    }

    /// <summary>换置顶告警：同一父频道的置顶帖被替换。</summary>
    public static MessageComponent PinnedUpdated(ulong guildId, ulong channelId, ulong before, ulong after, DateTimeOffset expirationDate)
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithTextDisplay("论坛置顶帖监控");
            container.WithSeparator();
            container.WithTextDisplay($"""
            论坛 <#{channelId}> 置顶帖发生变更
            - 新置顶帖: <#{after}>
            - 旧置顶帖: <#{before}>
            旧置顶帖已被新置顶帖替换
            """);
            container.WithSeparator();
            container.WithTextDisplay($"-# 到期将自动选择**保持新置顶帖** | 到期时间: <t:{expirationDate.ToUnixTimeSeconds()}:R>");
            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: "恢复旧置顶",
                    customId: DecisionCustomId("restore", guildId, channelId),
                    style: ButtonStyle.Success,
                    emote: new Emoji("↩️"));
                row.WithButton(
                    label: "保持新置顶",
                    customId: DecisionCustomId("discard", guildId, channelId),
                    style: ButtonStyle.Secondary,
                    emote: new Emoji("✅"));
            });
        }).Build();
    }

    /// <summary>告警收尾卡片（无按钮）。</summary>
    public static MessageComponent PinnedAlertClosed(PinnedAlert alert, string detail)
    {
        var previous = alert.PreviousThreadId == 0 ? string.Empty : $"\n- 旧置顶帖: <#{alert.PreviousThreadId}>";

        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithTextDisplay("论坛置顶帖监控");
            container.WithSeparator();
            container.WithTextDisplay($"""
            - 父频道: <#{alert.ChannelId}>
            - 新置顶帖: <#{alert.ThreadId}>{previous}
            """);
            container.WithSeparator();
            container.WithTextDisplay($"""
            ### {DecisionTitle(alert.Decision)}
            {detail}
            """);
        }).Build();
    }

    private static string DecisionTitle(PinnedDecision decision) => decision switch
    {
        PinnedDecision.Restored => "已恢复置顶",
        PinnedDecision.Discarded => "已忽略该提醒",
        PinnedDecision.TimedOut => "到期自动处理",
        _ => "处理完成"
    };
}