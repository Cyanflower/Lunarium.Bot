using Discord;
using Lunarium.Bot.Framework.Interaction;

namespace Lunarium.Bot.Framework;

public static class ModalWindowPrefab
{
    public static Modal SingleTextField(CustomId customId, string title, string label, string description, string placeholder = "请输入...")
    {
        return new ModalBuilder()
            .WithTitle(title)
            .WithCustomId(customId.ToString())
            .AddTextInput(
                label: label,              // Label 文本，≤45 字符
                customId: "text",                // 1–100 字符
                style: TextInputStyle.Short,     // 单行；多行用 TextInputStyle.Paragraph
                placeholder: placeholder,           // ≤100
                minLength: 1,
                maxLength: 4000,                 // LargestMaxLength = 4000
                required: true,
                description: description)          // Label 下方小字，≤100
        .Build();
    }

    /// <summary>
    ///     身份组多选模态窗。最多可选 <paramref name="maxValues"/> 个身份组。
    /// </summary>
    public static Modal MultiRoleSelectField(
        CustomId customId,
        string title,
        string label,
        string? description = null,
        string placeholder = "请选择身份组",
        int minValues = 0,
        int maxValues = 25,
        bool required = false)
    {
        if (maxValues is < 1 or > SelectMenuBuilder.MaxValuesCount)
            throw new ArgumentOutOfRangeException(nameof(maxValues), maxValues, $"必须在 1–{SelectMenuBuilder.MaxValuesCount} 之间");
        if (minValues < 0 || minValues > maxValues)
            throw new ArgumentOutOfRangeException(nameof(minValues), minValues, "必须在 0 到 maxValues 之间");

        return new ModalBuilder()
            .WithTitle(title)
            .WithCustomId(customId.ToString())
            .AddSelectMenu(
                label: label,                            // Label 文本，≤45 字符
                customId: "roles",                       // 1–100 字符
                placeholder: placeholder,                // ≤100
                minValues: minValues,
                maxValues: maxValues,
                required: required,
                type: ComponentType.RoleSelect,          // 身份组下拉，无需 options
                description: description)                // Label 下方小字，≤100
            .Build();
    }

    /// <summary>
    ///     频道选择模态窗。可通过 <paramref name="channelTypes"/> 限制可选频道类型。
    /// </summary>
    public static Modal ChannelSelectField(
        CustomId customId,
        string title,
        string label,
        string? description = null,
        string placeholder = "请选择频道",
        int minValues = 0,
        int maxValues = 1,
        bool required = false,
        ChannelType[]? channelTypes = null)
    {
        if (maxValues is < 1 or > SelectMenuBuilder.MaxValuesCount)
            throw new ArgumentOutOfRangeException(nameof(maxValues), maxValues, $"必须在 1–{SelectMenuBuilder.MaxValuesCount} 之间");
        if (minValues < 0 || minValues > maxValues)
            throw new ArgumentOutOfRangeException(nameof(minValues), minValues, "必须在 0 到 maxValues 之间");

        return new ModalBuilder()
            .WithTitle(title)
            .WithCustomId(customId.ToString())
            .AddSelectMenu(
                label: label,                            // Label 文本，≤45 字符
                customId: "channels",                    // 1–100 字符
                placeholder: placeholder,                // ≤100
                minValues: minValues,
                maxValues: maxValues,
                required: required,
                type: ComponentType.ChannelSelect,       // 频道下拉，无需 options
                channelTypes: channelTypes,              // null = 全部频道类型
                description: description)                // Label 下方小字，≤100
            .Build();
    }
}