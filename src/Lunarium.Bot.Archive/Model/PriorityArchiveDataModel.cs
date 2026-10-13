using Discord;

namespace Lunarium.Bot.Archive.Model;

public sealed class PriorityThreadInfo
{
    // 帖子ID
    public ulong ThreadId { get; set; }
    // 权重
    public int Priority { get; set; }
    // 添加原因
    public string Reason { get; set; } = "Unknown Reason";
    // 帖子跳转链接
    public string ThreadJumpUrl { get; set; } = "";
    // 帖子作者
    public ulong AuthorId { get; set; }
    // 添加者
    public ulong Adder { get; set; }
}