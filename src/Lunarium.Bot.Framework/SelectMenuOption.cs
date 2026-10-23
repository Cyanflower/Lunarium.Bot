
namespace Lunarium.Bot.Framework;

/// <summary>
///     带强类型值的下拉选项。
/// </summary>
/// <typeparam name="TValue">选项值类型（通常是业务枚举）。</typeparam>
/// <param name="Label">展示给用户的文本。</param>
/// <param name="Value">选项值；由使用方决定如何转成 Discord 的字符串值。</param>
public sealed record SelectOption<TValue>(string Label, TValue Value)
    where TValue : struct, Enum;