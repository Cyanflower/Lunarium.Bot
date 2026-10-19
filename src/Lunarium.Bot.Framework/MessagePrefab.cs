using Discord;

namespace Lunarium.Bot.Framework;

public static class MessagePrefab
{
    public static MessageComponent AnyFail(string reason, string title = "错误")
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Red);
            container.WithTextDisplay(title);
            container.WithSeparator();
            container.WithTextDisplay(reason);
        })
        .Build();
    }

    public static MessageComponent AuthFail(string reason)
    {
        return AnyFail(reason, "### 鉴权失败");
    }

    public static MessageComponent FunctionFail(string reason)
    {

        return AnyFail(reason, "### 内部错误");
    }
}