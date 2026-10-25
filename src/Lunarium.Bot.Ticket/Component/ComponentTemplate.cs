using System.Globalization;

using Discord;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Model;

namespace Lunarium.Bot.Ticket.Component;

/// <summary>
/// Discord 消息模板集合（按用途拆分为多个 partial 文件）：
/// <list type="bullet">
///   <item><c>ComponentTemplate.cs</c>：共享辅助方法</item>
///   <item><c>ComponentTemplate.Dashboard.cs</c>：入口面板</item>
///   <item><c>ComponentTemplate.TicketChannel.cs</c>：工单频道开场消息</item>
///   <item><c>ComponentTemplate.Audit.cs</c>：审核卡片与拒绝模态窗</item>
///   <item><c>ComponentTemplate.EntryModals.cs</c>：四类提交模态窗</item>
///   <item><c>ComponentTemplate.Status.cs</c>：投诉记录里的状态卡片</item>
///   <item><c>ComponentTemplate.Soothe.cs</c>：自动安抚的通知卡片</item>
/// </list>
/// </summary>
public static partial class ComponentTemplate
{
    /// <summary>
    ///     把强类型选项转成 Discord 下拉选项。
    ///     线上值使用枚举的整数编号（如 <c>101</c>），它与 <see cref="Model.TicketCategory" /> 的稳定编号一致，
    ///     枚举成员改名不会影响已发出的消息。
    /// </summary>
    private static List<SelectMenuOptionBuilder> ToSelectOptions<TEnum>(IEnumerable<SelectOption<TEnum>> options)
        where TEnum : struct, Enum =>
        options.Select(option => new SelectMenuOptionBuilder(
            option.Label,
            ((IFormattable)option.Value).ToString("D", CultureInfo.InvariantCulture))).ToList();

    /// <summary>
    ///     把多行文本逐行转成 Markdown 引用块。
    ///     Discord 的 <c>&gt;</c> 只对当前行生效，不逐行处理的话第二行起会掉出引用块。
    /// </summary>
    private static string QuoteLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return string.Join('\n', normalized.Split('\n')
            .Select(line => line.Length == 0 ? ">" : $"> {line}"));
    }

    /// <summary>
    ///     自动通过时间的展示行；<see langword="null"/> 表示未启用（必须人工审核）。
    /// </summary>
    private static string FormatAutoApproveLine(DateTimeOffset? autoApproveTime) =>
        autoApproveTime is { } due
            ? $"自动通过时间: <t:{due.ToUnixTimeSeconds()}> | <t:{due.ToUnixTimeSeconds()}:R>"
            : "自动通过: 未启用（需人工审核）";

    /// <summary>
    ///     工单分类的单行展示（一级 • 二级），用于卡片标题；二级为 <see cref="TicketCategory.Unknown"/> 时显示"未分类"。
    /// </summary>
    private static string FormatTicketCategory(TicketType type, TicketCategory category) =>
        $"{type.Describe()} • {(category == TicketCategory.Unknown ? "未分类" : category.Describe())}";
}
