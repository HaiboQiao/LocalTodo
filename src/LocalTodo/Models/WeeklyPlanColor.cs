namespace LocalTodo.Models;

/// <summary>
/// 每周计划可使用的固定颜色主题。
/// 数值和数据库存储键均保持稳定，不与具体 UI 色值绑定。
/// </summary>
public enum WeeklyPlanColor
{
    Blue = 0,
    Green = 1,
    Teal = 2,
    Purple = 3,
    Pink = 4,
    Orange = 5,
    Yellow = 6,
    Gray = 7
}

public static class WeeklyPlanColorStorage
{
    public const WeeklyPlanColor DefaultColor =
        WeeklyPlanColor.Blue;

    public static string ToStorageKey(
        this WeeklyPlanColor color) =>
        color switch
        {
            WeeklyPlanColor.Blue => "Blue",
            WeeklyPlanColor.Green => "Green",
            WeeklyPlanColor.Teal => "Teal",
            WeeklyPlanColor.Purple => "Purple",
            WeeklyPlanColor.Pink => "Pink",
            WeeklyPlanColor.Orange => "Orange",
            WeeklyPlanColor.Yellow => "Yellow",
            WeeklyPlanColor.Gray => "Gray",
            _ => "Blue"
        };

    public static WeeklyPlanColor FromStorageKey(
        string? storageKey) =>
        storageKey?.Trim() switch
        {
            "Blue" => WeeklyPlanColor.Blue,
            "Green" => WeeklyPlanColor.Green,
            "Teal" => WeeklyPlanColor.Teal,
            "Purple" => WeeklyPlanColor.Purple,
            "Pink" => WeeklyPlanColor.Pink,
            "Orange" => WeeklyPlanColor.Orange,
            "Yellow" => WeeklyPlanColor.Yellow,
            "Gray" => WeeklyPlanColor.Gray,
            _ => DefaultColor
        };

    public static WeeklyPlanColor FromLegacyHex(
        string? colorHex) =>
        colorHex?.Trim().ToUpperInvariant() switch
        {
            "#059669" => WeeklyPlanColor.Green,
            "#0891B2" => WeeklyPlanColor.Teal,
            "#7C3AED" => WeeklyPlanColor.Purple,
            "#DC2626" => WeeklyPlanColor.Pink,
            "#EA580C" => WeeklyPlanColor.Orange,
            "#475569" => WeeklyPlanColor.Gray,
            _ => DefaultColor
        };
}
