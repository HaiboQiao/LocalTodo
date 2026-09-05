using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 通用任务列表页面的状态和命令。
///
/// “所有任务”和“已完成”页面共用此 ViewModel。
/// 右侧任务详情使用防抖自动保存：
/// 用户停止编辑约 600 毫秒后，修改自动写入数据库。
/// </summary>
public partial class TaskListViewModel :
    ObservableObject,
    IPendingChanges
{
    private static readonly TimeSpan
        AutoSaveDelay =
            TimeSpan.FromMilliseconds(600);

    private readonly TaskService
        _taskService;

    private readonly DialogService
        _dialogService;

    private readonly TaskTimeRefreshService?
        _timeRefreshService;

    /// <summary>
    /// 用户停止编辑后触发自动保存。
    /// </summary>
    private readonly DispatcherTimer
        _autoSaveTimer;

    /// <summary>
    /// 串行化自动保存、完成和删除操作，
    /// 避免多个写入同时访问同一任务。
    /// </summary>
    private readonly SemaphoreSlim
        _saveGate =
            new(1, 1);

    /// <summary>
    /// 当前已经开始、但尚未完成的详情自动保存。
    ///
    /// 退出或切换页面不能只检查 pending 标记，因为保存任务
    /// 取走 pending 后，数据库写入仍可能正在进行。
    /// </summary>
    private Task<bool>?
        _activeAutoSaveTask;

    /// <summary>
    /// 当前 ViewModel 生命周期内已经完成删除的任务 ID。
    ///
    /// 当前精简版 TaskItem 不包含 IsDeleted 属性，
    /// 因此使用 ID 集合阻止删除后的旧对象再次触发自动保存。
    /// </summary>
    private readonly HashSet<string>
        _deletedTaskIds =
            new(StringComparer.Ordinal);

    /// <summary>
    /// 正在同步 DatePicker 与任务日期时，
    /// 不把同步过程识别成用户修改。
    /// </summary>
    private bool
        _isSynchronizingDueDate;

    /// <summary>
    /// 正在从数据库重建集合时，
    /// 不触发自动保存。
    /// </summary>
    private bool
        _isReloadingTasks;

    /// <summary>
    /// 当前等待自动保存的任务。
    /// </summary>
    private TaskItem?
        _pendingAutoSaveTask;

    /// <summary>
    /// 等待保存的修改是否包含截止日期变化。
    /// 日期变化后需要重新生成日期分组。
    /// </summary>
    private bool
        _pendingAutoSaveRequiresRegroup;

    /// <summary>
    /// 当前选中任务最近一次确认过的数据库版本。
    /// </summary>
    private TaskEditBaseline?
        _selectedTaskEditBaseline;

    /// <summary>
    /// 当前选中任务尚未成功保存的字段组。
    /// </summary>
    private TaskEditFields
        _pendingAutoSaveFields;

    private bool
        _selectedTaskHasConflict;

    /// <summary>
    /// 用户在后台保存完成前切换任务时，
    /// 暂存原任务的冲突基线和未保存字段。
    /// </summary>
    private readonly Dictionary<string, DeferredTaskEditState>
        _deferredTaskEdits =
            new(StringComparer.Ordinal);

    private DateTime
        _lastGroupingDate;

    /// <summary>
    /// 从数据库读取的原始任务集合。
    /// </summary>
    public ObservableCollection<TaskItem>
        Tasks
    { get; } = [];

    /// <summary>
    /// 供任务列表界面使用的包装集合。
    /// 每个条目同时包含任务和日期分组信息。
    /// </summary>
    public ObservableCollection<TaskListEntry>
        TaskEntries
    { get; } = [];

    /// <summary>
    /// WPF 分组、排序后的任务视图。
    /// </summary>
    public ICollectionView
        TaskEntriesView
    { get; }

    public IReadOnlyList<TaskQuadrantOption>
        QuadrantOptions
    { get; } =
        TaskEditorOptionCatalog
            .Quadrants;

    public IReadOnlyList<TaskDueTimeOption>
    DueTimeOptions
    { get; } =
        TaskEditorOptionCatalog
            .DueTimes;

    public IReadOnlyList<TaskReminderOption>
    ReminderOptions
    { get; } =
        TaskEditorOptionCatalog
            .Reminders;

    public IReadOnlyList<TaskRepeatOption>
        RepeatOptions
    { get; } =
        TaskEditorOptionCatalog
            .Repeats;

    public bool CanSetDueTime =>
    NewTaskDueDate.HasValue;

    public bool CanSetReminder =>
        NewTaskDueDate.HasValue &&
        NewTaskDueTime.HasValue;

    public bool CanSetRepeat =>
        NewTaskDueDate.HasValue;

    public bool CanSetContinuous =>
        NewTaskDueDate.HasValue;

    /// <summary>
    /// 详情有截止日期以后，
    /// 才允许选择具体截止时间。
    /// </summary>
    public bool CanSetSelectedDueTime =>
        SelectedDueDate.HasValue;

    /// <summary>
    /// 详情同时有截止日期和具体时间以后，
    /// 才允许设置提醒。
    /// </summary>
    public bool CanSetSelectedReminder =>
        SelectedDueDate.HasValue &&
        SelectedDueTime.HasValue;

    /// <summary>
    /// 有截止日期以后才允许设置循环。
    /// </summary>
    public bool CanSetSelectedRepeat =>
        SelectedDueDate.HasValue;

    public bool CanSetSelectedContinuous =>
        SelectedDueDate.HasValue;

    /// <summary>
    /// 当前页面显示的任务状态。
    /// Pending 对应“所有任务”，
    /// Completed 对应“已完成”。
    /// </summary>
    public TodoStatus Status { get; }

    public bool CanAddTask =>
        Status ==
        TodoStatus.Pending;

    public string EmptyMessage =>
        Status ==
        TodoStatus.Completed
            ? "当前没有已完成任务"
            : "当前没有未完成任务";

    [ObservableProperty]
    private string newTaskTitle =
        string.Empty;

    /// <summary>
    /// 快速新增任务说明。
    /// </summary>
    [ObservableProperty]
    private string newTaskDescription =
        string.Empty;

    /// <summary>
    /// 快速新增任务是否标记为重点。
    /// </summary>
    [ObservableProperty]
    private bool newTaskIsImportant;

    [ObservableProperty]
    private bool newTaskIsContinuous;

    [ObservableProperty]
    private DateTime? newTaskDueDate;

    /// <summary>
    /// 新增任务的截止时间。
    ///
    /// null 表示没有设置具体时间。
    /// </summary>
    [ObservableProperty]
    private TimeSpan? newTaskDueTime;

    /// <summary>
    /// 新增任务当前选择的提醒方式。
    /// </summary>
    [ObservableProperty]
    private TaskReminderOption?
        newTaskReminderOption;

    /// <summary>
    /// 新增任务循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType newTaskRepeatType =
        TaskRepeatType.None;

    [ObservableProperty]
    private QuadrantType newTaskQuadrant =
        QuadrantType
            .NotImportantNotUrgent;

    /// <summary>
    /// 当前列表中选中的包装条目。
    /// </summary>
    [ObservableProperty]
    private TaskListEntry? selectedEntry;

    /// <summary>
    /// 当前右侧详情正在编辑的任务。
    /// </summary>
    [ObservableProperty]
    private TaskItem? selectedTask;

    /// <summary>
    /// DatePicker 使用的本地日期代理属性。
    /// </summary>
    [ObservableProperty]
    private DateTime? selectedDueDate;

    /// <summary>
    /// 当前详情任务的具体截止时间。
    ///
    /// null 表示当前任务只有截止日期，
    /// 没有设置具体时、分。
    /// </summary>
    [ObservableProperty]
    private TimeSpan? selectedDueTime;

    /// <summary>
    /// 当前详情任务选择的提醒方式。
    ///
    /// 直接复用新增任务的 ReminderOptions。
    /// </summary>
    [ObservableProperty]
    private TaskReminderOption?
        selectedReminderOption;

    /// <summary>
    /// 当前详情任务的循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType
        selectedRepeatType =
            TaskRepeatType.None;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage =
        "当前无修改";

    /// <summary>
    /// 任务列表中是否显示
    /// Ⅰ / Ⅱ / Ⅲ / Ⅳ 四象限简写。
    ///
    /// 这里只控制列表显示，
    /// 不修改任务本身的象限数据。
    /// </summary>
    [ObservableProperty]
    private bool
        showQuadrantAbbreviations =
            true;

    public bool HasTasks =>
        Tasks.Count > 0;

    /// <summary>
    /// 当前任务列表顶部显示的任务数量。
    ///
    /// 所有任务：
    /// 共 X 项待办
    ///
    /// 已完成：
    /// 共 X 项已完成
    /// </summary>
    public string TaskCountText =>
        Status ==
            TodoStatus.Completed
            ? $"共 {Tasks.Count} 项已完成"
            : $"共 {Tasks.Count} 项待办";

    public bool IsEmpty =>
        Tasks.Count == 0;

    public bool HasSelectedTask =>
        SelectedTask is not null;

    public TaskChangeSource ChangeSource =>
        Status == TodoStatus.Pending
            ? TaskChangeSource.AllTasks
            : TaskChangeSource.CompletedTasks;

    public TaskListViewModel(
        TaskService taskService,
        DialogService dialogService,
        TodoStatus status,
        TaskTimeRefreshService?
            timeRefreshService = null)
    {
        _taskService =
            taskService;

        _dialogService =
            dialogService;

        Status =
            status;

        TaskEntriesView =
            CollectionViewSource.GetDefaultView(
                TaskEntries);

        ConfigureTaskEntriesView();

        _autoSaveTimer =
            new DispatcherTimer
            {
                Interval =
                    AutoSaveDelay
            };

        _autoSaveTimer.Tick +=
            OnAutoSaveTimerTick;

        _timeRefreshService =
            timeRefreshService;

        _lastGroupingDate =
            GetToday();

        /*
         * LocalTodo 会长期驻留在系统托盘中。
         * 程序跨过午夜后，需要重新计算日期分组。
         */
        if (Status ==
            TodoStatus.Pending)
        {
            if (_timeRefreshService is not null)
            {
                _timeRefreshService.RefreshRequested +=
                    OnTimeRefreshRequested;
            }
        }
    }

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
            "正在读取任务";

        string? selectedTaskId =
            SelectedTask?.Id;

        try
        {
            /*
             * 页面重新加载前，先提交仍在等待中的编辑，
             * 避免切换页面或外部刷新时丢失修改。
             */
            if (!await FlushPendingAutoSaveAsync())
            {
                return;
            }

            await ReloadAndSelectAsync(
                selectedTaskId);

            /*
             * “所有任务”和“已完成”现在统一使用
             * 任务详情状态提示。
             *
             * 任务数量由左侧 TaskCountText 单独负责，
             * 这里不再重复显示任务总数。
             */
            StatusMessage =
                "当前无修改";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                $"读取任务失败：" +
                $"{Status}",
                exception);

            StatusMessage =
                $"读取任务失败：" +
                $"{exception.Message}";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    /// <summary>
    /// 打开新增任务 Popup 前恢复默认内容。
    /// </summary>
    public void BeginQuickAdd()
    {
        NewTaskTitle =
            string.Empty;

        NewTaskDescription =
            string.Empty;

        NewTaskIsImportant =
            false;

        NewTaskIsContinuous =
            false;

        NewTaskDueDate =
            null;

        NewTaskDueTime =
            null;

        NewTaskReminderOption =
            ReminderOptions[0];

        NewTaskRepeatType =
            TaskRepeatType.None;

        NewTaskQuadrant =
            QuadrantType
                .NotImportantNotUrgent;
    }

    partial void OnNewTaskDueDateChanged(
    DateTime? value)
    {
        OnPropertyChanged(
            nameof(CanSetDueTime));

        OnPropertyChanged(
            nameof(CanSetReminder));

        OnPropertyChanged(
            nameof(CanSetRepeat));

        OnPropertyChanged(
            nameof(CanSetContinuous));

        if (value.HasValue)
        {
            return;
        }

        NewTaskDueTime =
            null;

        NewTaskReminderOption =
            ReminderOptions[0];

        NewTaskRepeatType =
            TaskRepeatType.None;

        NewTaskIsContinuous =
            false;
    }

    partial void OnNewTaskDueTimeChanged(
        TimeSpan? value)
    {
        OnPropertyChanged(
            nameof(CanSetReminder));

        if (!value.HasValue)
        {
            NewTaskReminderOption =
                ReminderOptions[0];
        }
    }

    /// <summary>
    /// 保留 AddTaskCommand，避免以后其他地方仍引用它。
    /// </summary>
    [RelayCommand]
    private async Task AddTaskAsync()
    {
        await AddQuickTaskAsync();
    }

    /// <summary>
    /// 从新的 Popup 创建完整任务。
    ///
    /// 返回 true 表示新增成功，
    /// View 可以关闭 Popup。
    /// </summary>
    public async Task<bool>
        AddQuickTaskAsync()
    {
        if (IsBusy ||
            !CanAddTask)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                NewTaskTitle))
        {
            StatusMessage =
                "请输入任务标题";

            return false;
        }

        IsBusy =
            true;

        try
        {
            /*
             * 如果右侧详情还有等待保存的修改，
             * 新增前先提交，避免两个写入互相影响。
             */
            if (!await FlushPendingAutoSaveAsync())
            {
                return false;
            }

            TaskReminderOption reminderOption =
                NewTaskReminderOption ??
                ReminderOptions[0];

            TaskEditDraft draft =
                new(
                    NewTaskTitle,
                    NewTaskDescription,
                    NewTaskDueDate,
                    NewTaskDueTime,
                    reminderOption.Enabled,
                    reminderOption.MinutesBefore,
                    NewTaskRepeatType,
                    NewTaskIsContinuous,
                    NewTaskIsImportant,
                    NewTaskQuadrant);

            TaskItem createdTask =
                await _taskService
                    .CreateTaskAsync(
                        draft,
                        changeSource:
                            ChangeSource);

            /*
             * 成功后清空快速新增字段。
             */
            NewTaskTitle =
                string.Empty;

            NewTaskDescription =
                string.Empty;

            NewTaskIsImportant =
                false;

            NewTaskIsContinuous =
                false;

            NewTaskDueDate =
                null;

            NewTaskQuadrant =
                QuadrantType
                    .NotImportantNotUrgent;

            NewTaskDueTime =
                null;

            NewTaskReminderOption =
                ReminderOptions[0];

            NewTaskRepeatType =
                TaskRepeatType.None;

            await ReloadAndSelectAsync(
                createdTask.Id);

            StatusMessage =
                $"已新增任务：" +
                $"{createdTask.Title}";

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "新增任务失败。",
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

    [RelayCommand]
    private async Task ToggleTaskAsync(
    TaskItem? task)
    {
        if (IsBusy ||
            task is null)
        {
            return;
        }

        IsBusy =
            true;

        try
        {
            /*
             * 在真正修改任务之前记录：
             *
             * 当前操作是否是在“已完成”页面
             * 恢复一条循环任务。
             *
             * TaskService 恢复成功以后会把
             * RepeatType 修改成 None，
             * 所以必须提前记录。
             */
            bool isRestoringRecurringTask =
                task.Status ==
                    TodoStatus.Completed &&
                task.RepeatType !=
                    TaskRepeatType.None;

            TodoStatus targetStatus =
                task.Status == TodoStatus.Completed
                    ? TodoStatus.Pending
                    : TodoStatus.Completed;

            /*
             * 如果当前任务还有尚未触发的
             * 详情自动保存，
             * 完成/恢复操作优先，
             * 因此取消这次等待保存。
             */
            CancelPendingAutoSave(
                task);

            /*
             * 只获取一次写入锁。
             *
             * 这个锁用于避免：
             *
             * 自动保存、
             * 完成任务、
             * 删除任务
             *
             * 同时写入同一个任务。
             */
            await _saveGate
                .WaitAsync();

            TaskItem? nextRecurringTask;

            try
            {
                /*
                 * 普通任务完成：
                 * 返回 null。
                 *
                 * 循环任务完成：
                 * 返回新创建的下一期任务。
                 *
                 * 已完成任务恢复：
                 * 返回 null。
                 */
                nextRecurringTask =
                    await _taskService
                        .SetCompletionStateAsync(
                            task.Id,
                            targetStatus,
                            changeSource:
                                ChangeSource);
            }
            finally
            {
                /*
                 * 无论数据库操作成功还是失败，
                 * 都必须释放写入锁。
                 */
                _saveGate.Release();
            }

            bool isNowCompleted =
                targetStatus == TodoStatus.Completed;

            /*
             * ============================
             * 所有任务：未完成 → 已完成
             * ============================
             *
             * 当前任务已经不再属于
             * “所有任务”页面。
             *
             * 只从当前内存集合增量移除，
             * 避免整个列表重新加载后滚动位置跳动。
             */
            if (Status ==
                    TodoStatus.Pending &&
                isNowCompleted)
            {
                RemoveTaskFromCurrentView(
                    task);

                /*
                 * 如果这是循环任务，
                 * TaskService 已经创建下一期。
                 *
                 * 直接把下一期加入当前列表。
                 */
                if (nextRecurringTask
                    is not null)
                {
                    AddTaskToCurrentView(
                        nextRecurringTask);
                }
            }
            else
            {
                /*
                 * 主要用于：
                 *
                 * “已完成”页面恢复任务。
                 *
                 * 恢复后任务已经不再属于
                 * Completed 列表，
                 * 因此重新读取当前页面。
                 */
                await ReloadAndSelectAsync(
                    task.Id);
            }

            /*
             * 根据最终操作显示状态信息。
             */
            StatusMessage =
                isNowCompleted
                    ? nextRecurringTask is not null
                        ? "任务已完成，已生成下一期循环任务"
                        : "任务已完成"
                    : isRestoringRecurringTask
                        ? "任务已恢复为未完成，并已取消循环"
                        : "任务已恢复为未完成";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "修改任务完成状态失败。",
                exception);

            StatusMessage =
                exception.Message;
        }
        finally
        {
            /*
             * 无论成功、失败还是发生异常，
             * 最终都恢复页面可操作状态。
             */
            IsBusy =
                false;
        }
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(
        TaskItem? task)
    {
        if (IsBusy ||
            task is null)
        {
            return;
        }

        /*
         * 必须在调用 Dialog / Service 之前记录。
         *
         * 因为 Completed 循环历史期选择
         * “删除当前周期”后，
         * TaskService 会把当前历史期改成：
         *
         * RepeatType = None
         * RecurrenceSeriesId = null
         *
         * 如果删除后再判断 RepeatType，
         * 就无法知道它原本是不是循环历史期。
         */
        bool deletingCompletedRecurringHistory =
            task.Status ==
                TodoStatus.Completed &&
            task.RepeatType !=
                TaskRepeatType.None;

        TaskDeleteChoice deleteChoice =
            _dialogService
                .GetTaskDeleteChoice(
                    task);

        if (deleteChoice ==
            TaskDeleteChoice.Cancel)
        {
            return;
        }

        bool deletionCompleted =
            false;

        IsBusy =
            true;

        try
        {
            /*
             * 删除前取消该任务尚未触发的自动保存。
             */
            _deletedTaskIds.Add(
                task.Id);

            CancelPendingAutoSave(
                task);

            await _saveGate
                .WaitAsync();

            try
            {
                await _taskService
                    .DeleteTaskWithChoiceByIdAsync(
                        task.Id,
                        deleteChoice,
                        changeSource:
                            ChangeSource);

                deletionCompleted =
                    true;
            }
            finally
            {
                _saveGate.Release();
            }

            /*
             * 删除后重新读取当前页面。
             *
             * Pending 循环任务选择“删除当前周期”时，
             * TaskService 会生成下一期，
             * 重新加载后自然能看到它。
             *
             * Completed 循环历史期选择“删除当前周期”时，
             * Service 不会生成任何下一期，
             * 也不会影响已经存在的 Pending 下一期。
             */
            await ReloadAndSelectAsync(
                selectedTaskId: null);

            StatusMessage =
                deleteChoice switch
                {
                    TaskDeleteChoice
                        .DeleteCurrentOccurrence =>
                            deletingCompletedRecurringHistory
                                ? $"已删除已完成的当前周期，" +
                                  $"后续循环不受影响：" +
                                  $"{task.Title}"
                                : $"已删除当前周期，" +
                                  $"并继续下一周期：" +
                                  $"{task.Title}",

                    TaskDeleteChoice
                        .DeleteEntireSeries =>
                            $"已删除整个循环任务：" +
                            $"{task.Title}",

                    _ =>
                        $"已删除任务：" +
                        $"{task.Title}"
                };
        }
        catch (Exception exception)
        {
            if (!deletionCompleted)
            {
                _deletedTaskIds.Remove(
                    task.Id);
            }

            AppLog.Error(
                "删除任务失败。",
                exception);

            StatusMessage =
                exception.Message;
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    /// <summary>
    /// 根据任务当前保存的提醒设置，
    /// 找到详情 ComboBox 中应该选中的选项。
    /// </summary>
    private TaskReminderOption
        FindReminderOption(
            TaskItem? task)
    {
        /*
         * 没任务或者任务没有开启提醒，
         * 都显示“不提醒”。
         */
        if (task is null ||
            !task.ReminderEnabled)
        {
            return ReminderOptions[0];
        }

        /*
         * 根据：
         *
         * ReminderEnabled
         * ReminderMinutesBefore
         *
         * 找到对应选项。
         */
        TaskReminderOption? option =
            ReminderOptions
                .FirstOrDefault(
                    item =>
                        item.Enabled &&
                        item.MinutesBefore ==
                            task.ReminderMinutesBefore);

        /*
         * 正常情况下都会找到。
         *
         * 如果数据库里出现旧的未知值，
         * 安全回退到“不提醒”。
         */
        return option ??
            ReminderOptions[0];
    }

    /// <summary>
    /// 原有的日期转换方法。
    /// 右侧任务详情目前仍会使用它，
    /// 所以这里暂时不要删除。
    /// </summary>
    private static DateTimeOffset
        CreateLocalDueDate(
            DateTime selectedDate)
    {
        return TaskRules.CreateLocalDueAt(
                   selectedDate,
                   dueTime: null) ??
               throw new InvalidOperationException(
                   "无法创建任务日期。");
    }

    private DateTime GetToday()
    {
        return _timeRefreshService?.Today ??
               LocalTimeService.System
                   .ToLocalDateTime(
                       SystemClock.Instance.UtcNow)
                   .Date;
    }
}
