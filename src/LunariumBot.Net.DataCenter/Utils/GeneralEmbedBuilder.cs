// NuGet 包依赖
using Discord;

// 解决方案内引用项目
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.DataCenter.Utils;

public static class EmbedUtils
{
    public static Embed InfoEmbed(string title, string description)
    {
        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(Color.Blue)
            .Build();
    }
    public static Embed AuthEmbed(string description)
    {
        return new EmbedBuilder()
            .WithTitle("权限不足")
            .WithDescription(description)
            .WithColor(Color.Red)
            .Build();
    }

    public static Embed SuccessEmbed(string description, string title = "成功")
    {
        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(Color.Green)
            .Build();
    }

    public static Embed FailedEmbed(string description, string reason = "")
    {
        return new EmbedBuilder()
            .WithTitle("失败")
            .WithDescription(description + "\n原因:\n```" + reason.Slice(2000) + "\n```")
            .WithColor(Color.Red)
            .Build();
    }
}
