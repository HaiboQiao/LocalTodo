using System;
using LocalTodo.Services;

namespace LocalTodo.Models;

/// <summary>
/// 任务新增和编辑界面提交给业务层的原始输入。
///
/// ViewModel 只负责收集这些值；日期、时间、提醒、循环和
/// 兼容字段之间的约束统一由 TaskRules 处理。
/// </summary>
public sealed record TaskEditDraft(
    string? Title,
    string? Description,
    DateTime? DueDate,
    TimeSpan? DueTime,
    bool ReminderEnabled,
    int ReminderMinutesBefore,
    TaskRepeatType RepeatType,
    bool IsContinuous,
    bool IsImportant,
    QuadrantType Quadrant)
{
    /// <summary>
    /// 从现有任务建立完整草稿。
    ///
    /// 桌面任务列表只编辑日期时，会以此保留原有的具体时间、
    /// 提醒和循环设置；清空日期后仍由 TaskRules 统一清理。
    /// </summary>
    public static TaskEditDraft FromTask(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        DateTime? localDueAt =
            task.DueAt.HasValue
                ? LocalDueDateTime.GetWallClock(
                    task.DueAt.Value)
                : null;

        return new TaskEditDraft(
            task.Title,
            task.Description,
            localDueAt?.Date,
            task.HasDueTime &&
                localDueAt.HasValue
                    ? localDueAt.Value.TimeOfDay
                    : null,
            task.ReminderEnabled,
            task.ReminderMinutesBefore,
            task.RepeatType,
            task.IsContinuous,
            task.IsImportant,
            task.AssignedQuadrant);
    }
}
