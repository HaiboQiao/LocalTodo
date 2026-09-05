using System;
using System.Collections.Generic;
using System.Linq;
using LocalTodo.Models;

namespace LocalTodo.Helpers;

/// <summary>
/// 将连续日期范围映射到固定宽度的横向坐标，并为每项成果建立稳定轨道。
/// 该类不依赖 WPF，便于对日期边界、跨年和拖拽换算做单元测试。
/// </summary>
public static class AchievementTimelineLayoutEngine
{
    public const double CardHeight = 56;
    public const double MinimumCardWidth = 136;
    public const double MaximumCardWidth = 284;
    public const double CardEdgeMargin = 12;
    public const double LaneHeight = 94;
    public const double DurationTrackTop = 72;
    public const int WindowYearCount = 3;
    public const double ContentTop = 16;
    public const double MinimumContentHeight = 260;

    /// <summary>
    /// 将时间轴上的横坐标映射为成果开始日期。
    /// 开始日期使用某一天的左边界。
    /// </summary>
    public static DateTime XToStartDate(
        double x,
        int year,
        double timelineWidth) =>
        XToDate(
            x,
            new DateTime(year, 1, 1),
            new DateTime(year + 1, 1, 1),
            timelineWidth);

    public static DateTime XToStartDate(
        double x,
        DateTime rangeStart,
        DateTime rangeEndExclusive,
        double timelineWidth) =>
        XToDate(
            x,
            rangeStart,
            rangeEndExclusive,
            timelineWidth);

    /// <summary>
    /// 将成果右端点坐标映射为完成日期。
    /// 开始和结束都使用日期节点，同日成果因此会落在同一个坐标上。
    /// </summary>
    public static DateTime XToEndDate(
        double x,
        int year,
        double timelineWidth) =>
        XToDate(
            x,
            new DateTime(year, 1, 1),
            new DateTime(year + 1, 1, 1),
            timelineWidth);

    public static DateTime XToEndDate(
        double x,
        DateTime rangeStart,
        DateTime rangeEndExclusive,
        double timelineWidth) =>
        XToDate(
            x,
            rangeStart,
            rangeEndExclusive,
            timelineWidth);

    /// <summary>
    /// 统一的“日期 → 横坐标”换算。卡片锚点、时间线和拖拽共用此坐标系。
    /// </summary>
    public static double DateToX(
        DateTime date,
        DateTime rangeStart,
        DateTime rangeEndExclusive,
        double timelineWidth)
    {
        (DateTime safeStart, int daysInRange) =
            GetRangeMetrics(rangeStart, rangeEndExclusive);
        double safeWidth = Math.Max(1, timelineWidth);
        DateTime rangeEnd =
            rangeEndExclusive.Date.AddDays(-1);
        DateTime safeDate = date.Date < safeStart
            ? safeStart
            : date.Date > rangeEnd
                ? rangeEnd
                : date.Date;

        return (safeDate - safeStart).Days /
            (double)daysInRange *
            safeWidth;
    }

    /// <summary>
    /// 统一的“横坐标 → 日期”换算，最终吸附到完整日期。
    /// </summary>
    public static DateTime XToDate(
        double x,
        DateTime rangeStart,
        DateTime rangeEndExclusive,
        double timelineWidth)
    {
        (DateTime safeStart, int daysInRange) =
            GetRangeMetrics(rangeStart, rangeEndExclusive);
        double safeWidth = Math.Max(1, timelineWidth);
        int dayOffset = Math.Clamp(
            (int)Math.Round(
                Math.Clamp(x, 0, safeWidth) /
                safeWidth *
                daysInRange,
                MidpointRounding.AwayFromZero),
            0,
            daysInRange - 1);

        return safeStart.AddDays(dayOffset);
    }

    public static int DeltaXToDays(
        double deltaX,
        int year,
        double timelineWidth) =>
        DeltaXToDays(
            deltaX,
            new DateTime(year, 1, 1),
            new DateTime(year + 1, 1, 1),
            timelineWidth);

