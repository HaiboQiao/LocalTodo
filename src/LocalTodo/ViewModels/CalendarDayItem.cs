using System;
using System.Collections.Generic;
using System.Linq;
using LocalTodo.Models;

namespace LocalTodo.ViewModels;

/// <summary>
/// 月视图中的一个日期格。
/// </summary>
public sealed class CalendarDayItem
{
    private const int MaximumVisibleTaskCount =
        3;

    /// <summary>
    /// 当前日期格代表的本地日期。
    /// </summary>
    public required DateTime Date
    { get; init; }

    /// <summary>
    /// 当前日期是否属于正在查看的月份。
    /// </summary>
    public required bool IsCurrentMonth
    { get; init; }

    /// <summary>
    /// 日期格左上角显示的阳历文字。
    /// </summary>
    public required string SolarDayText
    { get; init; }

    /// <summary>
    /// 日期格右上角显示的农历文字。
    /// </summary>
    public required string LunarText
    { get; init; }

    /// <summary>
    /// 当天全部任务。
    /// </summary>
    public IReadOnlyList<TaskItem> Tasks
    { get; init; } = [];

    public required bool IsToday
    { get; init; }

    public bool IsWeekend =>
        Date.DayOfWeek is
            DayOfWeek.Saturday or
            DayOfWeek.Sunday;

    public string FullDateText =>
        Date.ToString(
            "yyyy年M月d日 dddd");

    public int TaskCount =>
        Tasks.Count;

    public bool HasTasks =>
        Tasks.Count > 0;

    /// <summary>
    /// 日期格中最多直接显示三条任务。
    /// </summary>
    public IReadOnlyList<TaskItem>
        VisibleTasks =>
            Tasks
                .Take(
                    MaximumVisibleTaskCount)
                .ToArray();

    public int HiddenTaskCount =>
        Math.Max(
            0,
            Tasks.Count -
            MaximumVisibleTaskCount);

    public bool HasHiddenTasks =>
        HiddenTaskCount > 0;

    public string MoreTasksText =>
        $"还有 {HiddenTaskCount} 项";
}
