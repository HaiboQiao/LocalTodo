using System;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 计算循环任务紧接着的下一周期截止时间。
///
/// 核心规则：
///
/// 每完成或删除一个循环周期，
/// 永远只向前推进一个周期。
///
/// 即使当前任务已经过期多个周期，
/// 也绝对不会自动跳过中间周期。
///
/// 例如：
///
/// 每天循环：
/// 8月8日 -> 8月9日
///
/// 即使今天已经是8月12日，
/// 完成8月8日这一期以后，
/// 新任务仍然是8月9日，
/// 而不是直接跳到8月13日。
/// </summary>
public static class TaskRecurrenceCalculator
{
    /// <summary>
    /// 根据当前循环任务，
    /// 计算紧接着的下一周期。
    ///
    /// 注意：
    ///
    /// 这里不关心用户什么时候点击完成。
    /// 下一周期只取决于：
    ///
    /// 1. 当前任务的截止日期/时间；
    /// 2. 当前任务的循环方式。
    /// </summary>
    public static DateTimeOffset?
        GetNextDueAt(
            TaskItem task,
            ILocalTimeService? localTimeService = null)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        /*
         * 没有截止日期，
         * 或者本身不是循环任务，
         * 就不存在下一周期。
         */
        if (!task.DueAt.HasValue ||
            task.RepeatType ==
                TaskRepeatType.None)
        {
            return null;
        }

        if (!Enum.IsDefined(
                typeof(TaskRepeatType),
                task.RepeatType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(task.RepeatType),
                task.RepeatType,
                "无法识别任务循环方式。");
        }

        /*
         * 循环的“日 / 周 / 月 / 年”
         * 都属于用户本地日历概念。
         *
         * 因此先转换成本地时间，
         * 然后只向前推进一个周期。
         */
        ILocalTimeService resolvedLocalTimeService =
            localTimeService ??
            LocalTimeService.System;

        DateTime currentLocal =
            LocalDueDateTime.GetWallClock(
                task.DueAt.Value);

        /*
         * =====================================
         * 最关键的规则
         * =====================================
         *
         * 这里只调用一次 AdvanceOnePeriod。
         *
         * 不和“当前时间”比较；
         * 不和“完成时间”比较；
         * 不使用 while；
         * 不使用 for；
         * 不自动寻找未来日期。
         *
         * 当前周期过期多久都没有关系。
         *
         * 每完成一次，
         * 只进入紧接着的下一周期。
         */
        return AdvanceOnePeriod(
            currentLocal,
            task.RepeatType,
            task.RecurrenceAnchorMonth,
            task.RecurrenceAnchorDay,
            resolvedLocalTimeService);
    }

    /// <summary>
    /// 将当前截止时间向前推进一个周期。
    /// </summary>
    private static DateTimeOffset
        AdvanceOnePeriod(
            DateTime currentLocal,
            TaskRepeatType repeatType,
            int? recurrenceAnchorMonth,
            int? recurrenceAnchorDay,
            ILocalTimeService localTimeService)
    {
        /*
         * DateTimeOffset.DateTime 得到本地墙上时间。
         *
         * 将 Kind 明确设为 Unspecified，
         * 后面重新根据下一日期计算本地 UTC Offset。
         */
        DateTime current =
            DateTime.SpecifyKind(
                currentLocal,
                DateTimeKind.Unspecified);

        DateTime next =
            repeatType switch
            {
                /*
                 * 每天：
                 *
                 * 8月8日 -> 8月9日
                 */
                TaskRepeatType.Daily =>
                    current.AddDays(1),

                /*
                 * 每周：
                 *
                 * 8月2日 -> 8月9日
                 */
                TaskRepeatType.Weekly =>
                    current.AddDays(7),

                /*
                 * 每月：
                 *
                 * 7月9日 -> 8月9日
                 */
                TaskRepeatType.Monthly =>
                    GetNextMonthlyDateTime(
                        current,
                        recurrenceAnchorDay ??
                            current.Day),

                /*
                 * 每年：
                 *
                 * 2026年8月9日
                 * ->
                 * 2027年8月9日
                 */
                TaskRepeatType.Yearly =>
                    GetNextYearlyDateTime(
                        current,
                        recurrenceAnchorMonth ??
                            current.Month,
                        recurrenceAnchorDay ??
                            current.Day),

                /*
                 * 每周工作日：
                 *
                 * 周五 -> 下周一
                 *
                 * 周一 -> 周二
                 */
                TaskRepeatType.Weekdays =>
                    GetNextWeekday(
                        current),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(repeatType),
                        repeatType,
                        "无法识别任务循环方式。")
            };

        /*
         * 使用“下一周期日期”对应的本地 UTC Offset。
         *
         * 这样在存在夏令时的系统中，
         * 也不会错误继承上一周期的 UTC Offset。
         */
        return localTimeService
            .ResolveLocalDateTime(
                next);
    }

    private static DateTime GetNextMonthlyDateTime(
        DateTime current,
        int anchorDay)
    {
        DateTime targetMonth =
            new DateTime(
                    current.Year,
                    current.Month,
                    1,
                    0,
                    0,
                    0,
                    DateTimeKind.Unspecified)
                .AddMonths(1);

        int day =
            Math.Min(
                anchorDay,
                DateTime.DaysInMonth(
                    targetMonth.Year,
                    targetMonth.Month));

        return targetMonth
            .AddDays(
                day - 1)
            .Add(
                current.TimeOfDay);
    }

    private static DateTime GetNextYearlyDateTime(
        DateTime current,
        int anchorMonth,
        int anchorDay)
    {
        int targetYear =
            checked(current.Year + 1);

        int day =
            Math.Min(
                anchorDay,
                DateTime.DaysInMonth(
                    targetYear,
                    anchorMonth));

        return new DateTime(
                targetYear,
                anchorMonth,
                day,
                0,
                0,
                0,
                DateTimeKind.Unspecified)
            .Add(
                current.TimeOfDay);
    }

    /// <summary>
    /// 获取紧接着的下一个工作日。
    /// </summary>
    private static DateTime
        GetNextWeekday(
            DateTime current)
    {
        DateTime result =
            current.AddDays(1);

        while (result.DayOfWeek is
               DayOfWeek.Saturday or
               DayOfWeek.Sunday)
        {
            result =
                result.AddDays(1);
        }

        return result;
    }
}
