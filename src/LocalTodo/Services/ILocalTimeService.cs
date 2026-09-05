using System;
using Microsoft.Win32;

namespace LocalTodo.Services;

/// <summary>
/// 统一处理用户本地日历时间与绝对时间之间的转换。
/// </summary>
public interface ILocalTimeService
{
    TimeZoneInfo TimeZone
    { get; }

    /// <summary>
    /// 将绝对时刻转换为当前服务时区中的本地钟表时间。
    /// 返回值的 Kind 始终为 Unspecified。
    /// </summary>
    DateTime ToLocalDateTime(
        DateTimeOffset instant);

    /// <summary>
    /// 将本地钟表时间解析为绝对时刻。
    ///
    /// DST 开始导致时间不存在时，按缺口长度向后顺延；
    /// DST 结束导致时间重复时，选择较晚的那个绝对时刻。
    /// </summary>
    DateTimeOffset ResolveLocalDateTime(
        DateTime localDateTime);
}

/// <summary>
/// 使用指定 TimeZoneInfo 的本地时间实现。
/// </summary>
public sealed class LocalTimeService :
    ILocalTimeService
{
    public static ILocalTimeService System
    { get; } =
        SystemLocalTimeService.Instance;

    public TimeZoneInfo TimeZone
    { get; }

    public LocalTimeService(
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(
            timeZone);

        TimeZone =
            timeZone;
    }

    public DateTime ToLocalDateTime(
        DateTimeOffset instant)
    {
        DateTime localDateTime =
            TimeZoneInfo.ConvertTime(
                    instant,
                    TimeZone)
                .DateTime;

        return DateTime.SpecifyKind(
            localDateTime,
            DateTimeKind.Unspecified);
    }

    public DateTimeOffset ResolveLocalDateTime(
        DateTime localDateTime)
    {
        DateTime normalized =
            DateTime.SpecifyKind(
                localDateTime,
                DateTimeKind.Unspecified);

        if (TimeZone.IsInvalidTime(
                normalized))
        {
            normalized =
                MoveAcrossInvalidTimeGap(
                    normalized);
        }

        TimeSpan offset;

        if (TimeZone.IsAmbiguousTime(
                normalized))
        {
            TimeSpan[] offsets =
                TimeZone.GetAmbiguousTimeOffsets(
                    normalized);

            /*
             * UTC = 本地时间 - Offset。
             * 因而选择数值更小的 Offset，得到较晚的绝对时刻。
             */
            offset =
                offsets[0] <= offsets[1]
                    ? offsets[0]
                    : offsets[1];
        }
        else
        {
            offset =
                TimeZone.GetUtcOffset(
                    normalized);
        }

        return new DateTimeOffset(
            normalized,
            offset);
    }

    private DateTime MoveAcrossInvalidTimeGap(
        DateTime invalidLocalDateTime)
    {
        DateTime before =
            invalidLocalDateTime;

        DateTime after =
            invalidLocalDateTime;

        /*
         * 现代时区的 DST 缺口通常为 30、60 或 120 分钟。
         * 这里以分钟寻找缺口两端，并设置一天上限，既覆盖历史规则，
         * 也避免异常 TimeZoneInfo 造成无限循环。
         */
        for (int minute = 0;
             minute < 24 * 60;
             minute++)
        {
            before =
                before.AddMinutes(-1);

            if (!TimeZone.IsInvalidTime(
                    before))
            {
                break;
            }
        }

        for (int minute = 0;
             minute < 24 * 60;
             minute++)
        {
            after =
                after.AddMinutes(1);

            if (!TimeZone.IsInvalidTime(
                    after))
            {
                break;
            }
        }

        if (TimeZone.IsInvalidTime(before) ||
            TimeZone.IsInvalidTime(after))
        {
            throw new InvalidOperationException(
                $"无法解析时区 {TimeZone.Id} 中不存在的本地时间 " +
                $"{invalidLocalDateTime:O}。");
        }

        TimeSpan gap =
            TimeZone.GetUtcOffset(after) -
            TimeZone.GetUtcOffset(before);

        if (gap <= TimeSpan.Zero)
        {
            /*
             * 防御不完整或非典型的时区规则。至少移动到已经确认有效
             * 的缺口末端，绝不继续保留无效本地时间。
             */
            return after;
        }

        DateTime adjusted =
            invalidLocalDateTime + gap;

        return TimeZone.IsInvalidTime(
                adjusted)
            ? after
            : adjusted;
    }
}

/// <summary>
/// 动态读取 Windows 当前时区。
/// 切换系统时间或时区后清除 TimeZoneInfo 缓存，后续截止时间、
/// 分组和提醒无需重启程序即可采用新时区，同时仍保留本地钟表值。
/// </summary>
public sealed class SystemLocalTimeService :
    ILocalTimeService
{
    public static SystemLocalTimeService Instance
    { get; } = new();

    private SystemLocalTimeService()
    {
        SystemEvents.TimeChanged +=
            OnSystemTimeChanged;
    }

    public TimeZoneInfo TimeZone =>
        TimeZoneInfo.Local;

    public DateTime ToLocalDateTime(
        DateTimeOffset instant)
    {
        return CreateCurrentService()
            .ToLocalDateTime(
                instant);
    }

    public DateTimeOffset ResolveLocalDateTime(
        DateTime localDateTime)
    {
        return CreateCurrentService()
            .ResolveLocalDateTime(
                localDateTime);
    }

    private static LocalTimeService CreateCurrentService()
    {
        return new LocalTimeService(
            TimeZoneInfo.Local);
    }

    private static void OnSystemTimeChanged(
        object? sender,
        EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
    }
}
