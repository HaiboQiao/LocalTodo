using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalTodo.Models;

/// <summary>
/// 每周计划中的一个固定时间段。
/// </summary>
public sealed class WeeklyPlanItem
    : ObservableObject
{
    public string Id
    { get; set; } = string.Empty;

    private WeeklyDay
        _day = WeeklyDay.Monday;

    public WeeklyDay Day
    {
        get => _day;
        set
        {
            if (SetProperty(ref _day, value))
            {
                OnPropertyChanged(nameof(DayTitle));
            }
        }
    }

    private int
        _startMinutes = 540;

    public int StartMinutes
    {
        get => _startMinutes;
        set
        {
            if (!SetProperty(
                    ref _startMinutes,
                    value))
            {
                return;
            }

            NotifyTimeLayoutChanged();
        }
    }

    private int
        _endMinutes = 600;

    public int EndMinutes
    {
        get => _endMinutes;
        set
        {
            if (!SetProperty(
                    ref _endMinutes,
                    value))
            {
                return;
            }

            NotifyTimeLayoutChanged();
        }
    }

    public string Title
    { get; set; } = string.Empty;

    public string Description
    { get; set; } = string.Empty;

    public WeeklyPlanColor Color
    { get; set; } = WeeklyPlanColorStorage.DefaultColor;

    public DateTimeOffset CreatedAt
    { get; set; }

    public DateTimeOffset UpdatedAt
    { get; set; }

    public string DayTitle =>
        Day.GetTitle();

    public string TimeRangeText =>
        $"{FormatMinutes(StartMinutes)}–" +
        FormatMinutes(EndMinutes);

    public string StartTimeText =>
        FormatMinutes(StartMinutes);

    public string EndTimeText =>
        FormatMinutes(EndMinutes);

    private static string FormatMinutes(
        int minutes)
    {
        if (minutes == 1440)
        {
            return "24:00";
        }

        return $"{minutes / 60:00}:" +
               $"{minutes % 60:00}";
    }

    public void SetTimeRange(
        int startMinutes,
        int endMinutes)
    {
        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
    }

    private void NotifyTimeLayoutChanged()
    {
        OnPropertyChanged(nameof(TimeRangeText));
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(EndTimeText));
    }
}

public static class WeeklyPlanRules
{
    public const int SnapMinutes = 15;
    public const int MinimumDurationMinutes = 15;
}

public enum WeeklyDay
{
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Sunday = 7
}

public static class WeeklyDayExtensions
{
    public static string GetTitle(
        this WeeklyDay day) =>
        day switch
        {
            WeeklyDay.Monday => "周一",
            WeeklyDay.Tuesday => "周二",
            WeeklyDay.Wednesday => "周三",
            WeeklyDay.Thursday => "周四",
            WeeklyDay.Friday => "周五",
            WeeklyDay.Saturday => "周六",
            WeeklyDay.Sunday => "周日",
            _ => "未指定"
        };
}
