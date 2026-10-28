using System.Globalization;

using Discord;
using Discord.Interactions;

using Lunarium.Bot.Framework.Interaction;

namespace Lunarium.Bot.Lum.Features.Shared;

/// <summary>只含一个文本输入框（customId 固定为 <c>text</c>）的模态窗提交数据。</summary>
/// <remarks>
/// Discord.Net 要求 <c>[ModalInteraction]</c> 方法的最后一个参数实现 <see cref="IModal"/>，
/// 直接用 <c>SocketModal</c> 会被视为非法定义而静默跳过（表现为 UnknownCommand）。
/// </remarks>
public sealed class TextInputModal : IModal
{
    public string Title => "输入";

    [ModalTextInput("text")]
    public string? Text { get; set; }
}

/// <summary>交互输入的公共构造与解析。</summary>
public static class InteractionInputs
{
    /// <summary>单输入框模态窗（customId 固定为 <c>text</c>）。</summary>
    public static Modal TextModal(
        string scope,
        string action,
        string sessionId,
        string title,
        string label,
        string? value,
        TextInputStyle style,
        int maxLength,
        bool required,
        string? placeholder = null,
        string? description = null)
    {
        return new ModalBuilder()
            .WithTitle(title)
            .WithCustomId(new CustomId(scope, action, sessionId).ToString())
            .AddTextInput(
                label: label,
                customId: "text",
                style: style,
                placeholder: placeholder,
                minLength: 0,
                maxLength: maxLength,
                required: required,
                value: string.IsNullOrEmpty(value) ? null : value,
                description: description)
            .Build();
    }

    /// <summary>解析 <c>#RRGGBB</c> 或 <c>RRGGBB</c>。</summary>
    public static bool TryParseColor(string raw, out Color color)
    {
        color = Color.Blue;

        var text = raw.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length != 6 ||
            !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new Color(value);
        return true;
    }
}
