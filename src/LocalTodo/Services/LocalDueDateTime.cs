using System;
using System.Globalization;

namespace LocalTodo.Services;

/// <summary>
/// 截止时间的本地钟表语义辅助方法。
///
/// TaskItem.DueAt 为兼容现有模型继续使用 DateTimeOffset，但其中的
/// 年月日时分是权威值；Offset 只是当前时区下用于提醒计算的派生值。
/// </summary>
public static class LocalDueDateTime
{
    private const string DatabaseFormat =
        "yyyy-MM-dd'T'HH:mm:ss.fffffff";

    public static DateTime GetWallClock(
        DateTimeOffset dueAt)
    {
        return DateTime.SpecifyKind(
            dueAt.DateTime,
            DateTimeKind.Unspecified);
    }

    public static string FormatForDatabase(
        DateTimeOffset dueAt)
    {
        return GetWallClock(dueAt)
            .ToString(
                DatabaseFormat,
                CultureInfo.InvariantCulture);
    }

    public static DateTime ParseDatabaseValue(
        string value)
    {
        return DateTime.SpecifyKind(
            DateTime.ParseExact(
                value,
                DatabaseFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None),
            DateTimeKind.Unspecified);
    }
}
