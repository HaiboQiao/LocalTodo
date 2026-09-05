using System;
using System.Collections.Generic;
using System.Linq;
using LocalTodo.Models;

namespace LocalTodo.Helpers;

/// <summary>
/// 每周计划的纯布局计算。
/// 不依赖 WPF 控件，便于独立测试时间范围、坐标和重叠分栏。
/// </summary>
public static class WeeklyPlanLayoutEngine
{
    public const int DefaultDisplayStartMinutes =
        7 * 60;

    public const int DefaultDisplayEndMinutes =
        23 * 60;

    public const double DayInset = 4;
    public const double LaneGap = 3;
    public const double CompactHeightThreshold = 48;

    public static WeeklyPlanDisplayRange
        CalculateDisplayRange(
            IEnumerable<WeeklyPlanItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        WeeklyPlanItem[] validItems =
            items.Where(item =>
                    item.StartMinutes >= 0 &&
                    item.StartMinutes < 1440 &&
                    item.EndMinutes > item.StartMinutes &&
                    item.EndMinutes <= 1440)
                .ToArray();

        if (validItems.Length == 0)
        {
            return new WeeklyPlanDisplayRange(
                DefaultDisplayStartMinutes,
                DefaultDisplayEndMinutes);
        }

        int earliest =
            validItems.Min(item =>
                item.StartMinutes);
        int latest =
            validItems.Max(item =>
                item.EndMinutes);

        int paddedStart =
            Math.Max(0, earliest - 60);
        int paddedEnd =
            Math.Min(1440, latest + 60);

        int displayStart =
            paddedStart / 60 * 60;
        int displayEnd =
            (int)Math.Ceiling(
                paddedEnd / 60d) * 60;

        displayEnd =
            Math.Clamp(displayEnd, 60, 1440);

        if (displayEnd <= displayStart)
        {
            displayEnd =
                Math.Min(1440, displayStart + 60);
        }

        return new WeeklyPlanDisplayRange(
            displayStart,
            displayEnd);
    }

    public static WeeklyPlanLayoutSnapshot
        CreateSnapshot(
            WeeklyPlanDisplayRange range,
            double canvasWidth,
            double canvasHeight)
    {
        double safeWidth =
            Math.Max(0, canvasWidth);
        double safeHeight =
            Math.Max(0, canvasHeight);

        return new WeeklyPlanLayoutSnapshot(
            range,
            safeWidth,
            safeHeight,
            range.TotalMinutes <= 0
                ? 0
                : safeHeight / range.TotalMinutes,
            safeWidth / 7d);
    }

    public static int SnapMinutes(
        double minutes) =>
        (int)Math.Round(
            minutes /
            WeeklyPlanRules.SnapMinutes,
            MidpointRounding.AwayFromZero) *
        WeeklyPlanRules.SnapMinutes;

    public static double MinutesToY(
        int minutes,
        WeeklyPlanLayoutSnapshot snapshot) =>
        (minutes - snapshot.Range.StartMinutes) *
        snapshot.PixelsPerMinute;

    public static int YToMinutes(
        double y,
        WeeklyPlanLayoutSnapshot snapshot) =>
        SnapMinutes(
            snapshot.Range.StartMinutes +
            (snapshot.PixelsPerMinute <= 0
                ? 0
                : y / snapshot.PixelsPerMinute));

    public static WeeklyDay XToDay(
        double x,
        WeeklyPlanLayoutSnapshot snapshot)
    {
        if (snapshot.DayWidth <= 0)
        {
            return WeeklyDay.Monday;
        }

        int dayIndex =
            Math.Clamp(
                (int)Math.Floor(x / snapshot.DayWidth),
                0,
                6);

        return (WeeklyDay)(dayIndex + 1);
    }

    public static IReadOnlyList<WeeklyPlanGridLine>
        CreateGridLines(
            WeeklyPlanLayoutSnapshot snapshot)
    {
        List<WeeklyPlanGridLine> result = [];

        int firstMarker =
            (int)Math.Ceiling(
                snapshot.Range.StartMinutes / 30d) * 30;

        for (int minutes = firstMarker;
             minutes <= snapshot.Range.EndMinutes;
             minutes += 30)
        {
            bool isHour =
                minutes % 60 == 0;
            double top =
                MinutesToY(minutes, snapshot);
            double labelTop =
                Math.Clamp(
                    top - 7,
                    0,
                    Math.Max(
                        0,
                        snapshot.CanvasHeight - 14));

            result.Add(
                new WeeklyPlanGridLine(
                    minutes,
                    FormatMinutes(minutes),
                    top,
                    labelTop,
                    isHour));
        }

        return result;
    }

