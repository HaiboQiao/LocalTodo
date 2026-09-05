using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Data;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// LocalTodo 的任务业务服务。
/// </summary>
public sealed class TaskService
{
    private readonly TaskRepository
        _taskRepository;

    private readonly IClock
        _clock;

    private readonly ILocalTimeService
        _localTimeService;

    public TaskService(
        TaskRepository taskRepository,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
    {
        _taskRepository =
            taskRepository;

        _clock =
            clock ??
            SystemClock.Instance;

        _localTimeService =
            localTimeService ??
            LocalTimeService.System;
    }

    /// <summary>
    /// 任务数据发生变化时触发。
    /// </summary>
    public event EventHandler<TaskChangedEventArgs>?
        TasksChanged;

    /// <summary>
    /// 根据完成状态读取未删除任务。
    /// </summary>
    public Task<IReadOnlyList<TaskItem>>
        GetTasksAsync(
            TodoStatus status,
            CancellationToken cancellationToken = default)
    {
        return _taskRepository.GetTasksAsync(
            status,
            cancellationToken);
    }

    /// <summary>
    /// 读取垃圾箱中的软删除任务。
    /// </summary>
    public Task<IReadOnlyList<TaskItem>>
        GetDeletedTasksAsync(
            CancellationToken cancellationToken = default)
    {
        return _taskRepository
            .GetDeletedTasksAsync(
                cancellationToken);
    }

    /// <summary>
    /// 读取日历日期范围内的任务。
    /// </summary>
    public Task<IReadOnlyList<TaskItem>>
        GetCalendarTasksAsync(
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            CancellationToken cancellationToken = default)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException(
                "日历结束日期必须晚于开始日期。",
                nameof(endUtc));
        }

