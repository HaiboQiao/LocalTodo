using System;
using System.Linq;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

public abstract partial class MatrixViewModel
{
    #region 任务详情编辑

    /// <summary>
    /// 把任务内容复制到弹窗编辑缓冲区。
    /// </summary>
    public void OpenTaskEditor(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        /*
         * 打开新任务时取消旧的等待保存状态。
         * 调用方应当先提交上一个任务的修改。
         */
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
                task.Title ??
                string.Empty;

            EditorDescription =
                task.Description ??
                string.Empty;

            EditorDueDate =
                task.DueAt?
                    .DateTime
                    .Date;

            /*
             * HasDueTime=false 时，
             * 即使 DueAt 内部保存的是当天 00:00，
             * 详情下拉框也应该显示“无”。
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
                GetTaskQuadrant(
                    task);
        }
        finally
        {
            _isLoadingTaskEditor =
                false;
        }

        StatusMessage =
            "当前无修改";
    }

    /// <summary>
    /// 清除任务详情编辑状态。
    ///
    /// 调用这个方法前，应当先执行
    /// FlushTaskEditorAutoSaveAsync。
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
    }

    /// <summary>
    /// 安排一次防抖自动保存。
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
            "四象限任务详情自动保存计时器执行失败。",
            exception);

        StatusMessage =
            $"自动保存失败：" +
            $"{exception.Message}";
    }

    /// <summary>
    /// 立即提交等待中的详情修改。
    ///
    /// 弹窗关闭、切换任务、打开新增窗口前，
    /// 都要调用这个方法。
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
                        ? "四象限任务详情无法保存"
                        : StatusMessage);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "提交四象限任务详情失败。",
                exception);

            return FlushResult.Failed(
                $"四象限任务详情保存失败：" +
                $"{exception.Message}");
        }
    }

    public void DiscardPendingChanges()
    {
        CloseTaskEditor();
    }

    /// <summary>
    /// 将编辑缓冲区写回任务并保存数据库。
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

        /*
         * 标题临时为空时不写数据库。
         * 保留编辑内容，等待用户继续输入。
         */
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
         * 先复制当前编辑值。
         * 用户在数据库写入过程中继续输入时，
         * 新内容会再次进入下一次自动保存。
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

        _taskEditorDirtyFields =
            TaskEditFields.None;

        await _taskEditorSaveGate
            .WaitAsync();

        IsBusy =
            true;

        try
        {
            TaskEditSaveResult saveResult =
                await _taskService
                .SaveTaskEditAsync(
                    new TaskEditRequest(
                        baselineToSave,
                        draft,
                        fieldsToSave),
                    changeSource:
                        _changeSource);

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

            savedState.ApplyTo(
                taskToSave);

            _taskEditorBaseline =
                savedState;

            _taskEditorHasConflict =
                false;

            /*
             * 重新读取四个象限，
             * 让象限移动、日期分组、数量和排序立即更新。
             *
             * 编辑字段使用独立缓冲区，
             * 因此重新读取集合不会覆盖用户仍在输入的文字。
             */
            await ReloadSnapshotAsync();

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
                "自动保存四象限任务详情失败。",
                exception);

            StatusMessage =
                $"自动保存失败：" +
                $"{exception.Message}";

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

    /// <summary>
    /// 删除当前弹窗中的任务。
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

        /*
         * 删除前取消等待中的普通自动保存，
         * 防止任务删除后又被延迟保存。
         */
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
                        _changeSource);

            CloseTaskEditor();

            await ReloadSnapshotAsync();

            StatusMessage =
                $"已删除任务：" +
                $"{taskToDelete.Title}";

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "在四象限中删除任务失败。",
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
         * 清除截止日期时，
         * 时间、提醒和循环一起恢复为无效状态。
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
         * 没有具体截止时间以后，
         * 时间型提醒必须自动恢复为“不提醒”。
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

    partial void OnEditorReminderOptionChanged(
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