    public static IReadOnlyList<WeeklyPlanCardPlacement>
        CalculateCardPlacements(
            IEnumerable<WeeklyPlanItem> items,
            WeeklyPlanLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(items);

        List<WeeklyPlanCardPlacement> result = [];

        foreach (IGrouping<WeeklyDay, WeeklyPlanItem> dayGroup in
                 items.GroupBy(item => item.Day))
        {
            WeeklyPlanItem[] sorted =
                dayGroup
                    .OrderBy(item => item.StartMinutes)
                    .ThenBy(item => item.EndMinutes)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .ToArray();

            int index = 0;

            while (index < sorted.Length)
            {
                List<WeeklyPlanItem> cluster =
                    [sorted[index]];
                int clusterEnd =
                    sorted[index].EndMinutes;
                index++;

                while (index < sorted.Length &&
                       sorted[index].StartMinutes < clusterEnd)
                {
                    cluster.Add(sorted[index]);
                    clusterEnd =
                        Math.Max(
                            clusterEnd,
                            sorted[index].EndMinutes);
                    index++;
                }

                AddClusterPlacements(
                    cluster,
                    snapshot,
                    result);
            }
        }

        return result;
    }

    private static void AddClusterPlacements(
        IReadOnlyList<WeeklyPlanItem> cluster,
        WeeklyPlanLayoutSnapshot snapshot,
        ICollection<WeeklyPlanCardPlacement> result)
    {
        List<int> laneEnds = [];
        List<(WeeklyPlanItem Item, int Lane)> assignments = [];

        foreach (WeeklyPlanItem item in cluster)
        {
            int lane = -1;

            for (int index = 0;
                 index < laneEnds.Count;
                 index++)
            {
                if (laneEnds[index] <= item.StartMinutes)
                {
                    lane = index;
                    break;
                }
            }

            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(item.EndMinutes);
            }
            else
            {
                laneEnds[lane] = item.EndMinutes;
            }

            assignments.Add((item, lane));
        }

        int laneCount =
            Math.Max(1, laneEnds.Count);
        double availableWidth =
            Math.Max(
                0,
                snapshot.DayWidth -
                DayInset * 2 -
                LaneGap * (laneCount - 1));
        double laneWidth =
            laneCount == 0
                ? 0
                : availableWidth / laneCount;

        foreach ((WeeklyPlanItem item, int lane) in assignments)
        {
            double height =
                Math.Max(
                    0,
                    (item.EndMinutes - item.StartMinutes) *
                    snapshot.PixelsPerMinute);
            double left =
                ((int)item.Day - 1) * snapshot.DayWidth +
                DayInset +
                lane * (laneWidth + LaneGap);

            result.Add(
                new WeeklyPlanCardPlacement(
                    item,
                    left,
                    MinutesToY(
                        item.StartMinutes,
                        snapshot),
                    laneWidth,
                    height,
                    lane,
                    laneCount,
                    height < CompactHeightThreshold));
        }
    }

    public static string FormatMinutes(
        int minutes) =>
        minutes == 1440
            ? "24:00"
            : $"{minutes / 60:00}:" +
              $"{minutes % 60:00}";
}

public readonly record struct WeeklyPlanDisplayRange(
    int StartMinutes,
    int EndMinutes)
{
    public int TotalMinutes =>
        EndMinutes - StartMinutes;
}

public readonly record struct WeeklyPlanLayoutSnapshot(
    WeeklyPlanDisplayRange Range,
    double CanvasWidth,
    double CanvasHeight,
    double PixelsPerMinute,
    double DayWidth);

public sealed record WeeklyPlanGridLine(
    int Minutes,
    string Title,
    double Top,
    double LabelTop,
    bool IsHour);

public sealed record WeeklyPlanCardPlacement(
    WeeklyPlanItem Item,
    double Left,
    double Top,
    double Width,
    double Height,
    int LaneIndex,
    int LaneCount,
    bool IsCompact);
