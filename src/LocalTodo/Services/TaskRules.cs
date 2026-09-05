using System;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 任务新增和编辑的统一业务规则。
///
/// 所有入口必须把 UI 输入整理为 TaskEditDraft，再通过这里得到
/// 相同的数据库字段，避免页面之间出现行为漂移。
/// </summary>
public static class TaskRules
{
    public const int MaximumTitleLength =
        300;

    public const int MaximumDescriptionLength =
        10000;

    /// <summary>
    /// 归一化编辑草稿，但不修改任务对象。
    /// </summary>
    public static TaskEditResult Normalize(
        TaskEditDraft draft,
        TaskItem? currentTask = null,
        ILocalTimeService? localTimeService = null)
    {
        return NormalizeCore(
            draft,
            currentTask,
            normalizeText: true,
            localTimeService ??
                LocalTimeService.System);
    }

    private static TaskEditResult NormalizeCore(
        TaskEditDraft draft,
        TaskItem? currentTask,
        bool normalizeText,
        ILocalTimeService localTimeService)
    {
        ArgumentNullException.ThrowIfNull(draft);

        string normalizedTitle =
            normalizeText
                ? NormalizeTitle(
                    draft.Title)
                : currentTask?.Title ??
                    draft.Title ??
                    string.Empty;

        string normalizedDescription =
            normalizeText
                ? NormalizeDescription(
                    draft.Description)
                : currentTask?.Description ??
                    draft.Description ??
                    string.Empty;

        QuadrantMapping.ValidateQuadrant(
            draft.Quadrant);

        ValidateRepeatType(
            draft.RepeatType);

        if (draft.ReminderMinutesBefore < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft),
                draft.ReminderMinutesBefore,
                "提醒提前时间不能小于 0 分钟。");
        }

        ValidateDueTime(
            draft.DueTime);

        bool hasDueDate =
            draft.DueDate.HasValue;

        bool hasDueTime =
            hasDueDate &&
            draft.DueTime.HasValue;

        DateTimeOffset? dueAt =
            CreateLocalDueAt(
                draft.DueDate,
                hasDueTime
                    ? draft.DueTime
                    : null,
                localTimeService);

        bool reminderEnabled =
            hasDueTime &&
            draft.ReminderEnabled;

        int reminderMinutesBefore =
            reminderEnabled
                ? draft.ReminderMinutesBefore
                : 0;

        TaskRepeatType repeatType =
            hasDueDate
                ? draft.RepeatType
                : TaskRepeatType.None;

        bool isContinuous =
            hasDueDate &&
            draft.IsContinuous;

        string? recurrenceSeriesId =
            ResolveRecurrenceSeriesId(
                repeatType,
                currentTask?
                    .RecurrenceSeriesId);

        DateTime? currentLocalDueDate =
            currentTask?
                .DueAt?
                .DateTime
                .Date;

        TimeSpan? currentLocalDueTime =
            currentTask is
                {
                    HasDueTime: true,
                    DueAt: not null
                }
                    ? currentTask.DueAt.Value
                        .DateTime
                        .TimeOfDay
                    : null;

        DateTime? normalizedLocalDueDate =
            dueAt?
                .DateTime
                .Date;

        TimeSpan? normalizedLocalDueTime =
            hasDueTime &&
            dueAt.HasValue
                ? dueAt.Value
                    .DateTime
                    .TimeOfDay
                : null;

        bool dueDateChanged =
            currentTask is not null &&
            currentLocalDueDate !=
                normalizedLocalDueDate;

        bool dueTimeChanged =
            currentTask is not null &&
            currentLocalDueTime !=
                normalizedLocalDueTime;

        bool reminderChanged =
            currentTask is not null &&
            (currentTask.ReminderEnabled !=
                 reminderEnabled ||
             currentTask.ReminderMinutesBefore !=
                 reminderMinutesBefore);

        bool repeatTypeChanged =
            currentTask is not null &&
            currentTask.RepeatType !=
                repeatType;

        bool resetRecurrenceAnchor =
            currentTask is null ||
            dueDateChanged ||
            repeatTypeChanged;

        int? recurrenceAnchorMonth =
            null;

        int? recurrenceAnchorDay =
            null;

        if (normalizedLocalDueDate.HasValue)
        {
            if (repeatType ==
                TaskRepeatType.Monthly)
            {
                recurrenceAnchorDay =
                    resetRecurrenceAnchor
                        ? normalizedLocalDueDate.Value.Day
                        : currentTask?
                              .RecurrenceAnchorDay ??
                          normalizedLocalDueDate.Value.Day;
            }
            else if (repeatType ==
                     TaskRepeatType.Yearly)
            {
                recurrenceAnchorMonth =
                    resetRecurrenceAnchor
                        ? normalizedLocalDueDate.Value.Month
                        : currentTask?
                              .RecurrenceAnchorMonth ??
                          normalizedLocalDueDate.Value.Month;

                recurrenceAnchorDay =
                    resetRecurrenceAnchor
                        ? normalizedLocalDueDate.Value.Day
                        : currentTask?
                              .RecurrenceAnchorDay ??
                          normalizedLocalDueDate.Value.Day;
            }
        }

        return new TaskEditResult(
            normalizedTitle,
            normalizedDescription,
            dueAt,
            hasDueTime,
            reminderEnabled,
            reminderMinutesBefore,
            repeatType,
            recurrenceSeriesId,
            recurrenceAnchorMonth,
            recurrenceAnchorDay,
            isContinuous,
            draft.IsImportant,
            draft.Quadrant,
            dueDateChanged,
            dueTimeChanged,
            reminderChanged,
            dueDateChanged ||
                dueTimeChanged ||
                reminderChanged);
    }

    /// <summary>
    /// 把完整编辑草稿统一写回任务对象。
    /// </summary>
    public static TaskEditResult Apply(
        TaskItem task,
        TaskEditDraft draft,
        ILocalTimeService? localTimeService = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        TaskEditResult result =
            Normalize(
                draft,
                task,
                localTimeService);

        task.Title =
            result.Title;

        task.Description =
            result.Description;

        ApplySchedulingResult(
            task,
            result);

        /*
         * AssignedQuadrant 统一维护手动象限和旧 Priority。
         * 星标在象限之后单独写入，确保两者保持独立。
         */
        task.AssignedQuadrant =
            result.Quadrant;

        task.IsImportant =
            result.IsImportant;

        return result;
    }

    /// <summary>
    /// 只把日期、时间、提醒和循环规则写回任务。
    ///
    /// TaskList 的标题直接绑定到 TaskItem；用户临时清空标题时，
    /// 日期控件仍需正常响应，因此这里不提前校验或覆盖文本字段。
    /// 最终保存仍会通过完整 Apply 进行验证。
    /// </summary>
    public static TaskEditResult ApplyScheduling(
        TaskItem task,
        TaskEditDraft draft,
        ILocalTimeService? localTimeService = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(draft);

        TaskEditResult result =
            NormalizeCore(
            draft,
            task,
            normalizeText: false,
            localTimeService ??
                LocalTimeService.System);

        ApplySchedulingResult(
            task,
            result);

        return result;
    }

    public static string NormalizeTitle(
        string? title)
    {
        string normalizedTitle =
            title?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                normalizedTitle))
        {
            throw new ArgumentException(
                "任务标题不能为空。",
                nameof(title));
        }

        if (normalizedTitle.Length >
            MaximumTitleLength)
        {
            throw new ArgumentException(
                $"任务标题不能超过 " +
                $"{MaximumTitleLength} 个字符。",
                nameof(title));
        }

        return normalizedTitle;
    }

    public static string NormalizeDescription(
        string? description)
    {
        string normalizedDescription =
            description ??
            string.Empty;

        if (normalizedDescription.Length >
            MaximumDescriptionLength)
        {
            throw new ArgumentException(
                $"任务说明不能超过 " +
                $"{MaximumDescriptionLength} 个字符。",
                nameof(description));
        }

        return normalizedDescription;
    }

    /// <summary>
    /// 将本地日期和可选时间组合为带本地偏移的截止时间。
    /// 时间为空时，内部保存当天 00:00，并由 HasDueTime 区分“仅日期”。
    /// </summary>
    public static DateTimeOffset? CreateLocalDueAt(
        DateTime? dueDate,
        TimeSpan? dueTime,
        ILocalTimeService? localTimeService = null)
    {
        if (!dueDate.HasValue)
        {
            return null;
        }

        ValidateDueTime(dueTime);

        DateTime localDateTime =
            dueDate.Value.Date +
            (dueTime ??
             TimeSpan.Zero);

        localDateTime =
            DateTime.SpecifyKind(
                localDateTime,
                DateTimeKind.Unspecified);

        return (localTimeService ??
                LocalTimeService.System)
            .ResolveLocalDateTime(
                localDateTime);
    }

    private static void ApplySchedulingResult(
        TaskItem task,
        TaskEditResult result)
    {
        task.IsContinuous =
            result.IsContinuous;

        task.DueAt =
            result.DueAt;

        task.HasDueTime =
            result.HasDueTime;

        task.ReminderEnabled =
            result.ReminderEnabled;

        task.ReminderMinutesBefore =
            result.ReminderMinutesBefore;

        task.RepeatType =
            result.RepeatType;

        task.RecurrenceSeriesId =
            result.RecurrenceSeriesId;

        task.RecurrenceAnchorMonth =
            result.RecurrenceAnchorMonth;

        task.RecurrenceAnchorDay =
            result.RecurrenceAnchorDay;

        if (result.ShouldResetReminderDelivery)
        {
            task.ReminderDeliveredAt =
                null;
        }
    }

    private static string? ResolveRecurrenceSeriesId(
        TaskRepeatType repeatType,
        string? currentSeriesId)
    {
        if (repeatType ==
            TaskRepeatType.None)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(
                currentSeriesId)
            ? Guid.NewGuid()
                .ToString("N")
            : currentSeriesId;
    }

    private static void ValidateRepeatType(
        TaskRepeatType repeatType)
    {
        if (!Enum.IsDefined(
                typeof(TaskRepeatType),
                repeatType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(repeatType),
                repeatType,
                "无法识别任务循环方式。");
        }
    }

    private static void ValidateDueTime(
        TimeSpan? dueTime)
    {
        if (dueTime.HasValue &&
            (dueTime.Value < TimeSpan.Zero ||
             dueTime.Value >= TimeSpan.FromDays(1)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueTime),
                dueTime,
                "截止时间必须位于当天 00:00 至 23:59:59 之间。");
        }
    }
}
