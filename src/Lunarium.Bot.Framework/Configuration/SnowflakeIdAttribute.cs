using System.ComponentModel.DataAnnotations;

namespace Lunarium.Bot.Framework.Configuration;

/// <summary>
/// 要求属性是非 0 的雪花 ID（Discord 的 user / channel / role / guild ID）。
/// <para>
/// 刻意不继承 <see cref="RangeAttribute"/>：它的数值比较走转换路径，对 <c>ulong</c> 超过 2^53 的雪花 ID 不安全。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SnowflakeIdAttribute : ValidationAttribute
{
    public SnowflakeIdAttribute()
    {
        ErrorMessage = "必须是非0的雪花ID";
    }

    public override bool IsValid(object? value)
    {
        return value switch
        {
            ulong id => id != 0,
            uint id => id != 0,
            ushort id => id != 0,
            byte id => id != 0,
            long id => id > 0,
            int id => id > 0,
            short id => id > 0,
            _ => false,
        };
    }
}