        return _taskRepository
            .GetTasksByDueRangeAsync(
                startUtc,
                endUtc,
                cancellationToken);
    }

    /// <summary>
    /// 读取截至指定时刻真正到期且尚未投递的提醒。
    /// </summary>
    public Task<IReadOnlyList<TaskItem>>
        GetDueRemindersAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
    {
        return _taskRepository
            .GetDueRemindersAsync(
                now,
                cancellationToken);
    }

    /// <summary>
    /// 创建一条指定象限的未完成任务。
    /// </summary>
    public Task<TaskItem>
        CreateTaskAsync(
            string title,
            QuadrantType quadrant,
            DateTimeOffset? dueAt,
            CancellationToken cancellationToken = default)
    {
        return CreateTaskAsync(
            CreateDraftFromDueAt(
                title,
                description: null,
                quadrant,
                dueAt,
                isImportant:
                    QuadrantMapping
                        .IsImportant(
                            quadrant),
                hasDueTime: false,
                reminderEnabled: false,
                reminderMinutesBefore: 0,
                repeatType:
                    TaskRepeatType.None),
            cancellationToken);
    }

    /// <summary>
    /// 创建一条直接属于指定象限的任务。
    ///
    /// 这是兼容旧界面的简单版本。
    ///
    /// 当前主要供：
    /// 1. 四象限快速新增；
    /// 2. 桌面任务列表快速新增。
    ///
    /// 这些界面目前只设置日期，
    /// 不设置具体截止时间、提醒和循环。
    /// </summary>
    public Task<TaskItem>
        CreateTaskInQuadrantAsync(
            string title,
            string? description,
            QuadrantType quadrant,
            DateTimeOffset? dueAt,
            bool isImportant,
            CancellationToken cancellationToken = default)
    {
        return CreateTaskAsync(
            CreateDraftFromDueAt(
                title,
                description,
                quadrant,
                dueAt,
                isImportant,
                hasDueTime: false,
                reminderEnabled: false,
                reminderMinutesBefore: 0,
                repeatType:
                    TaskRepeatType.None),
            cancellationToken);
    }

    /// <summary>
    /// 创建一条包含完整日期、时间、提醒和循环设置的任务。
    ///
    /// 当前“所有任务”新增窗口使用这个版本。
    /// </summary>
    public Task<TaskItem>
        CreateTaskInQuadrantAsync(
            string title,
            string? description,
            QuadrantType quadrant,
            DateTimeOffset? dueAt,
            bool isImportant,
            bool hasDueTime,
            bool reminderEnabled,
            int reminderMinutesBefore,
            TaskRepeatType repeatType,
            CancellationToken cancellationToken = default)
    {
        return CreateTaskAsync(
            CreateDraftFromDueAt(
                title,
                description,
                quadrant,
                dueAt,
                isImportant,
                hasDueTime,
                reminderEnabled,
                reminderMinutesBefore,
                repeatType),
            cancellationToken);
    }

    /// <summary>
    /// 使用四个界面共用的编辑草稿创建任务。
    /// </summary>
    public async Task<TaskItem> CreateTaskAsync(
        TaskEditDraft draft,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        TaskEditResult normalized =
            TaskRules.Normalize(
                draft,
                localTimeService:
                    _localTimeService);

        DateTimeOffset now =
            _clock.UtcNow;

        TaskItem task =
            new()
            {
                Id =
                    Guid.NewGuid()
                        .ToString("N"),

                Title =
                    normalized.Title,

                Description =
                    normalized.Description,

                Status =
                    TodoStatus.Pending,

                Priority =
                    QuadrantMapping
                        .ToLegacyPriority(
                            normalized.Quadrant),

                IsImportant =
                    normalized.IsImportant,

                IsContinuous =
                    normalized.IsContinuous,

                DueAt =
                    normalized.DueAt,

                HasDueTime =
                    normalized.HasDueTime,

                ReminderEnabled =
                    normalized.ReminderEnabled,

                ReminderMinutesBefore =
                    normalized.ReminderMinutesBefore,

                RepeatType =
                    normalized.RepeatType,

                RecurrenceSeriesId =
                    normalized.RecurrenceSeriesId,

                RecurrenceAnchorMonth =
                    normalized.RecurrenceAnchorMonth,

                RecurrenceAnchorDay =
                    normalized.RecurrenceAnchorDay,

                ReminderDeliveredAt =
                    null,

                QuadrantMode =
                    QuadrantMode.Manual,

                ManualQuadrant =
                    normalized.Quadrant,

                CreatedAt =
                    now,

                UpdatedAt =
                    now,

                CompletedAt =
                    null
            };

        await _taskRepository
            .InsertAsync(
                task,
                cancellationToken);

        RaiseTasksChanged(
            task,
            TaskChangeType.Created,
            TaskEditFields.All,
            changeSource,
            requiresRegroup: true);

        return task;
    }

    /// <summary>
    /// 按脏字段保存普通详情编辑。
    ///
    /// Revision 未变化时直接保存；版本变化但用户字段不重叠时，
    /// 自动以数据库最新状态为基线重试；同字段变化则返回冲突，
    /// 不修改数据库，也不覆盖调用方的编辑缓冲区。
    /// </summary>
    public async Task<TaskEditSaveResult>
        SaveTaskEditAsync(
            TaskEditRequest request,
            CancellationToken cancellationToken = default,
            TaskChangeSource changeSource =
                TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        request.Validate();

        TaskEditBaseline originalBaseline =
            request.Baseline;

        TaskEditBaseline attemptBaseline =
            originalBaseline;

        bool wasMerged =
            false;

        for (int attempt = 0;
             attempt < 5;
             attempt++)
        {
            TaskItem candidate =
                attemptBaseline
                    .ToTaskItem();

            TaskEditResult normalized =
                TaskRules.Apply(
                    candidate,
                    request.Draft,
                    _localTimeService);

            candidate.UpdatedAt =
                _clock.UtcNow;

            TaskItem? persistedTask =
                await _taskRepository
                    .TryUpdateEditableFieldsAsync(
                        candidate,
                        request.ChangedFields,
                        attemptBaseline.Revision,
                        request.ChangedFields.HasFlag(
                            TaskEditFields.Schedule) &&
                        normalized
                            .ShouldResetReminderDelivery,
                        cancellationToken);

            if (persistedTask is not null)
            {
                RaiseTasksChanged(
                    persistedTask,
                    TaskChangeType.Updated,
                    request.ChangedFields,
                    changeSource,
                    RequiresRegroup(
                        request.ChangedFields));

                return new TaskEditSaveResult(
                    wasMerged
                        ? TaskEditSaveStatus.Merged
                        : TaskEditSaveStatus.Saved,
                    TaskEditBaseline.FromTask(
                        persistedTask),
                    TaskEditFields.None);
            }

            TaskItem? latestTask =
                await _taskRepository
                    .GetActiveTaskByIdAsync(
                        originalBaseline.Id,
                        cancellationToken);

            if (latestTask is null)
            {
                return new TaskEditSaveResult(
                    TaskEditSaveStatus
                        .TargetUnavailable,
                    Current: null,
                    request.ChangedFields);
            }

            /*
             * 完成/恢复属于专用命令，不能被普通详情编辑吸收为
             * 可自动合并的版本变化。编辑器打开时所属状态一旦变化，
             * 本轮详情保存即失效。
             */
            if (latestTask.Status !=
                originalBaseline.Status)
            {
                return new TaskEditSaveResult(
                    TaskEditSaveStatus
                        .TargetUnavailable,
                    Current: null,
                    request.ChangedFields);
            }

            TaskEditBaseline latestBaseline =
                TaskEditBaseline.FromTask(
                    latestTask);

            TaskItem desiredOnLatest =
                latestBaseline
                    .ToTaskItem();

            TaskRules.Apply(
                desiredOnLatest,
                request.Draft,
                _localTimeService);

            TaskEditBaseline desiredBaseline =
                TaskEditBaseline.FromTask(
                    desiredOnLatest);

            TaskEditFields conflictingFields =
                GetConflictingFields(
                    originalBaseline,
                    latestBaseline,
                    desiredBaseline,
                    request.ChangedFields);

            if (conflictingFields !=
                TaskEditFields.None)
            {
                return new TaskEditSaveResult(
                    TaskEditSaveStatus.Conflict,
                    latestBaseline,
                    conflictingFields);
            }

            attemptBaseline =
                latestBaseline;

            wasMerged =
                true;
        }

        TaskItem? finalLatestTask =
            await _taskRepository
                .GetActiveTaskByIdAsync(
                    originalBaseline.Id,
                    cancellationToken);

        bool targetUnavailable =
            finalLatestTask is null ||
            finalLatestTask.Status !=
                originalBaseline.Status;

        return new TaskEditSaveResult(
            targetUnavailable
                ? TaskEditSaveStatus.TargetUnavailable
                : TaskEditSaveStatus.Conflict,
            targetUnavailable
                ? null
                : TaskEditBaseline.FromTask(
                    finalLatestTask!),
            request.ChangedFields);
    }

    /// <summary>
    /// 将统一编辑草稿应用到现有任务后保存。
    ///
    /// 保留给服务层兼容调用；界面编辑器应使用 SaveTaskEditAsync
    /// 并传入真正修改过的字段。
    /// </summary>
    public async Task<TaskEditResult> UpdateTaskAsync(
        TaskItem task,
        TaskEditDraft draft,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        TaskEditBaseline baseline =
            TaskEditBaseline.FromTask(
                task);

        TaskItem desiredTask =
            baseline.ToTaskItem();

        TaskEditResult result =
            TaskRules.Apply(
                desiredTask,
                draft,
                _localTimeService);

        TaskEditFields changedFields =
            GetChangedFields(
                baseline,
                TaskEditBaseline.FromTask(
                    desiredTask));

        if (changedFields ==
            TaskEditFields.None)
        {
            return result;
        }

        TaskEditSaveResult saveResult =
            await SaveTaskEditAsync(
                new TaskEditRequest(
                    baseline,
                    draft,
                    changedFields),
                cancellationToken,
                changeSource);

        if (!saveResult.IsSaved ||
            saveResult.Current is null)
        {
            throw new TaskConcurrencyException(
                task.Id);
        }

        saveResult.Current.ApplyTo(
            task);

        return result;
    }

    /// <summary>
    /// 保存任务。
    /// </summary>
    public async Task UpdateTaskAsync(
        TaskItem task,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        task.Title =
            TaskRules.NormalizeTitle(
                task.Title);

        task.Description =
            TaskRules.NormalizeDescription(
                task.Description);

        if (task.Status ==
            TodoStatus.Completed)
        {
            task.CompletedAt ??=
                _clock.UtcNow;
        }
        else
        {
            task.CompletedAt =
                null;
        }

        task.UpdatedAt =
            _clock.UtcNow;

        await _taskRepository.UpdateAsync(
            task,
            cancellationToken);

        RaiseTasksChanged(
            task,
            TaskChangeType.Updated,
            TaskEditFields.All,
            changeSource,
            requiresRegroup: true);
    }

    /// <summary>
    /// 切换任务完成状态。
    /// </summary>
    public async Task<TaskItem?>
    ToggleCompletionAsync(
        TaskItem task,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        TaskStateSnapshot originalState =
            TaskStateSnapshot.Create(task);

        /*
         * 已完成 → 未完成。
         *
         * 对普通任务：
         * 直接恢复为未完成。
         *
         * 对循环任务：
         * 当前这一期在完成时已经生成了下一期循环任务。
         *
         * 因此如果用户再从“已完成”中恢复当前历史期次，
         * 必须把当前历史期次转换成普通不循环任务。
         *
         * 否则当前历史任务和已经生成的下一期任务
         * 都会继续保留 RepeatType，
         * 从而产生两条独立的循环任务链。
         */
        if (task.Status ==
            TodoStatus.Completed)
        {
            if (task.RepeatType !=
                TaskRepeatType.None)
            {
                /*
                 * 已完成历史期恢复以后，
                 * 变成一个独立普通任务。
                 *
                 * 因此不仅取消 RepeatType，
                 * 也必须让它退出原来的循环系列。
                 */
                task.RepeatType =
                    TaskRepeatType.None;

                task.RecurrenceSeriesId =
                    null;

                task.RecurrenceAnchorMonth =
                    null;

                task.RecurrenceAnchorDay =
                    null;
            }

            task.Status =
                TodoStatus.Pending;

            task.CompletedAt =
                null;

            task.Title =
                TaskRules.NormalizeTitle(
                    task.Title);

            task.Description =
                TaskRules.NormalizeDescription(
                    task.Description);

            task.UpdatedAt =
                _clock.UtcNow;

            try
            {
                await _taskRepository
                    .UpdateAsync(
                        task,
                        cancellationToken);
            }
            catch
            {
                originalState.Restore(task);
                throw;
            }

            RaiseTasksChanged(
                task,
                TaskChangeType.CompletionChanged,
                TaskEditFields.None,
                changeSource,
                requiresRegroup: true);

            return null;
        }

        /*
         * 未完成 → 已完成。
         */
        DateTimeOffset completedAt =
            _clock.UtcNow;

        task.Title =
            TaskRules.NormalizeTitle(
                task.Title);

        task.Description =
            TaskRules.NormalizeDescription(
                task.Description);

        task.Status =
            TodoStatus.Completed;

        task.CompletedAt =
            completedAt;

        task.UpdatedAt =
            completedAt;

        TaskItem? nextTask =
            null;

        DateTimeOffset? nextDueAt =
            TaskRecurrenceCalculator
                .GetNextDueAt(
                    task,
                    _localTimeService);

        /*
         * 普通任务。
         */
        if (!nextDueAt.HasValue)
        {
            try
            {
                await _taskRepository
                    .UpdateAsync(
                        task,
                        cancellationToken);
            }
            catch
            {
                originalState.Restore(task);
                throw;
            }

            RaiseTasksChanged(
                task,
                TaskChangeType.CompletionChanged,
                TaskEditFields.None,
                changeSource,
                requiresRegroup: true);

            return null;
        }

        /*
         * 旧数据库升级到新的循环系列结构以后，
         * 以前已经存在的循环任务可能没有 SeriesId。
         *
         * 当它下一次完成并准备生成下一期时，
         * 就从这一期开始正式建立循环系列。
         */
        if (string.IsNullOrWhiteSpace(
                task.RecurrenceSeriesId))
        {
            task.RecurrenceSeriesId =
                Guid.NewGuid()
                    .ToString("N");
        }

        /*
         * 循环任务：
         * 当前这一期进入已完成，
         * 同时建立下一期。
         */
        nextTask =
            new TaskItem
            {
                Id =
                    Guid.NewGuid()
                        .ToString("N"),

                Title =
                    task.Title,

                Description =
                    task.Description,

                Status =
                    TodoStatus.Pending,

                Priority =
                    task.Priority,

                IsImportant =
                    task.IsImportant,

                IsContinuous =
                    task.IsContinuous,

                DueAt =
                    nextDueAt,

                HasDueTime =
                    task.HasDueTime,

                ReminderEnabled =
                    task.ReminderEnabled,

                ReminderMinutesBefore =
                    task.ReminderMinutesBefore,

                RepeatType =
                    task.RepeatType,

                /*
                 * 下一期属于同一个循环系列，
                 * 所以绝对不能重新生成一个新的 Guid。
                 */
                RecurrenceSeriesId =
                    task.RecurrenceSeriesId,

                RecurrenceAnchorMonth =
                    task.RecurrenceAnchorMonth,

                RecurrenceAnchorDay =
                    task.RecurrenceAnchorDay,

                ReminderDeliveredAt =
                    null,

                QuadrantMode =
                    task.QuadrantMode,

                ManualQuadrant =
                    task.ManualQuadrant,

                CreatedAt =
                    completedAt,

                UpdatedAt =
                    completedAt,

                CompletedAt =
                    null
            };

        try
        {
            await _taskRepository
                .CompleteRecurringTaskAsync(
                    task,
                    nextTask,
                    cancellationToken);
        }
        catch
        {
            originalState.Restore(task);
            throw;
        }

        RaiseTasksChanged(
            task,
            TaskChangeType.CompletionChanged,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: true);

        return nextTask;
    }

    /// <summary>
    /// 将任务可靠地设置为指定完成状态。
    ///
    /// 列表中的任务对象可能因其他窗口或自动保存而暂时落后于数据库。
    /// 此入口按 ID 读取最新版本，并在提交瞬间遇到版本竞争时重新读取重试，
    /// 避免用户点击完成框后只能手动刷新再试。
    /// </summary>
    public async Task<TaskItem?> SetCompletionStateAsync(
        string taskId,
        TodoStatus targetStatus,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        if (targetStatus is not
            (TodoStatus.Pending or TodoStatus.Completed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetStatus));
        }

        TaskConcurrencyException? lastConflict = null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TaskItem? latestTask =
                await _taskRepository.GetActiveTaskByIdAsync(
                    taskId,
                    cancellationToken);

            if (latestTask is null)
            {
                throw new InvalidOperationException(
                    "该任务已在其他窗口删除，任务列表即将刷新。");
            }

            if (latestTask.Status == targetStatus)
            {
                return null;
            }

            try
            {
                return await ToggleCompletionAsync(
                    latestTask,
                    cancellationToken,
                    changeSource);
            }
            catch (TaskConcurrencyException exception)
            {
                lastConflict = exception;
            }
        }

        throw lastConflict ??
            new TaskConcurrencyException(taskId);
    }

    /// <summary>
    /// 把普通任务移动到垃圾箱。
    /// </summary>
    public async Task DeleteTaskAsync(
        TaskItem task,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        DateTimeOffset now =
            _clock.UtcNow;

        await _taskRepository.SoftDeleteAsync(
            task,
            now,
            cancellationToken);

        RaiseTasksChanged(
            task,
            TaskChangeType.Deleted,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: true);
    }

    /// <summary>
    /// 删除循环任务的当前周期。
    ///
    /// Pending 循环任务：
    /// 当前期进入垃圾箱，
    /// 同时主动生成下一期。
    ///
    /// Completed 循环历史任务：
    /// 下一期早已在“完成任务”时生成，
    /// 所以这里只删除当前历史记录，
    /// 绝对不能再次生成下一期，
    /// 更不能影响已经存在的下一期。
    ///
    /// 无论 Pending 还是 Completed，
    /// 被删除的当前期都会在进入垃圾箱前：
    ///
    /// RepeatType = None
    /// RecurrenceSeriesId = null
    ///
    /// 因此以后从垃圾箱恢复时，
    /// 它只会恢复成普通不循环任务。
    /// </summary>
    private async Task<TaskItem?>
        DeleteCurrentOccurrenceAsync(
            TaskItem task,
            CancellationToken cancellationToken,
            TaskChangeSource changeSource)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        TaskStateSnapshot originalState =
            TaskStateSnapshot.Create(task);

        if (task.RepeatType ==
            TaskRepeatType.None)
        {
            throw new InvalidOperationException(
                "当前任务不是循环任务。");
        }

        DateTimeOffset now =
            _clock.UtcNow;

        /*
         * 在当前任务退出循环系列之前，
         * 保存原来的循环方式和 SeriesId。
         *
         * Pending 删除当前周期时，
         * 新生成的下一期仍然要继续原系列。
         */
        TaskRepeatType originalRepeatType =
            task.RepeatType;

        string seriesId =
            string.IsNullOrWhiteSpace(
                task.RecurrenceSeriesId)
                ? Guid.NewGuid()
                    .ToString("N")
                : task.RecurrenceSeriesId;

        TaskItem? nextTask =
            null;

        /*
         * ============================
         * Pending：删除当前周期
         * ============================
         *
         * 当前这一期尚未完成，
         * 因而下一期通常还没有生成。
         *
         * 此时需要主动计算并创建下一期。
         */
        if (task.Status ==
            TodoStatus.Pending)
        {
            DateTimeOffset? nextDueAt =
                TaskRecurrenceCalculator
                    .GetNextDueAt(
                        task,
                        _localTimeService);

            if (!nextDueAt.HasValue)
            {
                throw new InvalidOperationException(
                    "无法计算循环任务的下一周期。");
            }

            nextTask =
                new TaskItem
                {
                    Id =
                        Guid.NewGuid()
                            .ToString("N"),

                    Title =
                        task.Title,

                    Description =
                        task.Description,

                    Status =
                        TodoStatus.Pending,

                    Priority =
                        task.Priority,

                    IsImportant =
                        task.IsImportant,

                    IsContinuous =
                        task.IsContinuous,

                    DueAt =
                        nextDueAt,

                    HasDueTime =
                        task.HasDueTime,

                    ReminderEnabled =
                        task.ReminderEnabled,

                    ReminderMinutesBefore =
                        task.ReminderMinutesBefore,

                    RepeatType =
                        originalRepeatType,

                    RecurrenceSeriesId =
                        seriesId,

                    RecurrenceAnchorMonth =
                        task.RecurrenceAnchorMonth,

                    RecurrenceAnchorDay =
                        task.RecurrenceAnchorDay,

                    ReminderDeliveredAt =
                        null,

                    QuadrantMode =
                        task.QuadrantMode,

                    ManualQuadrant =
                        task.ManualQuadrant,

                    CreatedAt =
                        now,

                    UpdatedAt =
                        now,

                    CompletedAt =
                        null
                };
        }

        /*
         * ============================
         * Completed：删除历史当前期
         * ============================
         *
         * 这里故意什么都不生成。
         *
         * 因为当前历史期完成时，
         * 下一期已经存在于 Pending 列表。
         *
         * 这是本次修复最关键的安全边界：
         * 删除历史期不能触碰现有下一期。
         */

        /*
         * 当前这一期退出循环系列。
         *
         * 这是垃圾箱恢复规则的关键：
         * 删除“当前周期”的任务恢复以后，
         * 必须成为普通不循环任务。
         */
        task.RepeatType =
            TaskRepeatType.None;

        task.RecurrenceSeriesId =
            null;

        task.RecurrenceAnchorMonth =
            null;

        task.RecurrenceAnchorDay =
            null;

        task.UpdatedAt =
            now;

        try
        {
            await _taskRepository
                .DeleteCurrentRecurringOccurrenceAsync(
                    task,
                    now,
                    nextTask,
                    cancellationToken);
        }
        catch
        {
            originalState.Restore(task);
            throw;
        }

        RaiseTasksChanged(
            task,
            TaskChangeType.Deleted,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: true);

        return nextTask;
    }

    /// <summary>
    /// 删除整个循环系列。
    ///
    /// 安全规则：
    /// 只有当前活动的 Pending 循环任务
    /// 才允许执行“删除整个周期”。
    ///
    /// Completed 循环任务只是历史记录，
    /// 永远不能通过历史记录停止当前或未来周期。
    ///
    /// 当前被删除的活动期保留 RepeatType 和 SeriesId，
    /// 因而从垃圾箱恢复时仍可恢复为循环任务，
    /// 这与现有“删除整个周期”的恢复规则一致。
    /// </summary>
    private async Task
        DeleteEntireSeriesAsync(
            TaskItem task,
            CancellationToken cancellationToken,
            TaskChangeSource changeSource)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        /*
         * 最终业务层保险。
         *
         * 即使未来某个界面错误地把 Completed 任务
         * 传进这里，也绝对不能继续执行。
         */
        if (task.Status !=
            TodoStatus.Pending)
        {
            throw new InvalidOperationException(
                "只有当前未完成的循环周期" +
                "才能执行“删除整个周期”。");
        }

        if (task.RepeatType ==
            TaskRepeatType.None)
        {
            throw new InvalidOperationException(
                "当前任务不是循环任务。");
        }

        DateTimeOffset now =
            _clock.UtcNow;

        string? seriesId =
            task.RecurrenceSeriesId;

        await _taskRepository
            .SoftDeletePendingRecurrenceSeriesAsync(
                task,
                seriesId,
                now,
                cancellationToken);

        RaiseTasksChanged(
            task,
            TaskChangeType.Deleted,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: true);
    }

    /// <summary>
    /// 按照用户选择执行任务删除。
    ///
    /// 返回值只有一种用途：
    ///
    /// Pending 循环任务选择“删除当前周期”时，
    /// 会创建下一期，因此返回新生成的下一期任务。
    ///
    /// 其他情况返回 null。
    /// </summary>
    public async Task<TaskItem?>
        DeleteTaskWithChoiceAsync(
            TaskItem task,
            TaskDeleteChoice choice,
            CancellationToken cancellationToken = default,
            TaskChangeSource changeSource =
                TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        switch (choice)
        {
            /*
             * 普通不循环任务。
             */
            case TaskDeleteChoice
                .DeleteSingleTask:

                /*
                 * 做一层数据一致性保护。
                 *
                 * 循环任务不应该绕过循环删除规则
                 * 直接走普通软删除。
                 */
                if (task.RepeatType !=
                    TaskRepeatType.None)
                {
                    throw new InvalidOperationException(
                        "循环任务不能使用普通删除方式。");
                }

                await DeleteTaskAsync(
                    task,
                    cancellationToken,
                    changeSource);

                return null;

            /*
             * 循环任务：只删除当前这一期。
             *
             * Pending：
             * 删除当前并生成下一期。
             *
             * Completed：
             * 只删除历史当前期，
             * 不影响已经存在的下一期。
             */
            case TaskDeleteChoice
                .DeleteCurrentOccurrence:

                return await
                    DeleteCurrentOccurrenceAsync(
                        task,
                        cancellationToken,
                        changeSource);

            /*
             * 循环任务：停止整个循环。
             *
             * 这里再次明确拦截 Completed，
             * 即使 UI 层未来出现回归，
             * 业务层也不会误删下一期。
             */
            case TaskDeleteChoice
                .DeleteEntireSeries:

                if (task.Status ==
                    TodoStatus.Completed)
                {
                    throw new InvalidOperationException(
                        "已完成的循环历史周期不能删除整个循环。" +
                        "如需停止循环，请在当前未完成周期中" +
                        "选择“删除整个周期”。");
                }

                await DeleteEntireSeriesAsync(
                    task,
                    cancellationToken,
                    changeSource);

                return null;

            /*
             * Cancel 正常情况下不会进入 Service，
             * 这里仍然安全返回。
             */
            case TaskDeleteChoice.Cancel:

                return null;

            default:

                throw new ArgumentOutOfRangeException(
                    nameof(choice),
                    choice,
                    "无法识别任务删除方式。");
        }
    }

    /// <summary>
    /// 从垃圾箱恢复任务。
    ///
    /// 原来的 Status 会保留：
    /// Pending 恢复到“所有任务”；
    /// Completed 恢复到“已完成”。
    /// </summary>
    public async Task RestoreDeletedTaskAsync(
        TaskItem task,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        DateTimeOffset now =
            _clock.UtcNow;

        await _taskRepository
            .RestoreDeletedAsync(
                task,
                now,
                cancellationToken);

        RaiseTasksChanged(
            task,
            TaskChangeType.Restored,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: true);
    }

    /// <summary>
    /// 永久删除垃圾箱任务。
    /// </summary>
    public async Task PermanentlyDeleteTaskAsync(
        TaskItem task,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        await _taskRepository
            .PermanentlyDeleteAsync(
                task,
                cancellationToken);

        RaiseTasksChanged(
            task,
            TaskChangeType.PermanentlyDeleted,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: true);
    }

    private static TaskEditFields GetChangedFields(
        TaskEditBaseline original,
        TaskEditBaseline desired)
    {
        TaskEditFields changed =
            TaskEditFields.None;

        foreach (TaskEditFields field in
                 EditableFieldValues)
        {
            if (!EditableFieldEquals(
                    original,
                    desired,
                    field))
            {
                changed |=
                    field;
            }
        }

        return changed;
    }

    private static TaskEditFields GetConflictingFields(
        TaskEditBaseline original,
        TaskEditBaseline latest,
        TaskEditBaseline desired,
        TaskEditFields changedFields)
    {
        TaskEditFields conflicts =
            TaskEditFields.None;

        foreach (TaskEditFields field in
                 EditableFieldValues)
        {
            if (!changedFields.HasFlag(field))
            {
                continue;
            }

            bool databaseChangedField =
                !EditableFieldEquals(
                    original,
                    latest,
                    field);

            bool databaseAlreadyMatchesDraft =
                EditableFieldEquals(
                    latest,
                    desired,
                    field);

            if (databaseChangedField &&
                !databaseAlreadyMatchesDraft)
            {
                conflicts |=
                    field;
            }
        }

        return conflicts;
    }

    private static bool EditableFieldEquals(
        TaskEditBaseline left,
        TaskEditBaseline right,
        TaskEditFields field)
    {
        return field switch
        {
            TaskEditFields.Title =>
                string.Equals(
                    left.Title,
                    right.Title,
                    StringComparison.Ordinal),

            TaskEditFields.Description =>
                string.Equals(
                    left.Description,
                    right.Description,
                    StringComparison.Ordinal),

            TaskEditFields.Schedule =>
                Nullable.Equals(
                    left.DueAt?.DateTime,
                    right.DueAt?.DateTime) &&
                left.IsContinuous ==
                    right.IsContinuous &&
                left.HasDueTime ==
                    right.HasDueTime &&
                left.ReminderEnabled ==
                    right.ReminderEnabled &&
                left.ReminderMinutesBefore ==
                    right.ReminderMinutesBefore,

            TaskEditFields.Repeat =>
                left.RepeatType ==
                    right.RepeatType &&
                string.Equals(
                    left.RecurrenceSeriesId,
                    right.RecurrenceSeriesId,
                    StringComparison.Ordinal) &&
                left.RecurrenceAnchorMonth ==
                    right.RecurrenceAnchorMonth &&
                left.RecurrenceAnchorDay ==
                    right.RecurrenceAnchorDay,

            TaskEditFields.IsImportant =>
                left.IsImportant ==
                    right.IsImportant,

            TaskEditFields.Quadrant =>
                left.Priority ==
                    right.Priority &&
                left.QuadrantMode ==
                    right.QuadrantMode &&
                left.ManualQuadrant ==
                    right.ManualQuadrant,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(field),
                    field,
                    "无法识别任务编辑字段。")
        };
    }

    private static readonly TaskEditFields[]
        EditableFieldValues =
        [
            TaskEditFields.Title,
            TaskEditFields.Description,
            TaskEditFields.Schedule,
            TaskEditFields.Repeat,
            TaskEditFields.IsImportant,
            TaskEditFields.Quadrant
        ];

    private TaskEditDraft CreateDraftFromDueAt(
        string? title,
        string? description,
        QuadrantType quadrant,
        DateTimeOffset? dueAt,
        bool isImportant,
        bool hasDueTime,
        bool reminderEnabled,
        int reminderMinutesBefore,
        TaskRepeatType repeatType)
    {
        DateTime? localDueAt =
            dueAt.HasValue
                ? LocalDueDateTime.GetWallClock(
                    dueAt.Value)
                : null;

        return new TaskEditDraft(
            title,
            description,
            localDueAt?.Date,
            hasDueTime &&
                localDueAt.HasValue
                    ? localDueAt.Value.TimeOfDay
                    : null,
            reminderEnabled,
            reminderMinutesBefore,
            repeatType,
            false,
            isImportant,
            quadrant);
    }

    /// <summary>
    /// 保存一次业务写入前会被修改的内存状态。
    ///
    /// 如果数据库事务失败，恢复这些字段，避免界面对象
    /// 与已经回滚的数据库状态不一致。
    /// </summary>
    private readonly record struct TaskStateSnapshot(
        string Title,
        string Description,
        TodoStatus Status,
        TaskRepeatType RepeatType,
        string? RecurrenceSeriesId,
        int? RecurrenceAnchorMonth,
        int? RecurrenceAnchorDay,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt,
        long Revision)
    {
        public static TaskStateSnapshot Create(
            TaskItem task)
        {
            return new TaskStateSnapshot(
                task.Title,
                task.Description,
                task.Status,
                task.RepeatType,
                task.RecurrenceSeriesId,
                task.RecurrenceAnchorMonth,
                task.RecurrenceAnchorDay,
                task.UpdatedAt,
                task.CompletedAt,
                task.Revision);
        }

        public void Restore(
            TaskItem task)
        {
            task.Title =
                Title;

            task.Description =
                Description;

            task.Status =
                Status;

            task.RepeatType =
                RepeatType;

            task.RecurrenceSeriesId =
                RecurrenceSeriesId;

            task.RecurrenceAnchorMonth =
                RecurrenceAnchorMonth;

            task.RecurrenceAnchorDay =
                RecurrenceAnchorDay;

            task.UpdatedAt =
                UpdatedAt;

            task.CompletedAt =
                CompletedAt;

            task.Revision =
                Revision;
        }
    }

    /// <summary>
    /// 根据任务 ID 可靠执行删除。
    ///
    /// 界面中的 TaskItem 可能因其他窗口保存而持有旧 Revision；
    /// 删除前重新读取数据库最新版，并在提交瞬间发生竞争时有限重试。
    /// </summary>
    public async Task<TaskItem?>
        DeleteTaskWithChoiceByIdAsync(
            string taskId,
            TaskDeleteChoice choice,
            CancellationToken cancellationToken = default,
            TaskChangeSource changeSource =
                TaskChangeSource.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        if (choice == TaskDeleteChoice.Cancel)
        {
            return null;
        }

        TaskConcurrencyException? lastConflict = null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TaskItem? latestTask =
                await _taskRepository.GetActiveTaskByIdAsync(
                    taskId,
                    cancellationToken);

            if (latestTask is null)
            {
                // 其他窗口已经完成删除时，将本次操作视为幂等成功。
                return null;
            }

            try
            {
                return await DeleteTaskWithChoiceAsync(
                    latestTask,
                    choice,
                    cancellationToken,
                    changeSource);
            }
            catch (TaskConcurrencyException exception)
            {
                lastConflict = exception;
            }
        }

        throw lastConflict ??
            new TaskConcurrencyException(taskId);
    }

    /// <summary>
    /// 原子标记提醒已投递，并广播不需要重分组的增量事件。
    /// </summary>
    public async Task<bool> MarkReminderDeliveredAsync(
        TaskItem task,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Reminder)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        bool marked =
            await _taskRepository
                .MarkReminderDeliveredAsync(
                    task,
                    deliveredAt,
                    cancellationToken);

        if (!marked)
        {
            return false;
        }

        task.ReminderDeliveredAt =
            deliveredAt;

        task.Revision++;

        RaiseTasksChanged(
            task,
            TaskChangeType.ReminderDelivered,
            TaskEditFields.None,
            changeSource,
            requiresRegroup: false);

        return true;
    }

    private static bool RequiresRegroup(
        TaskEditFields changedFields)
    {
        const TaskEditFields regroupFields =
            TaskEditFields.Schedule |
            TaskEditFields.IsImportant |
            TaskEditFields.Quadrant;

        return (changedFields &
                regroupFields) != 0;
    }

    private void RaiseTasksChanged(
        TaskItem task,
        TaskChangeType changeType,
        TaskEditFields changedFields,
        TaskChangeSource changeSource,
        bool requiresRegroup)
    {
        TasksChanged?.Invoke(
            this,
            new TaskChangedEventArgs(
                task.Id,
                changeType,
                changedFields,
                task.Revision,
                changeSource,
                requiresRegroup));
    }
}
