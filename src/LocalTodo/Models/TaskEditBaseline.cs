using System;

namespace LocalTodo.Models;

/// <summary>
/// 编辑器开始编辑或最近一次保存成功时看到的数据库状态。
///
/// 它用于判断数据库的新版本是否修改了同一字段，而不是依赖
/// ViewModel 中可能已经被用户继续编辑的 TaskItem 对象。
/// </summary>
public sealed record TaskEditBaseline(
    string Id,
    string Title,
    string Description,
    TodoStatus Status,
    TaskPriority Priority,
    bool IsImportant,
    bool IsContinuous,
    DateTimeOffset? DueAt,
    bool HasDueTime,
    bool ReminderEnabled,
    int ReminderMinutesBefore,
    TaskRepeatType RepeatType,
    string? RecurrenceSeriesId,
    int? RecurrenceAnchorMonth,
    int? RecurrenceAnchorDay,
    DateTimeOffset? ReminderDeliveredAt,
    QuadrantMode QuadrantMode,
    QuadrantType? ManualQuadrant,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    long Revision)
{
    public static TaskEditBaseline FromTask(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new TaskEditBaseline(
            task.Id,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.IsImportant,
            task.IsContinuous,
            task.DueAt,
            task.HasDueTime,
            task.ReminderEnabled,
            task.ReminderMinutesBefore,
            task.RepeatType,
            task.RecurrenceSeriesId,
            task.RecurrenceAnchorMonth,
            task.RecurrenceAnchorDay,
            task.ReminderDeliveredAt,
            task.QuadrantMode,
            task.ManualQuadrant,
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            task.Revision);
    }

    public TaskItem ToTaskItem()
    {
        TaskItem task =
            new();

        ApplyTo(
            task);

        return task;
    }

    /// <summary>
    /// 将数据库状态同步到页面对象。
    /// preserveFields 中的值仍保留页面当前输入。
    /// </summary>
    public void ApplyTo(
        TaskItem task,
        TaskEditFields preserveFields =
            TaskEditFields.None)
    {
        ArgumentNullException.ThrowIfNull(task);

        task.Id =
            Id;

        if (!preserveFields.HasFlag(
                TaskEditFields.Title))
        {
            task.Title =
                Title;
        }

        if (!preserveFields.HasFlag(
                TaskEditFields.Description))
        {
            task.Description =
                Description;
        }

        task.Status =
            Status;

        if (!preserveFields.HasFlag(
                TaskEditFields.Quadrant))
        {
            task.Priority =
                Priority;

            task.QuadrantMode =
                QuadrantMode;

            task.ManualQuadrant =
                ManualQuadrant;
        }

        if (!preserveFields.HasFlag(
                TaskEditFields.IsImportant))
        {
            task.IsImportant =
                IsImportant;
        }

        if (!preserveFields.HasFlag(
                TaskEditFields.Schedule))
        {
            task.IsContinuous =
                IsContinuous;

            task.DueAt =
                DueAt;

            task.HasDueTime =
                HasDueTime;

            task.ReminderEnabled =
                ReminderEnabled;

            task.ReminderMinutesBefore =
                ReminderMinutesBefore;

            task.ReminderDeliveredAt =
                ReminderDeliveredAt;
        }

        if (!preserveFields.HasFlag(
                TaskEditFields.Repeat))
        {
            task.RepeatType =
                RepeatType;

            task.RecurrenceSeriesId =
                RecurrenceSeriesId;
        }

        if (!preserveFields.HasFlag(
                TaskEditFields.Schedule) &&
            !preserveFields.HasFlag(
                TaskEditFields.Repeat))
        {
            task.RecurrenceAnchorMonth =
                RecurrenceAnchorMonth;

            task.RecurrenceAnchorDay =
                RecurrenceAnchorDay;
        }

        task.CreatedAt =
            CreatedAt;

        task.UpdatedAt =
            UpdatedAt;

        task.CompletedAt =
            CompletedAt;

        task.Revision =
            Revision;
    }
}
