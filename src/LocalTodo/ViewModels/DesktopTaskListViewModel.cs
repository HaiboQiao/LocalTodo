using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 桌面任务列表的 ViewModel。
///
/// 负责：
/// 1. 读取全部未完成任务；
/// 2. 使用与“所有任务”页面相同的日期分组；
/// 3. 快速新增任务；
/// 4. 编辑任务并 600ms 防抖自动保存；
/// 5. 完成任务；
/// 6. 删除任务到垃圾箱；
/// 7. 响应其他页面的任务变化；
/// 8. 跨午夜重新计算日期分组。
/// </summary>
public partial class DesktopTaskListViewModel :
    ObservableObject,
    IPendingChanges
{
    private static readonly TimeSpan
        AutoSaveDelay =
            TimeSpan.FromMilliseconds(
                600);

    private readonly TaskService
        _taskService;

    private readonly DialogService
        _dialogService;

    private readonly TaskEditorAutoSaveCoordinator
        _taskEditorAutoSave;

    /// <summary>
    /// 串行化详情保存和删除操作。
    /// </summary>
    private readonly SemaphoreSlim
        _taskEditorSaveGate =
            new(1, 1);

    /// <summary>
    /// 每分钟检查一次是否跨过午夜。
    /// </summary>
    private readonly TaskTimeRefreshService?
        _timeRefreshService;

    private DateTime
        _lastGroupingDate;

    /// <summary>
    /// 当前详情是否还有内容等待自动保存。
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
    /// 正在把任务内容加载到 Editor... 缓冲属性时，
    /// 不触发自动保存。
    /// </summary>
    private bool
        _isLoadingTaskEditor;

    /// <summary>
    /// 当前任务变化是否由桌面任务列表自己发起。
    ///
    /// TaskService 会触发 TasksChanged，
    /// 本页面自己的操作不需要再因此重复加载。
    /// </summary>
    private bool
        _isPerformingLocalChange;

    /// <summary>
    /// 原始未完成任务集合。
    /// </summary>
    public ObservableCollection<TaskItem>
        Tasks
    { get; } = [];

    /// <summary>
    /// 日期分组后的包装条目。
    ///
    /// 复用现有 TaskListEntry，
    /// 从而和“所有任务”页面保持相同分组规则。
    /// </summary>
    public ObservableCollection<TaskListEntry>
        TaskEntries
    { get; } = [];

    public ICollectionView
        TaskEntriesView
    { get; }

    /// <summary>
    /// 新增和详情编辑共用的象限选项。
    /// </summary>
    public IReadOnlyList<
        TaskQuadrantOption>
        QuadrantOptions
    { get; } =
        TaskEditorOptionCatalog
            .Quadrants;

    [ObservableProperty]
    private bool
        isBusy;

    [ObservableProperty]
    private string
        statusMessage =
            "正在准备桌面任务列表";

    /// <summary>
    /// 桌面任务列表中是否显示
    /// Ⅰ / Ⅱ / Ⅲ / Ⅳ 四象限简写。
    ///
    /// 这里只控制任务列表中的显示效果，
    /// 不修改任务本身的象限数据。
    ///
    /// 该属性由 MainWindowViewModel 中的
    /// “隐藏四象限图标”总设置统一控制。
    /// </summary>
    [ObservableProperty]
    private bool
        showQuadrantAbbreviations =
            true;

    #region 快速新增

    [ObservableProperty]
    private string
        newTaskTitle =
            string.Empty;

    [ObservableProperty]
    private string
        newTaskDescription =
            string.Empty;

    [ObservableProperty]
    private bool
        newTaskIsImportant;

    [ObservableProperty]
    private bool
        newTaskIsContinuous;

    [ObservableProperty]
    private DateTime?
        newTaskDueDate;

    [ObservableProperty]
    private QuadrantType
        newTaskQuadrant =
            QuadrantType
                .NotImportantNotUrgent;

    #endregion

    #region 任务详情

    [ObservableProperty]
    private TaskItem?
        editingTask;

    [ObservableProperty]
    private string
        editorTitle =
            string.Empty;

    [ObservableProperty]
    private string
        editorDescription =
            string.Empty;

    [ObservableProperty]
    private DateTime?
        editorDueDate;

    [ObservableProperty]
    private bool
        editorIsImportant;

    [ObservableProperty]
    private bool
        editorIsContinuous;

    [ObservableProperty]
    private QuadrantType
        editorQuadrant =
            QuadrantType
                .NotImportantNotUrgent;

    public bool HasEditingTask =>
        EditingTask is not null;

    public bool CanSetNewTaskContinuous =>
        NewTaskDueDate.HasValue;

    public bool CanSetEditorContinuous =>
        EditorDueDate.HasValue;

    #endregion

    public bool HasTasks =>
        Tasks.Count > 0;

    public string TaskCountText =>
        Tasks.Count == 0
            ? "暂无待办任务"
            : $"共 {Tasks.Count} 项待办";

    public DesktopTaskListViewModel(
        TaskService taskService,
        DialogService dialogService,
        TaskTimeRefreshService?
            timeRefreshService = null)
    {
        _taskService =
            taskService;

        _dialogService =
            dialogService;

        TaskEntriesView =
            CollectionViewSource
                .GetDefaultView(
                    TaskEntries);

        ConfigureTaskEntriesView();

        _taskEditorAutoSave =
            new TaskEditorAutoSaveCoordinator(
                AutoSaveDelay,
                () => EditingTask is not null,
                AutoSaveTaskEditorAsync,
                OnTaskEditorAutoSaveFailed);

        _timeRefreshService =
            timeRefreshService;

        _lastGroupingDate =
            GetToday();

        if (_timeRefreshService is not null)
        {
            _timeRefreshService.RefreshRequested +=
                OnTimeRefreshRequested;
        }

        /*
         * 主窗口、日历、四象限等页面修改任务后，
         * 自动同步桌面任务列表。
         */
        _taskService.TasksChanged +=
            OnTasksChanged;
    }

    #region 加载与日期分组

    private void ConfigureTaskEntriesView()
    {
        TaskDateGroupingService.ConfigureView(
            TaskEntriesView,
            TaskDateGroupSortProfile
                .PreserveSourceOrder);
    }

    /// <summary>
    /// 从数据库读取全部未完成任务。
    /// </summary>
    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy =
            true;

        StatusMessage =
            "正在读取待办任务";

        try
        {
            await ReloadTasksAsync(
                cancellationToken);

            StatusMessage =
                Tasks.Count == 0
                    ? "当前没有未完成任务"
                    : $"当前共有 " +
                      $"{Tasks.Count} 条未完成任务";
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "桌面任务列表加载已取消";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "加载桌面任务列表失败。",
                exception);

            StatusMessage =
                $"加载失败：" +
                $"{exception.Message}";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    private async Task ReloadTasksAsync(
        CancellationToken cancellationToken = default)
    {
        string? editingTaskId =
            EditingTask?.Id;

        IReadOnlyList<TaskItem> tasks =
            await _taskService
                .GetTasksAsync(
                    TodoStatus.Pending,
                    cancellationToken);

        Tasks.Clear();

        foreach (TaskItem task in tasks)
        {
            Tasks.Add(
                task);
        }

        RebuildTaskEntries();

        /*
         * 如果详情仍然打开，
         * 将 EditingTask 指向重新读取后的对象。
         *
         * EditorTitle 等缓冲属性不覆盖，
         * 所以不会打断正在输入的内容。
         */
        if (!string.IsNullOrWhiteSpace(
                editingTaskId))
        {
            TaskItem? refreshedTask =
                Tasks.FirstOrDefault(
                    task =>
                        task.Id ==
                        editingTaskId);

            if (refreshedTask is null &&
                _taskEditorDirtyFields ==
                    TaskEditFields.None &&
                !_hasPendingTaskEditorSave &&
                !_taskEditorHasConflict)
            {
                CloseTaskEditor();
            }
            else if (refreshedTask is not null)
            {
                EditingTask =
                    refreshedTask;
            }
        }
    }

    private void RebuildTaskEntries()
    {
        DateTime today =
            GetToday();

        _lastGroupingDate =
            today;

        TaskDateGroupingService.ReplaceEntries(
            TaskEntries,
            Tasks,
            today);

        TaskEntriesView.Refresh();

        OnPropertyChanged(
            nameof(HasTasks));

        OnPropertyChanged(
            nameof(TaskCountText));
    }

    private void OnTimeRefreshRequested(
        object? sender,
        TaskTimeRefreshEventArgs e)
    {
        DateTime today =
            e.Today;

        bool dateChanged =
            today !=
                _lastGroupingDate;

        bool overdueGroupingChanged =
            TaskDateGroupingService
                .RequiresRegroup(
                    TaskEntries);

        if (!dateChanged &&
            !overdueGroupingChanged)
        {
            return;
        }

        RebuildTaskEntries();

        StatusMessage =
            dateChanged
                ? "日期已变化，任务分组已更新"
                : "任务已到期，分组已更新";
    }

    private async void OnTasksChanged(
        object? sender,
        TaskChangedEventArgs e)
    {
        /*
         * 本 ViewModel 自己的保存/新增/完成/删除，
         * 对应方法会主动更新列表。
         */
        if (e.ChangeSource ==
                TaskChangeSource.DesktopTaskList ||
            e.ChangeType ==
                TaskChangeType.ReminderDelivered ||
            _isPerformingLocalChange ||
            IsBusy)
        {
            return;
        }

        try
        {
            await LoadAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "同步刷新桌面任务列表失败。",
                exception);
        }
    }

    #endregion

    #region 快速新增

    /// <summary>
    /// 打开新增窗口前恢复默认值。
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

        NewTaskQuadrant =
            QuadrantType
                .NotImportantNotUrgent;

        StatusMessage =
            "准备新增任务";
    }

    /// <summary>
    /// 保存新增任务。
    /// </summary>
    public async Task<bool>
        AddQuickTaskAsync()
    {
        if (IsBusy)
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
            TaskEditDraft draft =
                new(
                    NewTaskTitle,
                    NewTaskDescription,
                    NewTaskDueDate,
                    null,
                    false,
                    0,
                    TaskRepeatType.None,
                    NewTaskIsContinuous,
                    NewTaskIsImportant,
                    NewTaskQuadrant);

            _isPerformingLocalChange =
                true;

            TaskItem createdTask;

            try
            {
                createdTask =
                    await _taskService
                        .CreateTaskAsync(
                            draft,
                            changeSource:
                                TaskChangeSource
                                    .DesktopTaskList);
            }
            finally
            {
                _isPerformingLocalChange =
                    false;
            }

            await ReloadTasksAsync();

            StatusMessage =
                $"已新增任务：" +
                $"{createdTask.Title}";

            BeginQuickAdd();

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "桌面任务列表新增任务失败。",
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

    #endregion


    #region 完成任务

    /// <summary>
    /// 将待办任务标记为完成。
    ///
    /// 桌面任务列表只显示 Pending，
    /// 完成后任务会从当前列表移除。
    /// </summary>
    public async Task
        ToggleTaskCompletionAsync(
            TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        if (IsBusy)
        {
            return;
        }

        IsBusy =
            true;

        try
        {
            /*
             * 如果当前还有详情编辑，
             * 先保证最后一轮编辑保存完毕。
             */
            bool saved =
                await FlushTaskEditorAutoSaveAsync();

            if (!saved)
            {
                return;
            }

            if (EditingTask?.Id ==
                task.Id)
            {
                CloseTaskEditor();
            }

            _isPerformingLocalChange =
                true;

            try
            {
                await _taskService
                    .SetCompletionStateAsync(
                        task.Id,
                        TodoStatus.Completed,
                        changeSource:
                            TaskChangeSource
                                .DesktopTaskList);
            }
            finally
            {
                _isPerformingLocalChange =
                    false;
            }

            await ReloadTasksAsync();

            StatusMessage =
                $"已完成任务：" +
                $"{task.Title}";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "桌面任务列表完成任务失败。",
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

    #endregion

    /// <summary>
    /// 桌面窗口隐藏/退出前调用。
    ///
    /// 保存成功返回 true；
    /// 标题为空或数据库保存失败返回 false。
    /// </summary>
    public async Task<bool>
        PrepareForHideAsync()
    {
        bool saved =
            await FlushTaskEditorAutoSaveAsync();

        if (!saved)
        {
            return false;
        }

        CloseTaskEditor();

        return true;
    }

    partial void OnNewTaskDueDateChanged(
        DateTime? value)
    {
        OnPropertyChanged(
            nameof(CanSetNewTaskContinuous));

        if (!value.HasValue &&
            NewTaskIsContinuous)
        {
            NewTaskIsContinuous =
                false;
        }
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
