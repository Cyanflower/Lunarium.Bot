using Discord;
using Lunarium.Bot.Framework.Interaction;

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

    public static MessageComponent AnyNotifier(string reason, string title = "提示")
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);
            container.WithTextDisplay(title);
            container.WithSeparator();
            container.WithTextDisplay(reason);
        })
        .Build();
    }

    public static MessageComponent AnySuccess(string reason, string title = "成功")
    {
        return new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.DarkGreen);
            container.WithTextDisplay(title);
            container.WithSeparator();
            container.WithTextDisplay(reason);
        })
        .Build();
    }

    public static MessageComponent AuthFail(string reason) => AnyFail(reason, "### 鉴权失败");
    

    public static MessageComponent FunctionFail(string reason) => AnyFail(reason, "### 内部错误");
    

    public static MessageComponent ConfigFail(string reason) => AnyFail(reason, "### 配置错误");

    public static MessageComponent OperateSuccess(string reason) => AnySuccess(reason, "### 操作成功");

    public static MessageComponent EntryKey(CustomId customId, string? content = null)
    {
        return new ComponentBuilderV2()
            .WithContainer(container =>
            {
                container.WithAccentColor(Color.Gold);
                container.WithTextDisplay("### 🔑 等待输入密钥");
                container.WithSeparator();
                if (content is not null)
                {
                    container.WithTextDisplay(content);
                }
                container.WithSeparator();
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "输入",
                        customId: customId,
                        style: ButtonStyle.Primary,
                        emote: new Emoji("🔑")
                    );
                });
            })
            .Build();
    }

    public static MessageComponent AfterEntryKey(string? content = null)
    {
        return new ComponentBuilderV2()
            .WithContainer(container =>
            {
                container.WithAccentColor(Color.Gold);
                container.WithTextDisplay("### ✅ 密钥已输入");
                container.WithSeparator();
                if (content is not null)
                {
                    container.WithTextDisplay(content);
                }
                container.WithSeparator();
                container.WithTextDisplay("-# 仅保存在进程内存中，重启后会自动回落到环境变量。");
            })
            .Build();
    }
}