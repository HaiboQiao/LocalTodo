using System;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

public partial class DesktopTaskListViewModel
{
    #region 任务详情编辑

    /// <summary>
    /// 将任务内容复制到独立编辑缓冲区。
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

            EditorIsContinuous =
                task.IsContinuous;

            EditorIsImportant =
                task.IsImportant;

            EditorQuadrant =
                task.AssignedQuadrant;
        }
        finally
        {
            _isLoadingTaskEditor =
                false;
        }

        StatusMessage =
            "当前无修改";
    }

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

            EditorIsContinuous =
                false;

            EditorIsImportant =
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

    private void
        ScheduleTaskEditorAutoSave(
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
            "桌面任务详情自动保存计时器执行失败。",
            exception);

        StatusMessage =
            $"自动保存失败：" +
            $"{exception.Message}";
    }

    /// <summary>
    /// 等待正在执行的保存，并提交所有尚未保存的编辑。
    /// </summary>
    public async Task<bool>
        FlushTaskEditorAutoSaveAsync()
    {
        return await _taskEditorAutoSave
            .FlushAsync();
    }

    /// <summary>
    /// 供桌面窗口隐藏和应用退出生命周期使用的统一保存协议。
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
                        ? "桌面任务详情无法保存"
                        : StatusMessage);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "提交桌面任务详情失败。",
                exception);

            return FlushResult.Failed(
                $"桌面任务详情保存失败：" +
                $"{exception.Message}");
        }
    }

    public void DiscardPendingChanges()
    {
        CloseTaskEditor();
    }

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

        TaskItem taskToSave =
            EditingTask;

        TaskEditDraft draft =
            TaskEditDraft
                .FromTask(
                    taskToSave) with
            {
                Title = EditorTitle,
                Description = EditorDescription,
                DueDate = EditorDueDate,
                IsContinuous = EditorIsContinuous,
                IsImportant = EditorIsImportant,
                Quadrant = EditorQuadrant
            };

        TaskEditBaseline baselineToSave =
            _taskEditorBaseline;

        TaskEditFields fieldsToSave =
            _taskEditorDirtyFields;

        _taskEditorDirtyFields =
            TaskEditFields.None;

        await _taskEditorSaveGate
            .WaitAsync();

        try
        {
            _isPerformingLocalChange =
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
                            TaskChangeSource
                                .DesktopTaskList);
            }
            finally
            {
                _isPerformingLocalChange =
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
             * 标题、说明、象限、星标不会改变日期组。
             * 只有本地或合并进来的截止日期变化才重新生成分组。
             */
            if (dueDateChanged)
            {
                RebuildTaskEntries();
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
                "自动保存桌面任务详情失败。",
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
    /// 删除当前编辑任务到垃圾箱。
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
         * 删除后不再需要提交尚未执行的编辑。
         */
        _taskEditorAutoSave.StopDebounce();

        _hasPendingTaskEditorSave =
            false;

        IsBusy =
            true;

        await _taskEditorSaveGate
            .WaitAsync();

        try
        {
            _isPerformingLocalChange =
                true;

            try
            {
                await _taskService
                    .DeleteTaskWithChoiceByIdAsync(
                        taskToDelete.Id,
                        deleteChoice,
                        changeSource:
                            TaskChangeSource
                                .DesktopTaskList);
            }
            finally
            {
                _isPerformingLocalChange =
                    false;
            }

            CloseTaskEditor();

            await ReloadTasksAsync();

            StatusMessage =
                $"已删除任务：" +
                $"{taskToDelete.Title}";

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "桌面任务列表删除任务失败。",
                exception);

            StatusMessage =
                exception.Message;

            return false;
        }
        finally
        {
            _taskEditorSaveGate
                .Release();

            IsBusy =
                false;
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
            nameof(CanSetEditorContinuous));

        if (!value.HasValue &&
            EditorIsContinuous)
        {
            EditorIsContinuous =
                false;
        }

        ScheduleTaskEditorAutoSave(
            TaskEditFields.Schedule |
            TaskEditFields.Repeat);
    }

    partial void OnEditorIsContinuousChanged(
        bool value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Schedule);
    }

    partial void OnEditorIsImportantChanged(
        bool value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.IsImportant);
    }

    partial void OnEditorQuadrantChanged(
        QuadrantType value)
    {
        ScheduleTaskEditorAutoSave(
            TaskEditFields.Quadrant);
    }

    #endregion
}
