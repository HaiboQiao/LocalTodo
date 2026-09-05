using System;
using System.Globalization;

namespace LocalTodo.Services;

/// <summary>
/// 提供中国农历日期的本地离线显示。
///
/// 使用 .NET 自带的 ChineseLunisolarCalendar，
/// 不需要第三方 NuGet 包，也不访问互联网。
/// </summary>
public sealed class LunarCalendarService
{
    private readonly ChineseLunisolarCalendar
        _calendar =
            new();

    private static readonly string[]
        MonthNames =
        [
            string.Empty,
            "正月",
            "二月",
            "三月",
            "四月",
            "五月",
            "六月",
            "七月",
            "八月",
            "九月",
            "十月",
            "冬月",
            "腊月"
        ];

    private static readonly string[]
        DayNames =
        [
            string.Empty,
            "初一",
            "初二",
            "初三",
            "初四",
            "初五",
            "初六",
            "初七",
            "初八",
            "初九",
            "初十",
            "十一",
            "十二",
            "十三",
            "十四",
            "十五",
            "十六",
            "十七",
            "十八",
            "十九",
            "二十",
            "廿一",
            "廿二",
            "廿三",
            "廿四",
            "廿五",
            "廿六",
            "廿七",
            "廿八",
            "廿九",
            "三十"
        ];

    /// <summary>
    /// ChineseLunisolarCalendar 支持的最早公历日期。
    /// </summary>
    public DateTime MinimumSupportedDate =>
        _calendar
            .MinSupportedDateTime
            .Date;

    /// <summary>
    /// ChineseLunisolarCalendar 支持的最晚公历日期。
    /// </summary>
    public DateTime MaximumSupportedDate =>
        _calendar
            .MaxSupportedDateTime
            .Date;

    /// <summary>
    /// 返回日期格右上角使用的农历短文字。
    ///
    /// 农历初一显示月份，例如“八月”；
    /// 其他日期显示“初二”“十五”“廿三”等；
    /// 闰月初一显示“闰六月”等。
    /// </summary>
    public string GetDisplayText(
        DateTime solarDate)
    {
        DateTime date =
            solarDate.Date;

        if (date <
                MinimumSupportedDate ||
            date >
                MaximumSupportedDate)
        {
            return string.Empty;
        }

        int lunarYear =
            _calendar.GetYear(
                date);

        int rawLunarMonth =
            _calendar.GetMonth(
                date);

        int lunarDay =
            _calendar.GetDayOfMonth(
                date);

        int leapMonthPosition =
            _calendar.GetLeapMonth(
                lunarYear);

        bool isLeapMonth =
            leapMonthPosition > 0 &&
            rawLunarMonth ==
                leapMonthPosition;

        int normalizedLunarMonth =
            rawLunarMonth;

        /*
         * ChineseLunisolarCalendar 在有闰月的年份中，
         * 会把闰月作为额外的一个月插入。
         *
         * 例如闰六月时：
         * 6  = 六月
         * 7  = 闰六月
         * 8  = 七月
         *
         * 界面显示时需要把 7 映射回六月，
         * 把 8 及之后的月份向前减一。
         */
        if (leapMonthPosition > 0 &&
            rawLunarMonth >=
                leapMonthPosition)
        {
            normalizedLunarMonth--;
        }

        if (normalizedLunarMonth < 1 ||
            normalizedLunarMonth >=
                MonthNames.Length)
        {
            return string.Empty;
        }

        if (lunarDay == 1)
        {
            string leapPrefix =
                isLeapMonth
                    ? "闰"
                    : string.Empty;

            return
                $"{leapPrefix}" +
                $"{MonthNames[normalizedLunarMonth]}";
        }

        if (lunarDay < 1 ||
            lunarDay >=
                DayNames.Length)
        {
            return string.Empty;
        }

        return DayNames[lunarDay];
    }
}
