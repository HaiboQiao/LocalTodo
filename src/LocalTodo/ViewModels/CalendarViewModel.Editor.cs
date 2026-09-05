using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

public partial class CalendarViewModel
{
    #region 任务详情编辑

    /// <summary>
    /// 当前正在编辑的原始任务对象。
    ///
    /// TextBox 不直接绑定这个对象的属性，
    /// 而是绑定下面的 Editor... 缓冲属性。
    /// 因此日历刷新任务集合时不会打断输入光标。
    /// </summary>
    [ObservableProperty]
    private TaskItem? editingTask;

    [ObservableProperty]
    private string editorTitle =
        string.Empty;

    [ObservableProperty]
    private string editorDescription =
        string.Empty;

    [ObservableProperty]
    private DateTime? editorDueDate;

    /// <summary>
    /// 当前任务详情中的具体截止时间。
    /// null 表示只有截止日期，没有设置具体时分。
    /// </summary>
    [ObservableProperty]
    private TimeSpan? editorDueTime;

    /// <summary>
    /// 当前任务详情选择的提醒方式。
    /// </summary>
    [ObservableProperty]
    private TaskReminderOption?
        editorReminderOption;

    /// <summary>
    /// 当前任务详情选择的循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType
        editorRepeatType =
            TaskRepeatType.None;

    [ObservableProperty]
    private bool editorIsImportant;

    [ObservableProperty]
    private bool editorIsContinuous;

    [ObservableProperty]
    private QuadrantType editorQuadrant =
        QuadrantType
            .NotImportantNotUrgent;

    public bool HasEditingTask =>
        EditingTask is not null;

    /// <summary>
    /// 有截止日期后才允许设置具体截止时间。
    /// </summary>
    public bool CanSetEditorDueTime =>
        EditorDueDate.HasValue;

    /// <summary>
    /// 同时有日期和具体时间后才允许设置提醒。
    /// </summary>
    public bool CanSetEditorReminder =>
        EditorDueDate.HasValue &&
        EditorDueTime.HasValue;

    /// <summary>
    /// 有截止日期后才允许设置循环。
    /// </summary>
    public bool CanSetEditorRepeat =>
        EditorDueDate.HasValue;

    public bool CanSetEditorContinuous =>
        EditorDueDate.HasValue;

    /// <summary>
    /// 当前是否正在由日历任务详情执行本地保存。
    ///
    /// 供 MainWindowViewModel 判断是否应该响应
    /// TaskService.TasksChanged 并重新加载 Calendar。
    /// </summary>
    public bool IsSavingTaskEditorLocally =>
        _isSavingTaskEditorLocally;

    #endregion
    #region 任务详情编辑

    /// <summary>
    /// 把任务内容复制到独立的编辑缓冲区。
    /// </summary>
    public void OpenTaskEditor(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        _taskEditorAutoSave.StopDebounce();

        _hasPendingTaskEditorSave =
            false;

        _taskEditorBaseline =
            TaskEditBaseline.FromTask(task);

        _taskEditorDirtyFields =
            TaskEditFields.None;

        _taskEditorHasConflict =
            false;

        _isLoadingTaskEditor =
            true;

        try
        {
            EditingTask =
                task;

            EditorTitle =
                task.Title;

            EditorDescription =
                task.Description;

            EditorDueDate =
                task.DueAt?
                    .DateTime
                    .Date;

            /*
             * HasDueTime=false 时，
             * 即使 DueAt 内部是当天 00:00，
             * 详情仍然显示“无”。
             */
            EditorDueTime =
                task.HasDueTime &&
                task.DueAt.HasValue
                    ? task.DueAt.Value
                        .DateTime
                        .TimeOfDay
                    : null;

            EditorReminderOption =
                FindReminderOption(
                    task);

            EditorRepeatType =
                task.RepeatType;

            EditorIsImportant =
                task.IsImportant;

            EditorIsContinuous =
                task.IsContinuous;

            EditorQuadrant =
                task.AssignedQuadrant;
        }
        finally
        {
            _isLoadingTaskEditor =
                false;
        }

        OnPropertyChanged(
            nameof(CanSetEditorDueTime));

        OnPropertyChanged(
            nameof(CanSetEditorReminder));

        OnPropertyChanged(
            nameof(CanSetEditorRepeat));

        OnPropertyChanged(
            nameof(CanSetEditorContinuous));

        StatusMessage =
            "当前无修改";
    }

    /// <summary>
    /// 清除任务详情编辑状态。
    /// 调用前应先提交等待中的自动保存。
    /// </summary>
    public void CloseTaskEditor()
    {
        _taskEditorAutoSave.StopDebounce();

        _hasPendingTaskEditorSave =
            false;

        _taskEditorBaseline =
            null;

        _taskEditorDirtyFields =
            TaskEditFields.None;

        _taskEditorHasConflict =
            false;

        _isLoadingTaskEditor =
            true;

        try
        {
            EditingTask =
                null;

            EditorTitle =
                string.Empty;

            EditorDescription =
                string.Empty;

            EditorDueDate =
                null;

            EditorDueTime =
                null;

            EditorReminderOption =
                ReminderOptions[0];

            EditorRepeatType =
                TaskRepeatType.None;

            EditorIsImportant =
                false;

            EditorIsContinuous =
                false;

            EditorQuadrant =
                QuadrantType
                    .NotImportantNotUrgent;
        }
        finally
        {
            _isLoadingTaskEditor =
                false;
        }

        OnPropertyChanged(
            nameof(CanSetEditorDueTime));

        OnPropertyChanged(
            nameof(CanSetEditorReminder));

        OnPropertyChanged(
            nameof(CanSetEditorRepeat));

        OnPropertyChanged(
            nameof(CanSetEditorContinuous));
    }

