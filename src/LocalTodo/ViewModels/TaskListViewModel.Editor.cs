using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

public partial class TaskListViewModel
{
    /// <summary>
    /// 切换右侧任务详情时，更新属性监听和日期代理。
    /// </summary>
    partial void OnSelectedTaskChanged(
        TaskItem? oldValue,
        TaskItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -=
                OnSelectedTaskPropertyChanged;
        }

        /*
         * 用户主动切换任务时，立即提交上一条任务
         * 尚未到达 600ms 的修改。
         *
         * 集合重载时不在这里提交，重载调用方会先处理。
         */
        if (!_isReloadingTasks &&
            oldValue is not null &&
            ReferenceEquals(
                _pendingAutoSaveTask,
                oldValue))
        {
            BackgroundTaskObserver.Observe(
                FlushPendingAutoSaveAsync(),
                "切换任务详情时提交上一条任务失败。");
        }

        /*
 * 把当前任务的数据同步到右侧详情控件。
 *
 * 同步过程中不能把这些赋值误认为用户修改，
 * 所以暂时打开同步标记。
 */
        _isSynchronizingDueDate =
            true;

        try
        {
            /*
             * 截止日期。
             */
            SelectedDueDate =
                newValue?.DueAt?
                    .DateTime
                    .Date;

            /*
             * 截止时间。
             *
             * HasDueTime=false 时，
             * 即使 DueAt 内部是当天 00:00，
             * 详情中仍然应该显示“无”。
             */
            SelectedDueTime =
                newValue is not null &&
                newValue.HasDueTime &&
                newValue.DueAt.HasValue
                    ? newValue.DueAt.Value
                        .DateTime
                        .TimeOfDay
                    : null;

            /*
             * 提醒方式。
             */
            SelectedReminderOption =
                FindReminderOption(
                    newValue);

            /*
             * 循环方式。
             */
            SelectedRepeatType =
                newValue?.RepeatType ??
                TaskRepeatType.None;
        }
        finally
        {
            _isSynchronizingDueDate =
                false;
        }

        /*
         * 当前任务改变以后，
         * 三个控件的启用状态也需要立即刷新。
         */
        OnPropertyChanged(
            nameof(CanSetSelectedDueTime));

        OnPropertyChanged(
            nameof(CanSetSelectedReminder));

        OnPropertyChanged(
            nameof(CanSetSelectedRepeat));

        OnPropertyChanged(
            nameof(CanSetSelectedContinuous));

        if (newValue is not null)
        {
            newValue.PropertyChanged +=
                OnSelectedTaskPropertyChanged;
        }

        if (newValue is null)
        {
            _selectedTaskEditBaseline =
                null;

            _pendingAutoSaveFields =
                TaskEditFields.None;

            _selectedTaskHasConflict =
                false;
        }
        else if (_deferredTaskEdits.TryGetValue(
                     newValue.Id,
                     out DeferredTaskEditState?
                         deferredEdit))
        {
            _selectedTaskEditBaseline =
                deferredEdit.Baseline;

            _pendingAutoSaveFields =
                deferredEdit.DirtyFields;

            _selectedTaskHasConflict =
                true;
        }
        else
        {
            _selectedTaskEditBaseline =
                TaskEditBaseline.FromTask(
                    newValue);

            _pendingAutoSaveFields =
                TaskEditFields.None;

            _selectedTaskHasConflict =
                false;
        }

        OnPropertyChanged(
            nameof(HasSelectedTask));

