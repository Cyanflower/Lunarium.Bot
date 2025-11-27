
using System.Web;
using System.Globalization;
using System.Text.Json;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.DataCenter.Models;


public class UrlSignedInfo : IComparable<UrlSignedInfo>
{
    public string Url { get; init; }
    public ulong ChannelId { get; init; }
    public ulong MessageId { get; init; }

    // 解析后的Unix时间戳, 专门用于排序
    public long ExpireTimestamp { get; private set; }

    // 构造函数中解析 Hex
    public UrlSignedInfo(string url, ulong channelId, ulong messageId)
    {
        Url = url;
        ChannelId = channelId;
        MessageId = messageId;

        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var ex = query["ex"];

        if (long.TryParse(ex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long timestampValue))
        {
            ExpireTimestamp = timestampValue;
        }
        else
        {
            throw new FormatException($"Input string ex='{ex}' is not a valid hex timestamp.");
        }
    }

    // 比较逻辑
    public int CompareTo(UrlSignedInfo? other)
    {
        if (other == null) return 1;

        // 第一优先级: 按过期时间排序 (从小到大)
        int timeCompare = this.ExpireTimestamp.CompareTo(other.ExpireTimestamp);
        if (timeCompare != 0) return timeCompare;

        // 第二优先级: 如果时间相同，比较 URL 或 ID
        return string.Compare(this.Url, other.Url, StringComparison.Ordinal);
    }
}

public class UrlSignedInfoDb
{
    public string? Url { get; init; } = "";
    public long ChannelId { get; init; }
    public long MessageId { get; init; }

    // 从来不会使用 UrlSignedInfo 逆向转回 UrlSignedInfoDb, 数据库的 attachment_urls 涉及序列化为json数组字符串
    // public static UrlSignedInfoDb FromDomain(UrlSignedInfo urlSignedInfo) => new()
    // {
    //     Url = urlSignedInfo.Url,
    //     ChannelId = urlSignedInfo.ChannelId,
    //     MessageId = urlSignedInfo.MessageId,
    // };

    public bool TryToDomain(out UrlSignedInfo? result)
    {
        result = null;

        if (string.IsNullOrEmpty(Url)) 
            return false;

        result = new UrlSignedInfo(Url, ChannelId.ToDiscordId(), MessageId.ToDiscordId());

        return true;
    }
}