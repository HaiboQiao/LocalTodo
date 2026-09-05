using System;

namespace LocalTodo.Models;

/// <summary>
/// TaskRules 归一化后的任务字段，以及本次编辑造成的业务变化。
/// </summary>
public sealed record TaskEditResult(
    string Title,
    string Description,
    DateTimeOffset? DueAt,
    bool HasDueTime,
    bool ReminderEnabled,
    int ReminderMinutesBefore,
    TaskRepeatType RepeatType,
    string? RecurrenceSeriesId,
    int? RecurrenceAnchorMonth,
    int? RecurrenceAnchorDay,
    bool IsContinuous,
    bool IsImportant,
    QuadrantType Quadrant,
    bool DueDateChanged,
    bool DueTimeChanged,
    bool ReminderChanged,
    bool ShouldResetReminderDelivery);
