namespace LunariumBot.Net.Extensions;

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
}

#endregion