using System;

namespace Discord;

/// <summary>
/// 表示身份组的增强颜色配置 (渐变/全息)。
/// </summary>
public struct RoleColors
{
    public Color PrimaryColor { get; }
    public Color? SecondaryColor { get; }
    public Color? TertiaryColor { get; }

    // 辅助属性: 判断是纯色, 渐变还是全息
    public bool IsSolid => SecondaryColor == null && TertiaryColor == null;
    public bool IsGradient => SecondaryColor != null && TertiaryColor == null;
    public bool IsHolographic => TertiaryColor != null;

    internal RoleColors(uint primary, uint? secondary, uint? tertiary)
    {
        PrimaryColor = new Color(primary);
        SecondaryColor = secondary.HasValue ? new Color(secondary.Value) : (Color?)null;
        TertiaryColor = tertiary.HasValue ? new Color(tertiary.Value) : (Color?)null;
    }
}
