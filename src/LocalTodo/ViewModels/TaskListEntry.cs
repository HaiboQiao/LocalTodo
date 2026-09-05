using System;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 任务列表中的一个显示条目。
///
/// Task 保存原任务；
/// 其余字段只用于日期分组和排序。
/// </summary>
public sealed class TaskListEntry
{
    private TaskListEntry(
        TaskItem task,
        string groupHeader,
        int groupOrder,
        DateTime groupDate,
        bool isDueToday,
        int sequence)
    {
        Task =
            task;

        GroupHeader =
            groupHeader;

        GroupOrder =
            groupOrder;

        GroupDate =
            groupDate;

        IsDueToday =
            isDueToday;

        Sequence =
            sequence;
    }

    public TaskItem Task { get; }

    public string GroupHeader { get; }

    public int GroupOrder { get; }

    public DateTime GroupDate { get; }

    /// <summary>
    /// 当前条目是否属于“今天”分组。
    ///
    /// 该属性只控制分组标题样式，
    /// 不会把今天已经到点的任务误当成今天分组标题。
    /// </summary>
    public bool IsTodayGroup =>
        GroupOrder == 1;

    /// <summary>
    /// 任务的实际截止日期是否为今天。
    ///
    /// 即使有具体截止时间且已经到点、
    /// 任务被移动到“已过期”分组，
    /// 该属性仍然为 true，供任务行保留今天的视觉强调。
    /// </summary>
    public bool IsDueToday { get; }

    /// <summary>
    /// 任务真实截止时间。
    ///
    /// CollectionView 使用它保证：
    ///
    /// 1. 已过期组按真实截止时间排序；
    /// 2. 今天同一天的任务按具体时间排序；
    /// 3. 同一未来日期的任务按具体时间排序；
    /// 4. 增量加入的新循环任务立即进入正确位置。
    /// </summary>
    public DateTimeOffset SortDueAt =>
        Task.DueAt.HasValue
            ? new DateTimeOffset(
                LocalDueDateTime.GetWallClock(
                    Task.DueAt.Value),
                TimeSpan.Zero)
            : DateTimeOffset.MaxValue;

    /// <summary>
    /// 与 TaskRepository 的
    /// is_important DESC 保持一致。
    /// </summary>
    public bool SortIsImportant =>
        Task.IsImportant;

    /// <summary>
    /// 与 TaskRepository 的
    /// priority DESC 保持一致。
    /// </summary>
    public TaskPriority SortPriority =>
        Task.Priority;

    /// <summary>
    /// 与 TaskRepository 的
    /// created_at DESC 保持一致。
    /// </summary>
    public DateTimeOffset SortCreatedAt =>
        Task.CreatedAt;

    /// <summary>
    /// 最后的稳定排序序号。
    ///
    /// 不再负责主要业务排序，
    /// 只在前面的排序字段完全相同时使用。
    /// </summary>
    public int Sequence { get; }

    public static TaskListEntry Create(
        TaskItem task,
        DateTime today,
        int sequence)
    {
        DateTime? dueDate =
            task.DueAt?
                .DateTime
                .Date;

        /*
         * 没有截止日期：始终放在最后。
         */
        if (!dueDate.HasValue)
        {
            return new TaskListEntry(
                task,
                "无截止日期",
                groupOrder: 3,
                groupDate: DateTime.MaxValue,
                isDueToday: false,
                sequence);
        }

        /*
         * 判断任务是否已经过期。
         *
         * 具体规则统一由 TaskItem.IsOverdue 负责：
         *
         * 只有日期：
         * 截止日期早于今天才算过期。
         *
         * 有具体时间：
         * 当前时间达到或超过截止时间以后，
         * 当天也立即进入“已过期”。
         */
        if (task.IsOverdue)
        {
            return new TaskListEntry(
                task,
                "已过期",
                groupOrder: 0,
                groupDate: DateTime.MinValue,
                isDueToday:
                    dueDate.Value == today,
                sequence);
        }

        /*
         * 持续任务在完成或到期以前每天都属于“今天”。
         * 已完成列表仍按真实截止日期分组，不改变历史展示。
         */
        if (task.Status == TodoStatus.Pending &&
            task.IsContinuous)
        {
            string currentWeekdayText =
                GetWeekdayText(
                    today.DayOfWeek);

            return new TaskListEntry(
                task,
                $"今天, {currentWeekdayText}",
                groupOrder: 1,
                groupDate: today,
                isDueToday: true,
                sequence);
        }

        string weekdayText =
            GetWeekdayText(
                dueDate.Value.DayOfWeek);

        /*
         * 正好是今天。
         */
        if (dueDate.Value ==
            today)
        {
            return new TaskListEntry(
                task,
                $"今天, {weekdayText}",
                groupOrder: 1,
                groupDate: today,
                isDueToday: true,
                sequence);
        }

        DateTime currentWeekStart =
            GetMondayOfWeek(today);

        DateTime nextWeekStart =
            currentWeekStart.AddDays(7);

        DateTime weekAfterNextStart =
            nextWeekStart.AddDays(7);

        string relativeWeekdayText;

        /*
         * 日期仍在当前自然周中。
         */
        if (dueDate.Value <
            nextWeekStart)
        {
            relativeWeekdayText =
                weekdayText;
        }
        /*
         * 日期位于下一个自然周。
         */
        else if (dueDate.Value <
                 weekAfterNextStart)
        {
            relativeWeekdayText =
                $"下{weekdayText}";
        }
        /*
         * 更远日期不使用“下下周”等表达。
         */
        else
        {
            relativeWeekdayText =
                weekdayText;
        }

        string groupHeader =
            $"{dueDate.Value:M月d日}, " +
            $"{relativeWeekdayText}";

        return new TaskListEntry(
            task,
            groupHeader,
            groupOrder: 2,
            groupDate: dueDate.Value,
            isDueToday: false,
            sequence);
    }

    /// <summary>
    /// 取得日期所在周的星期一。
    /// </summary>
    private static DateTime GetMondayOfWeek(
        DateTime date)
    {
        int dayOffset =
            ((int)date.DayOfWeek + 6) % 7;

        return date.Date.AddDays(
            -dayOffset);
    }

    private static string GetWeekdayText(
        DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday =>
                "周一",

            DayOfWeek.Tuesday =>
                "周二",

            DayOfWeek.Wednesday =>
                "周三",

            DayOfWeek.Thursday =>
                "周四",

            DayOfWeek.Friday =>
                "周五",

            DayOfWeek.Saturday =>
                "周六",

            DayOfWeek.Sunday =>
                "周日",

            _ =>
                string.Empty
        };
    }
}
