using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.SearchBot.Models;

/// <summary>
/// 数据库直接返回的推送简要信息
/// </summary>
public class PushDisplayModelDb
{
    public long ThreadId { get; set; }
    public long? AuthorId { get; set; }
    public bool? IsUpdate { get; set; } // true=更新, false=新帖

    public static PushDisplayModelDb FromDomain(PushDisplayModel pushDisplayModel) => new()
    {
        ThreadId = pushDisplayModel.ThreadId.ToDbId(),
        AuthorId = pushDisplayModel.AuthorId?.ToDbId(),
        IsUpdate = pushDisplayModel.IsUpdate
    };

    public PushDisplayModel ToDomain() => new()
    {
        ThreadId = ThreadId.ToDiscordId(),
        AuthorId = AuthorId?.ToDiscordId(),
        IsUpdate = IsUpdate
    };
}

/// <summary>
/// 用于在分页器中展示的推送条目
/// </summary>
public class PushDisplayModel
{
    public ulong ThreadId { get; set; }
    public ulong? AuthorId { get; set; }
    public bool? IsUpdate { get; set; } // true=更新, false=新帖
}