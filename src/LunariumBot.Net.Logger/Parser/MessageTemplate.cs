using System.Text;

namespace LunariumBot.Net.Logger.Parser;

internal enum Destructuring
{
    Default,      // {Name}
    Destructure,  // {@Object}
    Stringify     // {$Object}
}
internal enum PropertyType
{
    Named,
    Positional
}

public record MessageTemplate
{
    internal readonly MessageTemplateTokens[] _messageTemplateTokens;
    internal MessageTemplate(MessageTemplateTokens[] messageTemplateTokens)
    {
        _messageTemplateTokens = messageTemplateTokens;
    }
}

// Token基类
public abstract record MessageTemplateTokens;

public record TextToken : MessageTemplateTokens
{
    internal string Text { get; }
    internal TextToken(string text)
    {
        Text = text;
    }
}

public record PropertyToken : MessageTemplateTokens
{
    internal string PropertyName { get; }
    internal TextToken RawText { get; }
    internal PropertyType PropertyType { get; }
    internal string? Format { get; }
    internal int? Alignment { get; }
    internal Destructuring Destructuring { get; }

    internal PropertyToken(
        string propertyName,
        TextToken rawText,
        PropertyType propertyType,
        string? format = null,
        int? alignment = null,
        Destructuring destructuring = Destructuring.Default
    )
    {
        PropertyName = propertyName;
        RawText = rawText;
        PropertyType = propertyType;
        Format = format;
        Alignment = alignment;
        Destructuring = destructuring;
    }
}