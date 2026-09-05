using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 四象限页面的状态和业务操作。
///
/// 负责：
/// 1. 通过共享仓储读取四个象限的任务集合；
/// 2. 通过拖放移动任务；
/// 3. 在指定象限内快速新增任务；
/// 4. 管理任务详情弹窗的编辑缓冲区和未保存状态；
/// 5. 完成任务后刷新四象限。
///
/// 每个窗口使用一个独立实例，因此编辑和 Popup 状态不会串窗。
/// </summary>
public abstract partial class MatrixViewModel :
    ObservableObject,
    IPendingChanges
{
    private readonly MatrixTaskStore
        _taskStore;

    private readonly QuadrantService
        _quadrantService;

    private readonly TaskService
        _taskService;

    private readonly DialogService
    _dialogService;

    private readonly TaskChangeSource
        _changeSource;

    private readonly TaskEditorAutoSaveCoordinator
        _taskEditorAutoSave;

    /// <summary>
    /// 防止多次自动保存或删除操作同时写数据库。
    /// </summary>
    private readonly SemaphoreSlim
        _taskEditorSaveGate =
            new(1, 1);

    /// <summary>
    /// IsBusy 结束时唤醒等待执行的完成操作，避免直接丢弃点击。
    /// </summary>
    private TaskCompletionSource<bool>?
        _idleCompletionSource;

    /// <summary>
    /// 当前是否存在等待自动保存的修改。
    /// </summary>
    private bool _hasPendingTaskEditorSave
    {
        get =>
            _taskEditorAutoSave.HasPendingSave;

        set =>
            _taskEditorAutoSave.HasPendingSave =
                value;
    }

    private TaskEditBaseline?
        _taskEditorBaseline;

    private TaskEditFields _taskEditorDirtyFields
    {
        get =>
            _taskEditorAutoSave.DirtyFields;

        set =>
            _taskEditorAutoSave.DirtyFields =
                value;
    }

    private bool _taskEditorHasConflict
    {
        get =>
            _taskEditorAutoSave.HasConflict;

        set =>
            _taskEditorAutoSave.HasConflict =
                value;
    }

    /// <summary>
    /// 加载任务详情编辑缓冲区时使用，
    /// 避免初始化字段触发“未保存”星号。
    /// </summary>
    private bool
        _isLoadingTaskEditor;

    /// <summary>
    /// 第一象限：重要且紧急。
    /// </summary>
    public ObservableCollection<TaskItem>
        ImportantAndUrgentTasks
        => _taskStore
            .ImportantAndUrgentTasks;

    /// <summary>
    /// 第二象限：重要但不紧急。
    /// </summary>
    public ObservableCollection<TaskItem>
        ImportantNotUrgentTasks
        => _taskStore
            .ImportantNotUrgentTasks;

    /// <summary>
    /// 第三象限：紧急但不重要。
    /// </summary>
    public ObservableCollection<TaskItem>
        UrgentNotImportantTasks
        => _taskStore
            .UrgentNotImportantTasks;

    /// <summary>
    /// 第四象限：不重要且不紧急。
    /// </summary>
    public ObservableCollection<TaskItem>
        NotImportantNotUrgentTasks
        => _taskStore
            .NotImportantNotUrgentTasks;

    /// <summary>
    /// 第一象限的日期分组显示视图。
    /// </summary>
    public MatrixDateGroupedTasks
        ImportantAndUrgentGroups
        => _taskStore
            .ImportantAndUrgentGroups;

    /// <summary>
    /// 第二象限的日期分组显示视图。
    /// </summary>
    public MatrixDateGroupedTasks
        ImportantNotUrgentGroups
        => _taskStore
            .ImportantNotUrgentGroups;

    /// <summary>
    /// 第三象限的日期分组显示视图。
    /// </summary>
    public MatrixDateGroupedTasks
        UrgentNotImportantGroups
        => _taskStore
            .UrgentNotImportantGroups;

    /// <summary>
    /// 第四象限的日期分组显示视图。
    /// </summary>
    public MatrixDateGroupedTasks
        NotImportantNotUrgentGroups
        => _taskStore
            .NotImportantNotUrgentGroups;

    /// <summary>
    /// 任务详情和快速新增弹窗共用的象限选项。
    /// </summary>
    public IReadOnlyList<TaskQuadrantOption>
        MatrixQuadrantOptions
    { get; } =
        TaskEditorOptionCatalog
            .Quadrants;

    /// <summary>
    /// 任务详情和快速新增共同使用的截止时间选项。
    /// 第一项“无”表示只有截止日期，没有具体时分。
    /// </summary>
    public IReadOnlyList<TaskDueTimeOption>
        DueTimeOptions
    { get; } =
        TaskEditorOptionCatalog
            .DueTimes;

    /// <summary>
    /// 任务详情和快速新增共同使用的提醒选项。
    /// 与“所有任务”和“日历”的新增/详情保持一致。
    /// </summary>
    public IReadOnlyList<TaskReminderOption>
        ReminderOptions
    { get; } =
        TaskEditorOptionCatalog
            .Reminders;

    /// <summary>
    /// 任务详情和快速新增共同使用的循环选项。
    /// </summary>
    public IReadOnlyList<TaskRepeatOption>
        RepeatOptions
    { get; } =
        TaskEditorOptionCatalog
            .Repeats;

    [ObservableProperty]
    private bool isBusy;

    partial void OnIsBusyChanged(bool value)
    {
        if (!value)
        {
            Interlocked.Exchange(
                    ref _idleCompletionSource,
                    null)
                ?.TrySetResult(true);
        }
    }

    [ObservableProperty]
    private string statusMessage =
        "正在准备四象限";

    /*
     * 象限内快速新增任务。
     */

    [ObservableProperty]
    private string newMatrixTaskTitle =
        string.Empty;

    [ObservableProperty]
    private string newMatrixTaskDescription =
    string.Empty;

    [ObservableProperty]
    private DateTime?
        newMatrixTaskDueDate;

    /// <summary>
    /// 四象限快速新增任务的具体截止时间。
    /// null 表示只有截止日期，没有具体时分。
    /// </summary>
    [ObservableProperty]
    private TimeSpan?
        newMatrixTaskDueTime;

    /// <summary>
    /// 四象限快速新增任务选择的提醒方式。
    /// </summary>
    [ObservableProperty]
    private TaskReminderOption?
        newMatrixTaskReminderOption;

    /// <summary>
    /// 四象限快速新增任务选择的循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType
        newMatrixTaskRepeatType =
            TaskRepeatType.None;

    [ObservableProperty]
    private bool
        newMatrixTaskIsImportant;

    [ObservableProperty]
    private bool
        newMatrixTaskIsContinuous;

    [ObservableProperty]
    private QuadrantType
        newMatrixTaskQuadrant =
            QuadrantType.NotImportantNotUrgent;

    /// <summary>
    /// 快速新增弹窗标题中显示的目标象限名称。
    /// </summary>
    public string NewMatrixTaskQuadrantText =>
        GetQuadrantTitle(
            NewMatrixTaskQuadrant);

    /// <summary>
    /// 有截止日期以后才允许设置具体截止时间。
    /// </summary>
    public bool CanSetNewMatrixTaskDueTime =>
        NewMatrixTaskDueDate.HasValue;

    /// <summary>
    /// 同时有截止日期和具体时间以后才允许设置提醒。
    /// </summary>
    public bool CanSetNewMatrixTaskReminder =>
        NewMatrixTaskDueDate.HasValue &&
        NewMatrixTaskDueTime.HasValue;

    /// <summary>
    /// 有截止日期以后才允许设置循环。
    /// </summary>
    public bool CanSetNewMatrixTaskRepeat =>
        NewMatrixTaskDueDate.HasValue;

    public bool CanSetNewMatrixTaskContinuous =>
        NewMatrixTaskDueDate.HasValue;

    /*
     * 任务详情弹窗编辑缓冲区。
     *
     * 这些属性不会直接修改原始 TaskItem，
     * 只有点击“保存修改”后才写回数据库。
     */

    [ObservableProperty]
    private TaskItem?
        editingTask;

    [ObservableProperty]
    private string editorTitle =
        string.Empty;

    [ObservableProperty]
    private string editorDescription =
        string.Empty;

    [ObservableProperty]
    private DateTime?
        editorDueDate;

    /// <summary>
    /// 当前详情任务的具体截止时间。
    /// null 表示只有截止日期，没有具体时分。
    /// </summary>
    [ObservableProperty]
    private TimeSpan?
        editorDueTime;

    /// <summary>
    /// 当前详情任务选择的提醒方式。
    /// </summary>
    [ObservableProperty]
    private TaskReminderOption?
        editorReminderOption;

    /// <summary>
    /// 当前详情任务选择的循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType
        editorRepeatType =
            TaskRepeatType.None;

    [ObservableProperty]
    private bool
        editorIsImportant;

    [ObservableProperty]
    private bool
        editorIsContinuous;

    [ObservableProperty]
    private QuadrantType
        editorQuadrant =
            QuadrantType.NotImportantNotUrgent;

    /// <summary>
    /// 当前是否已经打开任务详情。
    /// </summary>
    public bool HasEditingTask =>
        EditingTask is not null;

    /// <summary>
    /// 有截止日期以后才允许设置具体截止时间。
    /// </summary>
    public bool CanSetEditorDueTime =>
        EditorDueDate.HasValue;

    /// <summary>
    /// 同时有截止日期和具体时间以后才允许设置提醒。
    /// </summary>
    public bool CanSetEditorReminder =>
        EditorDueDate.HasValue &&
        EditorDueTime.HasValue;

    /// <summary>
    /// 有截止日期以后才允许设置循环。
    /// </summary>
    public bool CanSetEditorRepeat =>
        EditorDueDate.HasValue;

    public bool CanSetEditorContinuous =>
        EditorDueDate.HasValue;

    /// <summary>
    /// 第一象限当前是否有任务。
    /// </summary>
    public bool HasImportantAndUrgentTasks =>
        ImportantAndUrgentTasks.Count > 0;

    /// <summary>
    /// 第二象限当前是否有任务。
    /// </summary>
    public bool HasImportantNotUrgentTasks =>
        ImportantNotUrgentTasks.Count > 0;

    /// <summary>
    /// 第三象限当前是否有任务。
    /// </summary>
    public bool HasUrgentNotImportantTasks =>
        UrgentNotImportantTasks.Count > 0;

    /// <summary>
    /// 第四象限当前是否有任务。
    /// </summary>
    public bool HasNotImportantNotUrgentTasks =>
        NotImportantNotUrgentTasks.Count > 0;

    protected MatrixViewModel(
        MatrixTaskStore taskStore,
        QuadrantService quadrantService,
        TaskService taskService,
        DialogService dialogService,
        TaskChangeSource changeSource)
    {
        ArgumentNullException.ThrowIfNull(
            taskStore);

        ArgumentNullException.ThrowIfNull(
            quadrantService);

        ArgumentNullException.ThrowIfNull(
            taskService);

        ArgumentNullException.ThrowIfNull(
            dialogService);

        _taskStore =
            taskStore;

        _quadrantService =
            quadrantService;

        _taskService =
            taskService;

        _dialogService =
            dialogService;

        _changeSource =
            changeSource;

        _taskEditorAutoSave =
            new TaskEditorAutoSaveCoordinator(
                TimeSpan.FromMilliseconds(
                    600),
                () => EditingTask is not null,
                AutoSaveTaskEditorAsync,
                OnTaskEditorAutoSaveFailed);

        _taskStore.Changed +=
            OnTaskStoreChanged;
    }

    #region 加载和刷新

    /// <summary>
    /// 从数据库重新读取并显示四象限任务。
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy =
            true;

        StatusMessage =
            "正在读取四象限任务";

        try
        {
            await ReloadSnapshotAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "加载四象限失败。",
                exception);

            StatusMessage =
                $"加载四象限失败：" +
                $"{exception.Message}";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    /// <summary>
    /// 刷新共享任务快照。
    /// 当前会话只维护自己的编辑和提示状态。
    /// </summary>
    private async Task ReloadSnapshotAsync()
    {
        await _taskStore
            .RefreshAsync();

        UpdateTaskCountStatus();
    }

    private void OnTaskStoreChanged(
        object? sender,
        MatrixTaskStoreChangedEventArgs e)
    {
        OnPropertyChanged(
            nameof(HasImportantAndUrgentTasks));

        OnPropertyChanged(
            nameof(HasImportantNotUrgentTasks));

        OnPropertyChanged(
            nameof(HasUrgentNotImportantTasks));

        OnPropertyChanged(
            nameof(HasNotImportantNotUrgentTasks));

        if (e.ChangeKind ==
            MatrixTaskStoreChangeKind.DateGroupsRebuilt)
        {
            StatusMessage =
                "日期已变化，四象限分组已更新";

            return;
        }

        /*
         * 共享快照更新时只检查当前会话的编辑目标。
         * 另一个窗口的编辑缓冲和 Popup 生命周期不会被修改。
         */
        if (EditingTask is not null &&
            FindTaskById(
                EditingTask.Id) is null &&
            _taskEditorDirtyFields ==
                TaskEditFields.None &&
            !_hasPendingTaskEditorSave &&
            !_taskEditorHasConflict)
        {
            CloseTaskEditor();
        }

        if (!IsBusy &&
            !_hasPendingTaskEditorSave &&
            !_taskEditorHasConflict)
        {
            UpdateTaskCountStatus();
        }
    }

    private void UpdateTaskCountStatus()
    {
        int totalCount =
            _taskStore.TotalTaskCount;

        StatusMessage =
            totalCount == 0
                ? "当前没有未完成任务"
                : $"四象限中共有 {totalCount} 条任务";
    }

    #endregion

    #region 快速新增任务

    /// <summary>
    /// 打开快速新增弹窗前，准备目标象限和默认值。
    /// </summary>
    public void BeginQuickAdd(
        QuadrantType quadrant)
    {
        ValidateQuadrant(
            quadrant);

        NewMatrixTaskQuadrant =
            quadrant;

        ResetQuickAddFields();

        StatusMessage =
            $"准备添加到" +
            $"{GetQuadrantTitle(quadrant)}";
    }

    /// <summary>
    /// 将快速新增弹窗中的任务保存到指定象限。
    /// </summary>
    public async Task<bool>
        AddTaskToCurrentQuadrantAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                NewMatrixTaskTitle))
        {
            StatusMessage =
                "任务标题不能为空";

            return false;
        }

        IsBusy =
            true;

        QuadrantType targetQuadrant =
            NewMatrixTaskQuadrant;

        try
        {
            TaskReminderOption
                reminderOption =
                    NewMatrixTaskReminderOption ??
                    ReminderOptions[0];

            TaskEditDraft draft =
                new(
                    NewMatrixTaskTitle,
                    NewMatrixTaskDescription,
                    NewMatrixTaskDueDate,
                    NewMatrixTaskDueTime,
                    reminderOption.Enabled,
                    reminderOption.MinutesBefore,
                    NewMatrixTaskRepeatType,
                    NewMatrixTaskIsContinuous,
                    NewMatrixTaskIsImportant,
                    targetQuadrant);

            /*
             * 直接使用完整创建重载，一次写入：
             * 标题、说明、象限、截止日期、截止时间、
             * 重点、提醒和循环。
             */
            TaskItem createdTask =
                await _taskService
                    .CreateTaskAsync(
                        draft,
                        changeSource:
                            _changeSource);

            await ReloadSnapshotAsync();

            StatusMessage =
                $"已在" +
                $"{GetQuadrantTitle(targetQuadrant)}" +
                $"新增任务：{createdTask.Title}";

            ResetQuickAddFields();

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "在四象限中新增任务失败。",
                exception);

            StatusMessage =
                exception.Message;

            return false;
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    private void ResetQuickAddFields()
    {
        NewMatrixTaskTitle =
            string.Empty;

        NewMatrixTaskDescription =
            string.Empty;

        NewMatrixTaskDueDate =
            null;

        NewMatrixTaskDueTime =
            null;

        NewMatrixTaskReminderOption =
            ReminderOptions[0];

        NewMatrixTaskRepeatType =
            TaskRepeatType.None;

        NewMatrixTaskIsImportant =
            false;

        NewMatrixTaskIsContinuous =
            false;

        /*
         * NewMatrixTaskQuadrant 不在这里重置。
         *
         * BeginQuickAdd(quadrant) 会先把用户点击的目标象限
         * 写入 NewMatrixTaskQuadrant，再清空其他输入字段。
         * 保留象限值可以确保点击哪个象限的“+ 添加”，
         * Popup 默认就显示哪个象限。
         */
        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskDueTime));

        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskReminder));

        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskRepeat));

        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskContinuous));
    }

    /// <summary>
    /// 截止日期变化时同步快速新增控件的可用状态。
    ///
    /// 清空日期以后：
    /// 时间 -> 无；
    /// 提醒 -> 不提醒；
    /// 循环 -> 不循环。
    /// </summary>
    partial void OnNewMatrixTaskDueDateChanged(
        DateTime? value)
    {
        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskDueTime));

        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskReminder));

        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskRepeat));

        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskContinuous));

        if (!value.HasValue)
        {
            NewMatrixTaskDueTime =
                null;

            NewMatrixTaskReminderOption =
                ReminderOptions[0];

            NewMatrixTaskRepeatType =
                TaskRepeatType.None;

            NewMatrixTaskIsContinuous =
                false;
        }
    }

    /// <summary>
    /// 截止时间变化时同步提醒控件。
    ///
    /// 时间改回“无”以后，
    /// 提醒自动恢复为“不提醒”。
    /// </summary>
    partial void OnNewMatrixTaskDueTimeChanged(
        TimeSpan? value)
    {
        OnPropertyChanged(
            nameof(CanSetNewMatrixTaskReminder));

        if (!value.HasValue)
        {
            NewMatrixTaskReminderOption =
                ReminderOptions[0];
        }
    }

    partial void OnNewMatrixTaskQuadrantChanged(
        QuadrantType value)
    {
        OnPropertyChanged(
            nameof(NewMatrixTaskQuadrantText));
    }

    #endregion


    #region 任务操作

    /// <summary>
    /// 通过拖放把任务移动到目标象限。
    /// </summary>
    public async Task MoveTaskToQuadrantAsync(
        TaskItem task,
        QuadrantType targetQuadrant)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        ValidateQuadrant(
            targetQuadrant);

        if (IsBusy)
        {
            return;
        }

        if (_taskStore.IsTaskInQuadrant(
                task,
                targetQuadrant))
        {
            StatusMessage =
                $"“{task.Title}”已经位于" +
                $"{GetQuadrantTitle(targetQuadrant)}";

            return;
        }

        IsBusy =
            true;

        try
        {
            await _quadrantService
                .SetManualQuadrantAsync(
                    task,
                    targetQuadrant,
                    changeSource:
                        _changeSource);

            await ReloadSnapshotAsync();

            StatusMessage =
                $"已将“{task.Title}”移动到" +
                $"{GetQuadrantTitle(targetQuadrant)}";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "拖动任务到其他象限失败。",
                exception);

            StatusMessage =
                $"移动任务失败：" +
                $"{exception.Message}";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    /// <summary>
    /// 完成或恢复任务。
    /// 四象限中通常只显示未完成任务，
    /// 因此完成后任务会从当前页面移除。
    /// </summary>
    [RelayCommand]
    private async Task ToggleTaskAsync(
        TaskItem? task)
    {
        if (task is null)
        {
            return;
        }

        string taskId = task.Id;
        TodoStatus targetStatus =
            task.Status == TodoStatus.Completed
                ? TodoStatus.Pending
                : TodoStatus.Completed;

        if (IsBusy)
        {
            StatusMessage =
                "正在完成上一项操作，本次点击会继续处理";
        }

        await WaitUntilIdleAndBeginOperationAsync();

        await _taskEditorSaveGate
            .WaitAsync();

        try
        {
            await _taskService
                .SetCompletionStateAsync(
                    taskId,
                    targetStatus,
                    changeSource:
                        _changeSource);

            if (EditingTask?.Id ==
                taskId)
            {
                CloseTaskEditor();
            }

            await ReloadSnapshotAsync();

            StatusMessage =
                "任务已完成，并已从四象限移除";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "在四象限中完成任务失败。",
                exception);

            StatusMessage =
                exception.Message;
        }
        finally
        {
            _taskEditorSaveGate
                .Release();

            IsBusy =
                false;
        }
    }

    private async Task WaitUntilIdleAndBeginOperationAsync()
    {
        while (true)
        {
            if (!IsBusy)
            {
                /*
                 * 检查和占用都在同一次 UI 延续中完成，
                 * 不给其他界面操作留下再次抢占的间隙。
                 */
                IsBusy = true;
                return;
            }

            TaskCompletionSource<bool> signal =
                new(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);
            TaskCompletionSource<bool> activeSignal =
                Interlocked.CompareExchange(
                    ref _idleCompletionSource,
                    signal,
                    null) ?? signal;

            if (!IsBusy)
            {
                Interlocked.CompareExchange(
                    ref _idleCompletionSource,
                    null,
                    activeSignal);
                continue;
            }

            await activeSignal.Task;
        }
    }

    #endregion

    #region 辅助方法和内部类型

    private TaskItem? FindTaskById(
        string taskId)
    {
        return _taskStore
            .FindTaskById(
                taskId);
    }

    private QuadrantType GetTaskQuadrant(
        TaskItem task)
    {
        return _taskStore
            .GetTaskQuadrant(
                task);
    }

    private static string GetQuadrantTitle(
        QuadrantType quadrant)
    {
        return quadrant switch
        {
            QuadrantType.ImportantAndUrgent =>
                "第一象限",

            QuadrantType.ImportantNotUrgent =>
                "第二象限",

            QuadrantType.UrgentNotImportant =>
                "第三象限",

            QuadrantType.NotImportantNotUrgent =>
                "第四象限",

            _ =>
                "未知象限"
        };
    }

    private static void ValidateQuadrant(
        QuadrantType quadrant)
    {
        if (!Enum.IsDefined(
                typeof(QuadrantType),
                quadrant))
        {
            throw new ArgumentOutOfRangeException(
                nameof(quadrant),
                quadrant,
                "无法识别任务象限。");
        }
    }

    /// <summary>
    /// 根据任务当前保存的提醒字段，
    /// 找到详情下拉框应该选中的提醒选项。
    /// </summary>
    private TaskReminderOption
        FindReminderOption(
            TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        if (!task.ReminderEnabled)
        {
            return ReminderOptions[0];
        }

        TaskReminderOption? option =
            ReminderOptions
                .FirstOrDefault(
                    item =>
                        item.Enabled &&
                        item.MinutesBefore ==
                            task.ReminderMinutesBefore);

        return option ??
            ReminderOptions[0];
    }

    #endregion
}