    /// <summary>
    /// 安排一次 600 毫秒防抖自动保存。
    /// </summary>
    private void ScheduleTaskEditorAutoSave(
        TaskEditFields changedFields)
    {
        if (_isLoadingTaskEditor ||
            EditingTask is null)
        {
            return;
        }

        _taskEditorAutoSave.Schedule(
            changedFields);

        StatusMessage =
            "正在等待自动保存";
    }

    private void OnTaskEditorAutoSaveFailed(
        Exception exception)
    {
        AppLog.Error(
            "日历任务详情自动保存计时器执行失败。",
            exception);

        StatusMessage =
            $"自动保存失败：" +
            $"{exception.Message}";
    }

    /// <summary>
    /// 立即提交任务详情中所有尚未完成的修改。
    ///
    /// 不仅处理“等待中的保存”，
    /// 也会等待已经开始但尚未结束的数据库保存。
    ///
    /// 如果保存过程中用户又继续输入，
    /// 会继续循环提交最新一轮修改，
    /// 直到没有任何待保存内容。
    /// </summary>
    public async Task<bool>
        FlushTaskEditorAutoSaveAsync()
    {
        return await _taskEditorAutoSave
            .FlushAsync();
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
                await FlushTaskEditorAutoSaveAsync();

            return saved
                ? FlushResult.Success()
                : FlushResult.Blocked(
                    string.IsNullOrWhiteSpace(
                        StatusMessage)
                        ? "日历任务详情无法保存"
                        : StatusMessage);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "提交日历任务详情失败。",
                exception);