    public static int DeltaXToDays(
        double deltaX,
        DateTime rangeStart,
        DateTime rangeEndExclusive,
        double timelineWidth)
    {
        (_, int daysInRange) =
            GetRangeMetrics(rangeStart, rangeEndExclusive);

        return (int)Math.Round(
            deltaX /
            Math.Max(1, timelineWidth) *
            daysInRange,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 计算拖动指针接近可视区域边缘时的横向自动滚动量。
    /// 返回值越接近正负 maxStep，表示指针越靠近或越过对应边缘。
    /// </summary>
    public static double CalculateAutoScrollDelta(
        double pointerX,
        double viewportWidth,
        double edgeZone,
        double maxStep)
    {
        if (viewportWidth <= 0 ||
            edgeZone <= 0 ||
            maxStep <= 0)
        {
            return 0;
        }

        double safeEdgeZone = Math.Min(
            edgeZone,
            viewportWidth / 2);

        if (pointerX < safeEdgeZone)
        {
            double intensity = Math.Clamp(
                (safeEdgeZone - pointerX) /
                safeEdgeZone,
                0,
                1);
            return -maxStep * intensity;
        }

        double rightEdgeStart =
            viewportWidth - safeEdgeZone;

        if (pointerX > rightEdgeStart)
        {
            double intensity = Math.Clamp(
                (pointerX - rightEdgeStart) /
                safeEdgeZone,
                0,
                1);
            return maxStep * intensity;
        }

        return 0;
    }

    public static AchievementTimelineLayout Build(
        IEnumerable<AchievementRecord> records,
        int year,
        double timelineWidth)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (year < DateTime.MinValue.Year ||
            year >= DateTime.MaxValue.Year)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        DateTime yearStart = new(year, 1, 1);
        DateTime nextYearStart = yearStart.AddYears(1);
        return BuildRange(
            records,
            yearStart,
            nextYearStart,
            timelineWidth,
            yearStart,
            nextYearStart);
    }

    /// <summary>
    /// 构建“上一年 + 当前年 + 下一年”的连续滑动窗口。
    /// 每一年的可视宽度约等于当前视口宽度，跨年成果不会被截断成两张卡片。
    /// </summary>
    public static AchievementTimelineLayout BuildWindow(
        IEnumerable<AchievementRecord> records,
        int centerYear,
        double yearViewportWidth)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (centerYear <= DateTime.MinValue.Year ||
            centerYear >= DateTime.MaxValue.Year - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(centerYear));
        }

        DateTime rangeStart = new(centerYear - 1, 1, 1);
        DateTime selectedYearStart = new(centerYear, 1, 1);
        DateTime selectedYearEnd = selectedYearStart.AddYears(1);
        DateTime rangeEndExclusive = selectedYearEnd.AddYears(1);

