using System.Globalization;

using Discord;

using Lunarium.Bot.Framework;

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
    ///     自动通过时间的展示行；<see langword="null"/> 表示未启用（必须人工审核）。
    /// </summary>
    private static string FormatAutoApproveLine(DateTimeOffset? autoApproveTime) =>
        autoApproveTime is { } due
            ? $"自动通过时间: <t:{due.ToUnixTimeSeconds()}> | <t:{due.ToUnixTimeSeconds()}:R>"
            : "自动通过: 未启用（需人工审核）";
}