            return FlushResult.Failed(
                $"日历任务详情保存失败：" +
                $"{exception.Message}");
        }
    }

    public void DiscardPendingChanges()
    {
        CloseTaskEditor();
    }

    /// <summary>
    /// 把编辑缓冲区写回任务并保存到数据库。
    /// </summary>
    private async Task<bool>
        AutoSaveTaskEditorAsync()
    {
        if (EditingTask is null)
        {
            return true;
        }

        if (_taskEditorBaseline is null ||
            _taskEditorDirtyFields ==
                TaskEditFields.None)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(
                EditorTitle))
        {
            _hasPendingTaskEditorSave =
                true;

            StatusMessage =
                "任务标题不能为空，当前修改尚未保存";

            return false;
        }

        /*
         * 在真正等待数据库写锁之前，
         * 先复制本轮准备保存的编辑缓冲值。
         *
         * 保存过程中用户可以继续输入，
         * 新修改会由下一轮防抖保存继续提交。
         */
        TaskReminderOption
            reminderOptionToSave =
                EditorReminderOption ??
                ReminderOptions[0];

        TaskEditDraft draft =
            new(
                EditorTitle,
                EditorDescription,
                EditorDueDate,
                EditorDueTime,
                reminderOptionToSave.Enabled,
                reminderOptionToSave.MinutesBefore,
                EditorRepeatType,
                EditorIsContinuous,
                EditorIsImportant,
                EditorQuadrant);

        TaskItem taskToSave =
            EditingTask;

        string taskId =
            taskToSave.Id;

        TaskEditBaseline baselineToSave =
            _taskEditorBaseline;

        TaskEditFields fieldsToSave =
            _taskEditorDirtyFields;

        /*
         * 保存期间产生的新输入会重新累积到这个字段集合，
         * 因而不会被本轮成功结果误认为已经保存。
         */
        _taskEditorDirtyFields =
            TaskEditFields.None;

        await _taskEditorSaveGate
            .WaitAsync();

        try
        {
            _isSavingTaskEditorLocally =
                true;

            TaskEditSaveResult saveResult;

            try
            {
                saveResult =
                    await _taskService
                    .SaveTaskEditAsync(
                        new TaskEditRequest(
                            baselineToSave,
                            draft,
                            fieldsToSave),
                        changeSource:
                            TaskChangeSource.Calendar);
            }
            finally
            {
                _isSavingTaskEditorLocally =
                    false;
            }

            if (!saveResult.IsSaved)
            {
                _taskEditorDirtyFields |=
                    fieldsToSave;

                _taskEditorHasConflict =
                    true;

                if (saveResult.Current is not null)
                {
                    saveResult.Current.ApplyTo(
                        taskToSave);

                    _taskEditorBaseline =
                        saveResult.Current;

                    StatusMessage =
                        "检测到其他窗口修改了相同内容；" +
                        "当前输入已保留，请继续编辑后重试";
                }
                else
                {
                    StatusMessage =
                        "任务已在其他窗口完成或删除；" +
                        "当前输入未保存";
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

            savedState.ApplyTo(
                taskToSave);

            _taskEditorBaseline =
                savedState;

            _taskEditorHasConflict =
                false;

            /*
             * 本地日期或合并进来的其他窗口日期发生变化时，
             * 任务需要移动到正确的日期格。
             */
            if (dueDateChanged)
            {
                await LoadAsync();

                TaskItem? refreshedTask =
                    FindTaskById(
                        taskId);

                if (refreshedTask is not null &&
                    EditingTask?.Id ==
                        taskId)
                {
                    EditingTask =
                        refreshedTask;
                }
            }

            StatusMessage =
                saveResult.WasMerged
                    ? "已自动合并其他窗口的修改"
                    : "修改已自动保存";

            return true;
        }
        catch (Exception exception)
        {
            _taskEditorDirtyFields |=
                fieldsToSave;

            _hasPendingTaskEditorSave =
                true;

            AppLog.Error(
                "自动保存日历任务详情失败。",
                exception);

            StatusMessage =
                $"自动保存失败：" +
                $"{exception.Message}";

            return false;
        }
        finally
        {
            _taskEditorSaveGate
                .Release();
        }
    }

    /// <summary>
    /// 把当前任务软删除到垃圾箱。
    /// </summary>
    public async Task<bool>
        DeleteEditingTaskAsync()
    {
        if (IsBusy ||
            EditingTask is null)
        {
            return false;
        }

        TaskItem taskToDelete =
            EditingTask;

        TaskDeleteChoice deleteChoice =
            _dialogService
                .GetTaskDeleteChoice(
                    taskToDelete);

        if (deleteChoice ==
            TaskDeleteChoice.Cancel)
        {
            return false;
        }

        _taskEditorAutoSave.StopDebounce();

        _hasPendingTaskEditorSave =
            false;

        await _taskEditorSaveGate
            .WaitAsync();

        IsBusy =
            true;

        try
        {
            await _taskService
                .DeleteTaskWithChoiceByIdAsync(
                    taskToDelete.Id,
                    deleteChoice,
                    changeSource:
                        TaskChangeSource.Calendar);

            CloseTaskEditor();

            await LoadAsync();

            StatusMessage =
                $"已删除任务：" +
                $"{taskToDelete.Title}";

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "在日历中删除任务失败。",
                exception);

            StatusMessage =
                exception.Message;

            return false;
        }
        finally
        {
            IsBusy =
                false;

            _taskEditorSaveGate
                .Release();
        }
    }

    partial void OnEditingTaskChanged(
        TaskItem? value)
    {
        OnPropertyChanged(
            nameof(HasEditingTask));
    }

    partial void OnEditorTitleChanged(
        string value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Title);
    }

    partial void OnEditorDescriptionChanged(
        string value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Description);
    }

    partial void OnEditorDueDateChanged(
        DateTime? value)
    {
        OnPropertyChanged(
            nameof(CanSetEditorDueTime));

        OnPropertyChanged(
            nameof(CanSetEditorReminder));

        OnPropertyChanged(
            nameof(CanSetEditorRepeat));

        OnPropertyChanged(
            nameof(CanSetEditorContinuous));

        /*
         * 用户清除截止日期后，
         * 具体时间、提醒和循环都失去业务意义，
         * 因此同步恢复默认值。
         */
        if (!_isLoadingTaskEditor &&
            !value.HasValue)
        {
            _isLoadingTaskEditor =
                true;

            try
            {
                EditorDueTime =
                    null;

                EditorReminderOption =
                    ReminderOptions[0];

                EditorRepeatType =
                    TaskRepeatType.None;

                EditorIsContinuous =
                    false;
            }
            finally
            {
                _isLoadingTaskEditor =
                    false;
            }
        }

        ScheduleTaskEditorAutoSave(
            TaskEditFields.Schedule |
            TaskEditFields.Repeat);
    }

    partial void OnEditorDueTimeChanged(
        TimeSpan? value)
    {
        OnPropertyChanged(
            nameof(CanSetEditorReminder));

        /*
         * 用户把具体截止时间改成“无”以后，
         * 时间型提醒必须自动恢复成“不提醒”。
         */
        if (!_isLoadingTaskEditor &&
            !value.HasValue)
        {
            _isLoadingTaskEditor =
                true;

            try
            {
                EditorReminderOption =
                    ReminderOptions[0];
            }
            finally
            {
                _isLoadingTaskEditor =
                    false;
            }
        }

        ScheduleTaskEditorAutoSave(
            TaskEditFields.Schedule);
    }

    partial void
        OnEditorReminderOptionChanged(
            TaskReminderOption? value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Schedule);
    }

    partial void OnEditorRepeatTypeChanged(
        TaskRepeatType value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Repeat);
    }

    partial void OnEditorIsImportantChanged(
        bool value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.IsImportant);
    }

    partial void OnEditorIsContinuousChanged(
        bool value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Schedule);
    }

    partial void OnEditorQuadrantChanged(
        QuadrantType value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Quadrant);
    }

    #endregion
}