        /*
         * 用户切换到另一条任务以后，
         * 新任务本身还没有发生新的编辑，
         * 因此所有任务和已完成页面
         * 都统一恢复为“当前无修改”。
         */
        if (!_isReloadingTasks &&
            newValue is not null)
        {
            StatusMessage =
                _selectedTaskHasConflict
                    ? "此任务存在其他窗口的同字段冲突；" +
                      "当前输入已保留，请继续编辑后重试"
                    : "当前无修改";
        }
    }

    /// <summary>
    /// 右侧详情修改截止日期。
    ///
    /// 如果任务同时设置了具体截止时间，
    /// 日期变化时继续保留当前选择的时间。
    /// </summary>
    partial void OnSelectedDueDateChanged(
        DateTime? value)
    {
        if (_isSynchronizingDueDate ||
            _isReloadingTasks ||
            SelectedTask is null)
        {
            return;
        }

        OnPropertyChanged(
            nameof(CanSetSelectedDueTime));

        OnPropertyChanged(
            nameof(CanSetSelectedReminder));

        OnPropertyChanged(
            nameof(CanSetSelectedRepeat));

        OnPropertyChanged(
            nameof(CanSetSelectedContinuous));

        /*
         * 用户清除了截止日期。
         *
         * 没有日期以后：
         *
         * 时间 → 无
         * 提醒 → 不提醒
         * 循环 → 不循环
         */
        if (!value.HasValue)
        {
            /*
             * 先同步 UI 代理属性。
             *
             * 使用同步标记，
             * 防止下面三个属性的变化再次反向修改任务。
             */
            _isSynchronizingDueDate =
                true;

            try
            {
                SelectedDueTime =
                    null;

                SelectedReminderOption =
                    ReminderOptions[0];

                SelectedRepeatType =
                    TaskRepeatType.None;
            }
            finally
            {
                _isSynchronizingDueDate =
                    false;
            }

        }

        ApplySelectedTaskScheduling(
            TaskEditFields.Schedule |
            TaskEditFields.Repeat);
    }

    /// <summary>
    /// 右侧详情修改具体截止时间。
    /// </summary>
    partial void OnSelectedDueTimeChanged(
        TimeSpan? value)
    {
        if (_isSynchronizingDueDate ||
            _isReloadingTasks ||
            SelectedTask is null)
        {
            return;
        }

        OnPropertyChanged(
            nameof(CanSetSelectedReminder));

        /*
         * 没有日期时不允许单独设置时间。
         */
        if (!SelectedDueDate.HasValue)
        {
            return;
        }

        /*
         * 用户把时间改成“无”。
         *
         * 没有具体时间就不能进行
         * “提前5分钟 / 到点提醒”等时间型提醒。
         */
        if (!value.HasValue)
        {
            _isSynchronizingDueDate =
                true;

            try
            {
                SelectedReminderOption =
                    ReminderOptions[0];
            }
            finally
            {
                _isSynchronizingDueDate =
                    false;
            }
        }

        ApplySelectedTaskScheduling(
            TaskEditFields.Schedule);
    }

    /// <summary>
    /// 右侧详情修改提醒方式。
    /// </summary>
    partial void OnSelectedReminderOptionChanged(
        TaskReminderOption? value)
    {
        if (_isSynchronizingDueDate ||
            _isReloadingTasks ||
            SelectedTask is null)
        {
            return;
        }

        ApplySelectedTaskScheduling(
            TaskEditFields.Schedule);
    }

    /// <summary>
    /// 右侧详情修改循环方式。
    /// </summary>
    partial void OnSelectedRepeatTypeChanged(
        TaskRepeatType value)
    {
        if (_isSynchronizingDueDate ||
            _isReloadingTasks ||
            SelectedTask is null)
        {
            return;
        }

        ApplySelectedTaskScheduling(
            TaskEditFields.Repeat);
    }

    /// <summary>
    /// 将右侧日期、时间、提醒和循环代理统一交给 TaskRules。
    /// </summary>
    private void ApplySelectedTaskScheduling(
        TaskEditFields changedFields)
    {
        TaskItem? task =
            SelectedTask;

        if (task is null)
        {
            return;
        }

        TaskReminderOption reminderOption =
            SelectedReminderOption ??
            ReminderOptions[0];

        TaskEditDraft draft =
            new(
                task.Title,
                task.Description,
                SelectedDueDate,
                SelectedDueTime,
                reminderOption.Enabled,
                reminderOption.MinutesBefore,
                SelectedRepeatType,
                task.IsContinuous,
                task.IsImportant,
                task.AssignedQuadrant);

        TaskEditResult result;

        _isSynchronizingDueDate =
            true;

        try
        {
            result =
                TaskRules.ApplyScheduling(
                    task,
                    draft);
        }
        finally
        {
            _isSynchronizingDueDate =
                false;
        }

        ScheduleAutoSave(
            task,
            result.DueDateChanged ||
                result.DueTimeChanged,
            changedFields);
    }

    /// <summary>
    /// 右侧详情中的可编辑字段发生变化时，
    /// 安排一次防抖自动保存。
    /// </summary>
    private void OnSelectedTaskPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_isSynchronizingDueDate ||
            _isReloadingTasks ||
            sender is not TaskItem task)
        {
            return;
        }

        TaskEditFields changedFields =
            e.PropertyName switch
            {
                nameof(TaskItem.Title) =>
                    TaskEditFields.Title,

                nameof(TaskItem.Description) =>
                    TaskEditFields.Description,

                nameof(TaskItem.DueAt) or
                nameof(TaskItem.HasDueTime) or
                nameof(TaskItem.ReminderEnabled) or
                nameof(TaskItem.ReminderMinutesBefore) =>
                    TaskEditFields.Schedule,

                nameof(TaskItem.IsContinuous) =>
                    TaskEditFields.Schedule,

                nameof(TaskItem.RepeatType) or
                nameof(TaskItem.RecurrenceSeriesId) =>
                    TaskEditFields.Repeat,

                nameof(TaskItem.IsImportant) =>
                    TaskEditFields.IsImportant,

                nameof(TaskItem.AssignedQuadrant) or
                nameof(TaskItem.ManualQuadrant) or
                nameof(TaskItem.QuadrantMode) or
                nameof(TaskItem.Priority) =>
                    TaskEditFields.Quadrant,

                _ =>
                    TaskEditFields.None
            };

        if (changedFields ==
            TaskEditFields.None)
        {
            return;
        }

        bool requiresRegroup =
            e.PropertyName ==
                nameof(TaskItem.DueAt) ||
            e.PropertyName ==
                nameof(TaskItem.HasDueTime) ||
            e.PropertyName ==
                nameof(TaskItem.IsContinuous);

        ScheduleAutoSave(
            task,
            requiresRegroup,
            changedFields);
    }

    /// <summary>
    /// 重新启动 600ms 防抖计时器。
    /// </summary>
    private void ScheduleAutoSave(
        TaskItem task,
        bool requiresRegroup,
        TaskEditFields changedFields)
    {
        if (_selectedTaskEditBaseline is null ||
            _selectedTaskEditBaseline.Id !=
                task.Id)
        {
            _selectedTaskEditBaseline =
                TaskEditBaseline.FromTask(
                    task);
        }

        _pendingAutoSaveTask =
            task;

        _pendingAutoSaveRequiresRegroup |=
            requiresRegroup;

        _pendingAutoSaveFields |=
            changedFields;

        _selectedTaskHasConflict =
            false;

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();

        StatusMessage =
            "正在等待自动保存";
    }

    private async void OnAutoSaveTimerTick(
        object? sender,
        EventArgs e)
    {
        try
        {
            await FlushPendingAutoSaveAsync();
        }
        catch (Exception exception)
        {
            /*
             * FlushPendingAutoSaveAsync 内部通常会记录错误。
             * 此处仍保留最终防线，避免 async void 异常上抛。
             */
            AppLog.Error(
                "自动保存计时器执行失败。",
                exception);

            StatusMessage =
                $"自动保存失败：" +
                $"{exception.Message}";
        }
    }

    /// <summary>
    /// 取出等待中的任务并立即保存。
    /// </summary>
    private async Task<bool> FlushPendingAutoSaveAsync()
    {
        _autoSaveTimer.Stop();

        while (true)
        {
            Task<bool>? activeSaveTask =
                _activeAutoSaveTask;

            if (activeSaveTask is not null)
            {
                if (!await activeSaveTask)
                {
                    return false;
                }

                /*
                 * 数据库写入期间可能产生了更新的输入，
                 * 因而必须继续检查下一轮 pending。
                 */
                continue;
            }

            TaskItem? task =
                _pendingAutoSaveTask;

            bool requiresRegroup =
                _pendingAutoSaveRequiresRegroup;

            TaskEditFields fieldsToSave =
                _pendingAutoSaveFields;

            TaskEditBaseline? baselineToSave =
                task is not null &&
                _selectedTaskEditBaseline?.Id ==
                    task.Id
                    ? _selectedTaskEditBaseline
                    : task is not null &&
                      _deferredTaskEdits.TryGetValue(
                          task.Id,
                          out DeferredTaskEditState?
                              deferredEdit)
                        ? deferredEdit.Baseline
                        : task is not null
                            ? TaskEditBaseline.FromTask(
                                task)
                            : null;

            _pendingAutoSaveTask =
                null;

            _pendingAutoSaveRequiresRegroup =
                false;

            _pendingAutoSaveFields =
                TaskEditFields.None;

            if (task is null ||
                baselineToSave is null ||
                fieldsToSave ==
                    TaskEditFields.None)
            {
                return !_selectedTaskHasConflict;
            }

            TaskEditDraft draft =
                TaskEditDraft.FromTask(
                    task);

            Task<bool> saveTask =
                AutoSaveTaskAsync(
                    task,
                    draft,
                    baselineToSave,
                    fieldsToSave,
                    requiresRegroup);

            _activeAutoSaveTask =
                saveTask;

            bool succeeded;

            try
            {
                succeeded =
                    await saveTask;
            }
            finally
            {
                if (ReferenceEquals(
                        _activeAutoSaveTask,
                        saveTask))
                {
                    _activeAutoSaveTask =
                        null;
                }
            }

            if (!succeeded)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 将右侧详情的修改写入数据库。
    ///
    /// 自动保存期间把当前 ViewModel 标记为忙碌，
    /// 防止 TasksChanged 事件重新加载当前任务页面。
    ///
    /// 否则 SelectedTask 会被新的 TaskItem 对象替换，
    /// 导致 TextBox 的输入光标跳回开头。
    /// </summary>
    private async Task<bool> AutoSaveTaskAsync(
        TaskItem task,
        TaskEditDraft draft,
        TaskEditBaseline baselineToSave,
        TaskEditFields fieldsToSave,
        bool requiresRegroup)
    {
        /*
         * 用户可能正在清空标题后重新输入。
         * 标题为空时先不写数据库，继续保留界面内容。
         */
        if (string.IsNullOrWhiteSpace(
                draft.Title))
        {
            RestorePendingEdit(
                task,
                baselineToSave,
                fieldsToSave,
                requiresRegroup);

            StatusMessage =
                "任务标题不能为空，当前修改尚未保存";

            return false;
        }

        /*
         * 已经删除的旧任务对象不能再执行自动保存。
         */
        if (_deletedTaskIds.Contains(
                task.Id))
        {
            return false;
        }

        await _saveGate
            .WaitAsync();

        /*
         * 保存进入前记录原来的忙碌状态。
         *
         * 正常自动保存时通常为 false；
         * 使用原值恢复可以避免意外覆盖其他操作状态。
         */
        bool previousIsBusy =
            IsBusy;

        IsBusy =
            true;

        try
        {
            /*
             * 等待写入锁期间任务可能已经被删除，
             * 因此真正写数据库前再次检查。
             */
            if (_deletedTaskIds.Contains(
                    task.Id))
            {
                return false;
            }

            TaskEditSaveResult saveResult =
                await _taskService
                .SaveTaskEditAsync(
                    new TaskEditRequest(
                        baselineToSave,
                        draft,
                        fieldsToSave),
                    changeSource:
                        ChangeSource);

            TaskEditFields newerFields =
                ReferenceEquals(
                    _pendingAutoSaveTask,
                    task)
                    ? _pendingAutoSaveFields
                    : TaskEditFields.None;

            if (!saveResult.IsSaved)
            {
                TaskEditFields dirtyFields =
                    fieldsToSave |
                    newerFields;

                TaskEditBaseline conflictBaseline =
                    saveResult.Current ??
                    baselineToSave;

                if (saveResult.Current is not null)
                {
                    ApplyDatabaseState(
                        saveResult.Current,
                        task,
                        dirtyFields);
                }

                _deferredTaskEdits[task.Id] =
                    new DeferredTaskEditState(
                        conflictBaseline,
                        dirtyFields);

                if (SelectedTask?.Id ==
                    task.Id)
                {
                    _selectedTaskEditBaseline =
                        conflictBaseline;

                    _pendingAutoSaveFields =
                        dirtyFields;

                    _selectedTaskHasConflict =
                        true;

                    if (ReferenceEquals(
                            _pendingAutoSaveTask,
                            task))
                    {
                        _pendingAutoSaveTask =
                            null;

                        _pendingAutoSaveRequiresRegroup =
                            false;
                    }

                    _autoSaveTimer.Stop();

                    StatusMessage =
                        saveResult.Current is null
                            ? "任务已在其他窗口完成或删除；" +
                              "当前输入未保存"
                            : "检测到其他窗口修改了相同内容；" +
                              "当前输入已保留，请继续编辑后重试";
                }

                return false;
            }

            TaskEditBaseline savedState =
                saveResult.Current!;

            bool dueDateChanged =
                baselineToSave.DueAt?
                    .DateTime
                    .Date !=
                savedState.DueAt?
                    .DateTime
                    .Date;

            ApplyDatabaseState(
                savedState,
                task,
                newerFields);

            _deferredTaskEdits.Remove(
                task.Id);

            if (SelectedTask?.Id ==
                task.Id)
            {
                _selectedTaskEditBaseline =
                    savedState;

                _selectedTaskHasConflict =
                    false;
            }

            /*
             * 截止日期发生变化时，
             * 只重新生成当前内存中的日期分组。
             *
             * 不从数据库重新读取任务，
             * 因此不会替换 SelectedTask，
             * 也不会破坏 TextBox 的光标位置。
             */
            if ((requiresRegroup ||
                 dueDateChanged) &&
                _pendingAutoSaveTask is null &&
                Tasks.Any(
                    item =>
                        item.Id ==
                        task.Id))
            {
                RebuildTaskEntries(
                    task.Id);
            }
            else if ((requiresRegroup ||
                      dueDateChanged) &&
                     ReferenceEquals(
                         _pendingAutoSaveTask,
                         task))
            {
                _pendingAutoSaveRequiresRegroup =
                    true;
            }

            StatusMessage =
                _pendingAutoSaveTask is null
                    ? saveResult.WasMerged
                        ? "已自动合并其他窗口的修改"
                        : "修改已自动保存"
                    : "正在等待自动保存";

            return true;
        }
        catch (Exception exception)
        {
            RestorePendingEdit(
                task,
                baselineToSave,
                fieldsToSave,
                requiresRegroup);

            AppLog.Error(
                "自动保存任务失败。",
                exception);

            StatusMessage =
                $"自动保存失败：" +
                $"{exception.Message}";

            return false;
        }
        finally
        {
            /*
             * 必须恢复进入方法前的状态，
             * 不能无条件设置为 false。
             */
            IsBusy =
                previousIsBusy;

            _saveGate.Release();
        }
    }

    /// <summary>
    /// 供页面导航和应用退出生命周期使用的统一保存协议。
    /// </summary>
    public async Task<FlushResult>
        FlushPendingChangesAsync()
    {
        try
        {
            bool saved =
                await FlushPendingAutoSaveAsync();

            return saved
                ? FlushResult.Success()
                : FlushResult.Blocked(
                    string.IsNullOrWhiteSpace(
                        StatusMessage)
                        ? "任务详情无法保存"
                        : StatusMessage);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "提交任务列表详情失败。",
                exception);

            return FlushResult.Failed(
                $"任务详情保存失败：" +
                $"{exception.Message}");
        }
    }

    /// <summary>
    /// 用户明确放弃时，将内存对象恢复到各自最近一次确认的
    /// 数据库版本，并清除防抖、冲突和延期保存状态。
    /// </summary>
    public void DiscardPendingChanges()
    {
        _autoSaveTimer.Stop();

        bool previousReloadingState =
            _isReloadingTasks;

        _isReloadingTasks =
            true;

        try
        {
            foreach ((string taskId,
                      DeferredTaskEditState editState)
                     in _deferredTaskEdits)
            {
                TaskItem? task =
                    Tasks.FirstOrDefault(
                        item =>
                            item.Id == taskId);

                if (task is not null)
                {
                    editState.Baseline.ApplyTo(
                        task);
                }
            }

            if (_pendingAutoSaveTask is not null &&
                _selectedTaskEditBaseline is not null &&
                _pendingAutoSaveTask.Id ==
                    _selectedTaskEditBaseline.Id)
            {
                _selectedTaskEditBaseline.ApplyTo(
                    _pendingAutoSaveTask);
            }

            _pendingAutoSaveTask =
                null;

            _pendingAutoSaveRequiresRegroup =
                false;

            _pendingAutoSaveFields =
                TaskEditFields.None;

            _deferredTaskEdits.Clear();

            _selectedTaskHasConflict =
                false;

            _selectedTaskEditBaseline =
                SelectedTask is null
                    ? null
                    : TaskEditBaseline.FromTask(
                        SelectedTask);

            if (SelectedTask is not null)
            {
                ApplyDatabaseState(
                    _selectedTaskEditBaseline!,
                    SelectedTask,
                    TaskEditFields.None);
            }

            RebuildTaskEntries(
                SelectedTask?.Id);

            StatusMessage =
                "已放弃未保存的修改";
        }
        finally
        {
            _isReloadingTasks =
                previousReloadingState;
        }
    }

    /// <summary>
    /// 将数据库最新状态应用到任务对象，同时保护仍在输入的字段。
    /// </summary>
    private void ApplyDatabaseState(
        TaskEditBaseline state,
        TaskItem task,
        TaskEditFields preserveFields)
    {
        bool previousSynchronizingState =
            _isSynchronizingDueDate;

        _isSynchronizingDueDate =
            true;

        try
        {
            state.ApplyTo(
                task,
                preserveFields);

            if (SelectedTask?.Id ==
                task.Id)
            {
                SelectedDueDate =
                    task.DueAt?
                        .DateTime
                        .Date;

                SelectedDueTime =
                    task.HasDueTime &&
                    task.DueAt.HasValue
                        ? task.DueAt.Value
                            .DateTime
                            .TimeOfDay
                        : null;

                SelectedReminderOption =
                    FindReminderOption(
                        task);

                SelectedRepeatType =
                    task.RepeatType;
            }
        }
        finally
        {
            _isSynchronizingDueDate =
                previousSynchronizingState;
        }

        OnPropertyChanged(
            nameof(CanSetSelectedDueTime));

        OnPropertyChanged(
            nameof(CanSetSelectedReminder));

        OnPropertyChanged(
            nameof(CanSetSelectedRepeat));
    }

    private void RestorePendingEdit(
        TaskItem task,
        TaskEditBaseline baseline,
        TaskEditFields fields,
        bool requiresRegroup)
    {
        _deferredTaskEdits[task.Id] =
            new DeferredTaskEditState(
                baseline,
                fields);

        if (SelectedTask?.Id !=
            task.Id)
        {
            return;
        }

        _selectedTaskEditBaseline =
            baseline;

        _pendingAutoSaveFields |=
            fields;

        if (_pendingAutoSaveTask is null ||
            ReferenceEquals(
                _pendingAutoSaveTask,
                task))
        {
            _pendingAutoSaveTask =
                task;

            _pendingAutoSaveRequiresRegroup |=
                requiresRegroup;
        }
    }

    private void CancelPendingAutoSave(
    TaskItem task)
    {
        if (ReferenceEquals(
                _pendingAutoSaveTask,
                task))
        {
            _autoSaveTimer.Stop();

            _pendingAutoSaveTask =
                null;

            _pendingAutoSaveRequiresRegroup =
                false;
        }

        _deferredTaskEdits.Remove(
            task.Id);

        if (SelectedTask?.Id ==
            task.Id)
        {
            _pendingAutoSaveFields =
                TaskEditFields.None;

            _selectedTaskHasConflict =
                false;
        }
    }

    private sealed record DeferredTaskEditState(
        TaskEditBaseline Baseline,
        TaskEditFields DirtyFields);

}
