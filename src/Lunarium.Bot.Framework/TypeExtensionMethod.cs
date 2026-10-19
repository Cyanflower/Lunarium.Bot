using System.ComponentModel;
namespace Lunarium.Bot.Framework;

#region string

public static class StringExtensions
{
    // 'this string value' 表明这是一个针对 string 类型的扩展方法
    public static string Slice(this string value, int maxLength, string truncationSuffix = "...")
    {
        // 检查输入是否为空或null
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // 如果字符串本身未超长，直接返回
        if (value.Length <= maxLength)
        {
            return value;
        }

        // 如果字符串本身长度小于后缀长度, 直接返回字符串
        if (truncationSuffix.Length >= maxLength)
        {
            return value;
        }

        // 截断并添加后缀
        // 确保截断后的长度加上后缀的长度不超过 maxLength
        int truncatedLength = maxLength - truncationSuffix.Length;
        return value.Substring(0, truncatedLength) + truncationSuffix;
    }
}

#endregion

#region int, long, ulong

public static class DiscordExtensions
{
    // ulong 转 long
    public static long ToDbId(this ulong discordId)
    {
        return unchecked((long)discordId);
    }

    // long 转 ulong
    public static ulong ToDiscordId(this long dbId)
    {
        return unchecked((ulong)dbId);
    }

    // uint 转 int
    public static int ToDbId(this uint discordId)
    {
        return unchecked((int)discordId);
    }

    // int 转 uint
    public static uint ToDiscordId(this int dbId)
    {
        return unchecked((uint)dbId);
    }
}

#endregion

#region Enum

public static class EnumExtensions
{
    // 基础方法：获取单个枚举的描述
    public static string ToDescription(this Enum value)
    {
        if (value == null) return "";
        var field = value.GetType().GetField(value.ToString());
        if (field == null) return value.ToString();
        var attr = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
        return attr?.Description ?? value.ToString();
    }
}

#endregion

public static class DiscordMentionExtensions
{
    #region 单一 ID 转换

    public static string ToRoleMention(this ulong id) => $"<@&{id}>";
    public static string ToUserMention(this ulong id) => $"<@{id}>";
    public static string ToChannelMention(this ulong id) => $"<#{id}>";

    #endregion

    #region 身分组 (Role Mentions)

    public static List<string> ToRoleMentions(this IEnumerable<ulong> ids)
    {
        return ids.Select(id => id.ToRoleMention()).ToList();
    }

    public static string JoinRoleMentions(this IEnumerable<ulong> ids, string separator = "\n")
    {
        return string.Join(separator, ids.Select(id => id.ToRoleMention()));
    }

    #endregion

    #region 用户 (User Mentions)

    public static List<string> ToUserMentions(this IEnumerable<ulong> ids)
    {
        return ids.Select(id => id.ToUserMention()).ToList();
    }

    public static string JoinUserMentions(this IEnumerable<ulong> ids, string separator = "\n")
    {
        return string.Join(separator, ids.Select(id => id.ToUserMention()));
    }

    #endregion

    #region 频道 (Channel Mentions)

    public static List<string> ToChannelMentions(this IEnumerable<ulong> ids)
    {
        return ids.Select(id => id.ToChannelMention()).ToList();
    }

    public static string JoinChannelMentions(this IEnumerable<ulong> ids, string separator = "\n")
    {
        return string.Join(separator, ids.Select(id => id.ToChannelMention()));
    }

    #endregion
}