        return BuildRange(
            records,
            rangeStart,
            rangeEndExclusive,
            Math.Max(1, yearViewportWidth) * WindowYearCount,
            selectedYearStart,
            selectedYearEnd);
    }

    private static AchievementTimelineLayout BuildRange(
        IEnumerable<AchievementRecord> records,
        DateTime rangeStart,
        DateTime rangeEndExclusive,
        double timelineWidth,
        DateTime selectedYearStart,
        DateTime selectedYearEnd)
    {
        double safeWidth = Math.Max(1, timelineWidth);
        DateTime rangeEnd = rangeEndExclusive.AddDays(-1);
        double daysInRange =
            (rangeEndExclusive - rangeStart).TotalDays;

        List<ProjectedRecord> projected = records
            .Where(record =>
                record.CompletedDate >= rangeStart &&
                record.PeriodStart.Date <= rangeEnd)
            .OrderBy(record => record.PeriodStart)
            .ThenBy(record => record.CompletedDate)
            .ThenBy(record => record.Title)
            .Select(record =>
            {
                DateTime visibleStart =
                    record.PeriodStart.Date < rangeStart
                        ? rangeStart
                        : record.PeriodStart.Date;
                DateTime visibleEnd =
                    record.CompletedDate > rangeEnd
                        ? rangeEnd
                        : record.CompletedDate;
                double trackLeft = DateToX(
                    visibleStart,
                    rangeStart,
                    rangeEndExclusive,
                    safeWidth);
                double trackRight = DateToX(
                    visibleEnd,
                    rangeStart,
                    rangeEndExclusive,
                    safeWidth);
                double trackWidth =
                    Math.Max(0, trackRight - trackLeft);
                double trackCenter =
                    (trackLeft + trackRight) / 2;

                DateTime anchorDate = visibleStart.AddDays(
                    (visibleEnd - visibleStart).Days / 2);
                DateTime anchorYearStart =
                    new(anchorDate.Year, 1, 1);
                DateTime anchorYearEnd =
                    anchorYearStart.AddYears(1);
                double yearLeft = BoundaryToX(
                    anchorYearStart < rangeStart
                        ? rangeStart
                        : anchorYearStart,
                    rangeStart,
                    daysInRange,
                    safeWidth);
                double yearRight = BoundaryToX(
                    anchorYearEnd > rangeEndExclusive
                        ? rangeEndExclusive
                        : anchorYearEnd,
                    rangeStart,
                    daysInRange,
                    safeWidth);
                double availableWidth =
                    Math.Max(1, yearRight - yearLeft);
                bool isSingleDay = visibleStart == visibleEnd;
                double desiredCardWidth =
                    CalculateDesiredCardWidth(
                        record,
                        isSingleDay);
                double cardWidth = Math.Min(
                    desiredCardWidth,
                    Math.Max(
                        1,
                        availableWidth -
                        CardEdgeMargin * 2));
                double minimumCardLeft =
                    yearLeft + CardEdgeMargin;
                double maximumCardLeft =
                    yearRight - CardEdgeMargin - cardWidth;
                double preferredCardLeft =
                    trackCenter - cardWidth / 2;
                double cardLeft = maximumCardLeft >= minimumCardLeft
                    ? Math.Clamp(
                        preferredCardLeft,
                        minimumCardLeft,
                        maximumCardLeft)
                    : yearLeft +
                        (availableWidth - cardWidth) / 2;
                double pointerCenter = Math.Clamp(
                    trackCenter,
                    cardLeft + 20,
                    cardLeft + cardWidth - 20);

                return new ProjectedRecord(
                    record,
                    trackLeft,
                    trackWidth,
                    cardLeft,
                    cardWidth,
                    pointerCenter - 6,
                    isSingleDay);
            })
            .ToList();

        List<AchievementTimelinePlacement> placements = [];

        for (int index = 0;
             index < projected.Count;
             index++)
        {
            ProjectedRecord item = projected[index];

            placements.Add(
                new AchievementTimelinePlacement(
                    item.Record,
                    safeWidth,
                    item.TrackLeft,
                    item.TrackWidth,
                    item.CardLeft,
                    item.CardWidth,
                    item.PointerLeft,
                    DurationTrackTop,
                    ContentTop +
                    index * LaneHeight,
                    index,
                    item.IsSingleDay,
                    item.Record.PeriodStart.Date < rangeStart,
                    item.Record.CompletedDate > rangeEnd));
        }

        List<AchievementTimelineMonth> months = [];
        bool isMultiYear =
            rangeEndExclusive.Year - rangeStart.Year > 1;

        for (DateTime monthStart = rangeStart;
             monthStart < rangeEndExclusive;
             monthStart = monthStart.AddMonths(1))
        {
            DateTime monthEnd = monthStart.AddMonths(1);
            double left = BoundaryToX(
                monthStart,
                rangeStart,
                daysInRange,
                safeWidth);
            double width = BoundaryToX(
                monthEnd,
                rangeStart,
                daysInRange,
                safeWidth) - left;

            months.Add(
                new AchievementTimelineMonth(
                    monthStart.Year,
                    monthStart.Month,
                    isMultiYear && monthStart.Month == 1
                        ? $"{monthStart.Year}年 1月"
                        : $"{monthStart.Month}月",
                    left,
                    width,
                    monthStart.Month == 1));
        }

        double contentHeight = Math.Max(
            MinimumContentHeight,
            ContentTop +
            Math.Max(1, projected.Count) *
            LaneHeight + 12);

        double selectedYearLeft = BoundaryToX(
            selectedYearStart,
            rangeStart,
            daysInRange,
            safeWidth);
        double selectedYearWidth = BoundaryToX(
            selectedYearEnd,
            rangeStart,
            daysInRange,
            safeWidth) - selectedYearLeft;

        return new AchievementTimelineLayout(
            placements,
            months,
            projected.Count,
            contentHeight,
            safeWidth,
            rangeStart,
            rangeEndExclusive,
            selectedYearLeft,
            selectedYearWidth);
    }

    private static (DateTime RangeStart, int DaysInRange)
        GetRangeMetrics(
            DateTime rangeStart,
            DateTime rangeEndExclusive)
    {
        DateTime safeStart = rangeStart.Date;
        DateTime safeEnd = rangeEndExclusive.Date;

        if (safeEnd <= safeStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rangeEndExclusive));
        }

        return (
            safeStart,
            (safeEnd - safeStart).Days);
    }

    private static double BoundaryToX(
        DateTime boundary,
        DateTime rangeStart,
        double daysInRange,
        double timelineWidth) =>
        Math.Clamp(
            (boundary.Date - rangeStart.Date).TotalDays,
            0,
            daysInRange) /
        daysInRange *
        timelineWidth;

    /// <summary>
    /// 卡片宽度取标题行与日期行中较宽的一行，并限制合理上下界。
    /// 估算只负责稳定布局；WPF 仍会对极长文本执行省略显示。
    /// </summary>
    private static double CalculateDesiredCardWidth(
        AchievementRecord record,
        bool isSingleDay)
    {
        const double horizontalPadding = 26;
        const double titleMaximum = 160;
        const double categoryMaximum = 72;
        const double titleCategoryGap = 8;
        const double categoryHorizontalPadding = 14;

        double titleWidth = Math.Min(
            titleMaximum,
            EstimateTextWidth(
                record.Title,
                fontSize: 15,
                isSemibold: true));
        double categoryTextWidth = Math.Min(
            categoryMaximum,
            EstimateTextWidth(
                record.CategoryText,
                fontSize: 11,
                isSemibold: false));
        double headingWidth =
            titleWidth +
            titleCategoryGap +
            categoryTextWidth +
            categoryHorizontalPadding;
        string dateText = isSingleDay
            ? record.PeriodStart.ToString("yyyy.MM.dd")
            : $"{record.PeriodStart:yyyy.MM.dd}  →  {record.CompletedDate:yyyy.MM.dd}";
        double dateWidth = EstimateTextWidth(
            dateText,
            fontSize: 13,
            isSemibold: false);

        return Math.Clamp(
            Math.Ceiling(
                Math.Max(headingWidth, dateWidth) +
                horizontalPadding),
            MinimumCardWidth,
            MaximumCardWidth);
    }

    private static double EstimateTextWidth(
        string? text,
        double fontSize,
        bool isSemibold)
    {
        double units = 0;

        foreach (char character in text ?? string.Empty)
        {
            units += character switch
            {
                _ when char.IsWhiteSpace(character) => 0.34,
                _ when character > 0x7F => 1.0,
                _ when char.IsUpper(character) => 0.66,
                _ when char.IsDigit(character) => 0.58,
                '_' => 0.56,
                _ when char.IsPunctuation(character) => 0.42,
                _ => 0.54
            };
        }

        return units *
            fontSize *
            (isSemibold ? 1.04 : 1.0);
    }

    private sealed record ProjectedRecord(
        AchievementRecord Record,
        double TrackLeft,
        double TrackWidth,
        double CardLeft,
        double CardWidth,
        double PointerLeft,
        bool IsSingleDay);
}

public sealed record AchievementTimelinePlacement(
    AchievementRecord Record,
    double CanvasWidth,
    double Left,
    double Width,
    double CardLeft,
    double CardWidth,
    double PointerLeft,
    double TrackTop,
    double Top,
    int TrackIndex,
    bool IsSingleDay,
    bool StartsBeforeYear,
    bool EndsAfterYear);

public sealed record AchievementTimelineMonth(
    int Year,
    int Month,
    string Title,
    double Left,
    double Width,
    bool IsYearStart);

public sealed record AchievementTimelineLayout(
    IReadOnlyList<AchievementTimelinePlacement> Placements,
    IReadOnlyList<AchievementTimelineMonth> Months,
    int TrackCount,
    double ContentHeight,
    double TimelineWidth,
    DateTime RangeStart,
    DateTime RangeEndExclusive,
    double SelectedYearLeft,
    double SelectedYearWidth);
