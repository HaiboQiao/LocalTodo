using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 主窗口四象限和桌面四象限共享的只读任务视图状态。
///
/// 此类型只维护任务集合、象限映射和日期分组；
/// 编辑缓冲、快速新增、自动保存和提示信息仍属于各自会话。
/// </summary>
public sealed class MatrixTaskStore :
    IDisposable
{
    private readonly QuadrantService
        _quadrantService;

    private readonly TaskService
        _taskService;

    private readonly Dictionary<string, QuadrantType>
        _taskQuadrants =
            new(StringComparer.Ordinal);

    private readonly SemaphoreSlim
        _refreshGate =
            new(1, 1);

    private readonly TaskTimeRefreshService?
        _timeRefreshService;

    private DateTime
        _lastDateGroupDate;

    private bool
        _isDisposed;

    public ObservableCollection<TaskItem>
        ImportantAndUrgentTasks
    { get; } = [];

    public ObservableCollection<TaskItem>
        ImportantNotUrgentTasks
    { get; } = [];

    public ObservableCollection<TaskItem>
        UrgentNotImportantTasks
    { get; } = [];

    public ObservableCollection<TaskItem>
        NotImportantNotUrgentTasks
    { get; } = [];

    public MatrixDateGroupedTasks
        ImportantAndUrgentGroups
    { get; } = new();

    public MatrixDateGroupedTasks
        ImportantNotUrgentGroups
    { get; } = new();

    public MatrixDateGroupedTasks
        UrgentNotImportantGroups
    { get; } = new();

    public MatrixDateGroupedTasks
        NotImportantNotUrgentGroups
    { get; } = new();

    public int TotalTaskCount =>
        ImportantAndUrgentTasks.Count +
        ImportantNotUrgentTasks.Count +
        UrgentNotImportantTasks.Count +
        NotImportantNotUrgentTasks.Count;

    public event EventHandler<MatrixTaskStoreChangedEventArgs>?
        Changed;

    public MatrixTaskStore(
        QuadrantService quadrantService,
        TaskService taskService,
        TaskTimeRefreshService?
            timeRefreshService = null)
    {
        ArgumentNullException.ThrowIfNull(
            quadrantService);

        ArgumentNullException.ThrowIfNull(
            taskService);

        _quadrantService =
            quadrantService;

        _taskService =
            taskService;

        _timeRefreshService =
            timeRefreshService;

        _lastDateGroupDate =
            GetToday();

        _taskService.TasksChanged +=
            OnTasksChanged;

        if (_timeRefreshService is not null)
        {
            _timeRefreshService.RefreshRequested +=
                OnTimeRefreshRequested;
        }
    }

    /// <summary>
    /// 重新读取一次共享四象限快照。
    /// 同时发生的刷新会按顺序执行，避免交错修改集合。
    /// </summary>
    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        await _refreshGate
            .WaitAsync();

        try
        {
            QuadrantSnapshot snapshot =
                await _quadrantService
                    .GetSnapshotAsync();

            _taskQuadrants.Clear();

            FillCollection(
                ImportantAndUrgentTasks,
                snapshot.ImportantAndUrgentTasks,
                QuadrantType.ImportantAndUrgent);

            FillCollection(
                ImportantNotUrgentTasks,
                snapshot.ImportantNotUrgentTasks,
                QuadrantType.ImportantNotUrgent);

            FillCollection(
                UrgentNotImportantTasks,
                snapshot.UrgentNotImportantTasks,
                QuadrantType.UrgentNotImportant);

            FillCollection(
                NotImportantNotUrgentTasks,
                snapshot.NotImportantNotUrgentTasks,
                QuadrantType.NotImportantNotUrgent);

            RebuildDateGroups(
                GetToday());

            Changed?.Invoke(
                this,
                new MatrixTaskStoreChangedEventArgs(
                    MatrixTaskStoreChangeKind
                        .SnapshotRefreshed));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public TaskItem? FindTaskById(
        string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId);

        return EnumerateAllTasks()
            .FirstOrDefault(
                task =>
                    task.Id == taskId);
    }

    public QuadrantType GetTaskQuadrant(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        if (_taskQuadrants.TryGetValue(
                task.Id,
                out QuadrantType quadrant))
        {
            return quadrant;
        }

        if (task.QuadrantMode ==
                QuadrantMode.Manual &&
            task.ManualQuadrant.HasValue)
        {
            return task.ManualQuadrant.Value;
        }

        return QuadrantType
            .NotImportantNotUrgent;
    }

    public bool IsTaskInQuadrant(
        TaskItem task,
        QuadrantType quadrant)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        return _taskQuadrants.TryGetValue(
                task.Id,
                out QuadrantType currentQuadrant) &&
            currentQuadrant == quadrant;
    }

    private void FillCollection(
        ObservableCollection<TaskItem> target,
        IReadOnlyList<TaskItem> source,
        QuadrantType quadrant)
    {
        target.Clear();

        foreach (TaskItem task in source)
        {
            target.Add(task);

            _taskQuadrants[task.Id] =
                quadrant;
        }
    }

    private void RebuildDateGroups(
        DateTime today)
    {
        _lastDateGroupDate =
            today;

        ImportantAndUrgentGroups.Replace(
            ImportantAndUrgentTasks,
            today);

        ImportantNotUrgentGroups.Replace(
            ImportantNotUrgentTasks,
            today);

        UrgentNotImportantGroups.Replace(
            UrgentNotImportantTasks,
            today);

        NotImportantNotUrgentGroups.Replace(
            NotImportantNotUrgentTasks,
            today);
    }

    private IEnumerable<TaskItem>
        EnumerateAllTasks()
    {
        return ImportantAndUrgentTasks
            .Concat(
                ImportantNotUrgentTasks)
            .Concat(
                UrgentNotImportantTasks)
            .Concat(
                NotImportantNotUrgentTasks);
    }

    private async void OnTasksChanged(
        object? sender,
        TaskChangedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (e.ChangeType ==
                TaskChangeType.ReminderDelivered ||
            e.ChangeSource is
                TaskChangeSource.MainMatrix or
                TaskChangeSource.DesktopMatrix)
        {
            return;
        }

        try
        {
            await RefreshAsync();
        }
        catch (ObjectDisposedException)
        {
            // 应用退出或测试释放共享仓储时无需再刷新。
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "同步刷新共享四象限任务失败。",
                exception);
        }
    }

    private void OnTimeRefreshRequested(
        object? sender,
        TaskTimeRefreshEventArgs e)
    {
        DateTime today =
            e.Today;

        bool dateChanged =
            today !=
                _lastDateGroupDate;

        bool overdueGroupingChanged =
            ImportantAndUrgentGroups
                .RequiresRegroup() ||
            ImportantNotUrgentGroups
                .RequiresRegroup() ||
            UrgentNotImportantGroups
                .RequiresRegroup() ||
            NotImportantNotUrgentGroups
                .RequiresRegroup();

        if (!dateChanged &&
            !overdueGroupingChanged)
        {
            return;
        }

        RebuildDateGroups(
            today);

        Changed?.Invoke(
            this,
            new MatrixTaskStoreChangedEventArgs(
                MatrixTaskStoreChangeKind
                    .DateGroupsRebuilt));
    }

    private DateTime GetToday()
    {
        return _timeRefreshService?.Today ??
               LocalTimeService.System
                   .ToLocalDateTime(
                       SystemClock.Instance.UtcNow)
                   .Date;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed =
            true;

        _taskService.TasksChanged -=
            OnTasksChanged;

        if (_timeRefreshService is not null)
        {
            _timeRefreshService.RefreshRequested -=
                OnTimeRefreshRequested;
        }

        _refreshGate.Dispose();
    }
}

public enum MatrixTaskStoreChangeKind
{
    SnapshotRefreshed,
    DateGroupsRebuilt
}

public sealed class MatrixTaskStoreChangedEventArgs(
    MatrixTaskStoreChangeKind changeKind) :
    EventArgs
{
    public MatrixTaskStoreChangeKind ChangeKind
    { get; } = changeKind;
}
