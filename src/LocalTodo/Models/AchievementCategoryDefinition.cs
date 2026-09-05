using System;
using System.Globalization;

namespace LocalTodo.Models;

/// <summary>
/// 用户可维护的成长成果分类。
/// 默认分类和自建分类使用同一种数据结构。
/// </summary>
public sealed class AchievementCategoryDefinition
{
    public const string OtherCategoryId = "builtin-other";

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#7C8598";
    public int SortOrder { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool CanDelete =>
        !string.Equals(
            Id,
            OtherCategoryId,
            StringComparison.OrdinalIgnoreCase);
}

public static class AchievementCategoryColor
{
    public static bool IsValidHex(string? value) =>
        value is { Length: 7 } &&
        value[0] == '#' &&
        int.TryParse(
            value.AsSpan(1),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out _);

    public static string Normalize(string? value) =>
        IsValidHex(value)
            ? value!.ToUpperInvariant()
            : "#7C8598";

    /// <summary>
    /// 将分类颜色与白色混合，生成只用于标签底色的低饱和版本。
    /// </summary>
    public static string CreateSoftColor(string? value)
    {
        string color = Normalize(value);
        int red = Convert.ToInt32(color.Substring(1, 2), 16);
        int green = Convert.ToInt32(color.Substring(3, 2), 16);
        int blue = Convert.ToInt32(color.Substring(5, 2), 16);
        const double colorRatio = 0.11;

        red = (int)Math.Round(255 - (255 - red) * colorRatio);
        green = (int)Math.Round(255 - (255 - green) * colorRatio);
        blue = (int)Math.Round(255 - (255 - blue) * colorRatio);

        return $"#{red:X2}{green:X2}{blue:X2}";
    }
}
