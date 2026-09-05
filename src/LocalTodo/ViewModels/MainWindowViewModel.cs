using System;
using System.Windows;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTodo.Data;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

public partial class MainWindowViewModel :
    ObservableObject,
    IPendingChanges
{
    private readonly MainMatrixSessionViewModel
        _matrixViewModel;

    private readonly CalendarViewModel
        _calendarViewModel;

    private readonly WeeklyPlanViewModel
        _weeklyPlanViewModel;

    private readonly AchievementViewModel
        _achievementViewModel;

    private readonly ApplicationWindowService
        _applicationWindowService;

    private readonly DialogService
        _dialogService;

    private readonly PendingChangesCoordinator
        _pendingChangesCoordinator;

    private readonly SemaphoreSlim
        _navigationGate =
            new(1, 1);

    private NavigationItem?
        _displayedNavigationItem;

    private long
        _navigationRequestVersion;

    private bool
        _isNavigationInitialized;

    private bool
        _isRestoringNavigationSelection;

    /// <summary>
    /// Windows 开机自启动服务。
    /// </summary>
    private readonly StartupService
        _startupService;

    private readonly TaskListViewModel
        _allTasksViewModel;

    private readonly TaskListViewModel
        _completedViewModel;

    private readonly TrashViewModel
        _trashViewModel;

    /// <summary>
    /// 桌面任务列表。
    ///
    /// 用于将主窗口中的任务列表显示设置
    /// 同步到桌面任务列表小组件。
    /// </summary>
    private readonly DesktopTaskListViewModel
        _desktopTaskListViewModel;

    private readonly DesktopWidgetStateService
        _desktopWidgetStateService;

    private readonly AppSettingRepository
        _appSettingRepository;

    private const string
        HideQuadrantAbbreviationSettingKey =
            "TaskList.HideQuadrantAbbreviation";

    public ObservableCollection<NavigationItem>
        NavigationItems
    { get; }

    [ObservableProperty]
    private NavigationItem?
        selectedNavigationItem;

    [ObservableProperty]
    private object?
        currentPageViewModel;

    /// <summary>
    /// LocalTodo 是否已经注册为
    /// 当前 Windows 用户的开机自启动程序。
    ///
    /// true  = 开启
    /// false = 关闭
    /// </summary>
    [ObservableProperty]
    private bool
        isStartupEnabled;

    /// <summary>
    /// 桌面任务列表是否处于启用状态。
    /// </summary>
    [ObservableProperty]
    private bool
        isDesktopTaskListWidgetEnabled;

    /// <summary>
    /// 桌面四象限是否处于启用状态。
    /// </summary>
    [ObservableProperty]
    private bool
        isMatrixWidgetEnabled;

    /// <summary>
    /// 是否隐藏所有任务、已完成、垃圾箱
    /// 任务列表中的四象限简写。
    ///
    /// true  = 隐藏
    /// false = 显示
    /// </summary>
    [ObservableProperty]
    private bool
        isTaskListQuadrantAbbreviationHidden;

    public string PageTitle =>
        (_displayedNavigationItem ??
         SelectedNavigationItem)?.Title
        ?? "LocalTodo";

    public string PageDescription =>
        (_displayedNavigationItem ??
         SelectedNavigationItem)?.Description
        ?? "本地离线任务管理程序";

    public MainWindowViewModel(
        TaskService taskService,
        DialogService dialogService,
        MainMatrixSessionViewModel matrixViewModel,
        CalendarViewModel calendarViewModel,
        WeeklyPlanViewModel weeklyPlanViewModel,
        AchievementViewModel achievementViewModel,
        DesktopTaskListViewModel
            desktopTaskListViewModel,
        ApplicationWindowService
            applicationWindowService,
        PendingChangesCoordinator
            pendingChangesCoordinator,
        StartupService
            startupService,
        DesktopWidgetStateService
            desktopWidgetStateService,
        AppSettingRepository
            appSettingRepository,
        TaskTimeRefreshService?
            timeRefreshService = null)
    {
        _matrixViewModel =
            matrixViewModel;

        _calendarViewModel =
            calendarViewModel;

        _weeklyPlanViewModel =
            weeklyPlanViewModel;

        _achievementViewModel =
            achievementViewModel;

        _desktopTaskListViewModel =
            desktopTaskListViewModel;

        _applicationWindowService =
            applicationWindowService;

        _dialogService =
            dialogService;

        _pendingChangesCoordinator =
            pendingChangesCoordinator;

        _startupService =
            startupService;

        taskService.TasksChanged +=
            OnTasksChanged;

        _desktopWidgetStateService =
            desktopWidgetStateService;

        _desktopWidgetStateService.StateChanged +=
            OnDesktopWidgetStateChanged;

        _appSettingRepository =
            appSettingRepository;

        _allTasksViewModel =
            new TaskListViewModel(
                taskService,
                dialogService,
                TodoStatus.Pending,
                timeRefreshService);

        _completedViewModel =
            new TaskListViewModel(
                taskService,
                dialogService,
                TodoStatus.Completed,
                timeRefreshService);

        _trashViewModel =
            new TrashViewModel(
                taskService,
                dialogService);

        NavigationItems =
        [
            new NavigationItem(
                NavigationPage.AllTasks,
                "所有任务",
                "新建、查看和管理全部未完成任务。"),

            new NavigationItem(
                NavigationPage.Calendar,
                "日历",
                "按月份查看任务，并在指定日期新增任务。"),

            new NavigationItem(
                NavigationPage.Matrix,
                "四象限",
                "按照重要性和紧急程度管理任务。"),

            new NavigationItem(
                NavigationPage.WeeklyPlan,
                "每周计划",
                "规划每周固定时间段与日常节奏。"),

            new NavigationItem(
                NavigationPage.Achievements,
                "成长记录",
                "回顾已经取得的成果与成长轨迹。"),

            new NavigationItem(
                NavigationPage.Completed,
                "已完成",
                "查看已经完成的任务记录。"),

            new NavigationItem(
                NavigationPage.Trash,
                "垃圾箱",
                "查看已经删除的任务，可恢复或永久删除。")
        ];

        selectedNavigationItem =
            NavigationItems[0];
    }

    /// <summary>
    /// 打开或关闭 Windows 开机自启动。
    ///
    /// 开关打开：
    /// 将当前 LocalTodo.exe 写入
    /// 当前用户 Windows Run 启动项。
    ///
    /// 开关关闭：
    /// 从 Windows Run 启动项删除 LocalTodo。
    ///
    /// 最后总是重新读取 Windows 中的真实状态，
    /// 避免注册表操作失败后 UI 显示错误。
    /// </summary>
    [RelayCommand]
    private void ToggleStartup()
    {
        bool targetEnabled =
            !IsStartupEnabled;

        try
        {
            if (targetEnabled)
            {
                _startupService.Enable();

                AppLog.Information(
                    "已开启 LocalTodo 开机自启动。");
            }
            else
            {
                _startupService.Disable();

                AppLog.Information(
                    "已关闭 LocalTodo 开机自启动。");
            }
        }
        catch (Exception exception)
        {
            /*
             * 注册表操作失败时不直接使用
             * 用户点击后的目标值。
             *
             * finally 会重新读取真实状态，
             * 因此 UI 最终不会和 Windows 状态不一致。
             */
            AppLog.Error(
                targetEnabled
                    ? "开启 LocalTodo 开机自启动失败。"
                    : "关闭 LocalTodo 开机自启动失败。",
                exception);
        }
        finally
        {
            SyncStartupState();

            /*
             * 即使最终值与原值一样，
             * 也强制刷新开关外观。
             */
            OnPropertyChanged(
                nameof(IsStartupEnabled));
        }
    }

    /// <summary>
    /// 打开或关闭桌面四象限。
    ///
    /// 操作结束后重新读取真实状态，避免窗口操作失败时
    /// 开关外观和实际窗口状态不一致。
    /// </summary>
    [RelayCommand]
    private async Task ToggleMatrixWindowAsync()
    {
        try
        {
            await _applicationWindowService
                .ToggleMatrixWindowAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "切换桌面四象限失败。",
                exception);
        }
        finally
        {
            /*
             * 从 DesktopWidgetStateService 重新获取真实状态。
             */
            SyncDesktopWidgetState();

            /*
             * 即使状态值与原来相同，也强制通知界面重新读取。
             *
             * 例如：
             * 用户点击关闭，但窗口由于某种原因没有成功关闭，
             * 此时属性仍然为 true。普通属性赋值不会触发通知，
             * 所以这里需要手动通知。
             */
            OnPropertyChanged(
                nameof(IsMatrixWidgetEnabled));
        }
    }

    /// <summary>
    /// 打开或关闭桌面任务列表。
    ///
    /// 如果任务详情存在无法保存的内容，桌面任务列表可能拒绝关闭。
    /// 此时需要把开关恢复到开启状态。
    /// </summary>
    [RelayCommand]
    private async Task
    ToggleDesktopTaskListWindowAsync()
    {
        try
        {
            await _applicationWindowService
                .ToggleDesktopTaskListWindowAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "切换桌面任务列表失败。",
                exception);
        }
        finally
        {
            /*
             * 从唯一状态源重新同步。
             */
            SyncDesktopWidgetState();

            /*
             * 强制刷新绑定，保证开关显示真实状态。
             */
            OnPropertyChanged(
                nameof(
                    IsDesktopTaskListWidgetEnabled));
        }
    }

    /// <summary>
    /// 切换任务列表中的四象限简写显示。
    ///
    /// 开关打开：隐藏
    /// 开关关闭：显示
    /// </summary>
    [RelayCommand]
    private async Task
        ToggleTaskListQuadrantAbbreviationAsync()
    {
        bool originalValue =
            IsTaskListQuadrantAbbreviationHidden;

        bool newValue =
            !originalValue;

        /*
         * 先立即更新界面，
         * 让点击开关以后视觉反馈没有延迟。
         */
        IsTaskListQuadrantAbbreviationHidden =
            newValue;

        ApplyTaskListQuadrantVisibility();

        try
        {
            await _appSettingRepository
                .SetValuesAsync(
                    new Dictionary<string, string>
                    {
                        [HideQuadrantAbbreviationSettingKey] =
                            newValue.ToString()
                    });
        }
        catch (Exception exception)
        {
            /*
             * 保存失败时恢复原状态，
             * 避免界面状态和数据库状态不一致。
             */
            IsTaskListQuadrantAbbreviationHidden =
                originalValue;

            ApplyTaskListQuadrantVisibility();

            AppLog.Error(
                "保存四象限简写显示设置失败。",
                exception);
        }
    }

    /// <summary>
    /// 将“四象限简写显示”总设置统一同步到：
    ///
    /// 1. 所有任务；
    /// 2. 已完成；
    /// 3. 垃圾箱；
    /// 4. 桌面任务列表。
    /// </summary>
    private void
        ApplyTaskListQuadrantVisibility()
    {
        /*
         * 设置项保存的是：
         *
         * IsTaskListQuadrantAbbreviationHidden
         *
         * true  = 隐藏
         * false = 显示
         *
         * 而各任务列表 ViewModel 使用的是：
         *
         * ShowQuadrantAbbreviations
         *
         * true  = 显示
         * false = 隐藏
         *
         * 因此这里需要取反。
         */
        bool showQuadrantAbbreviations =
            !IsTaskListQuadrantAbbreviationHidden;

        /*
         * 主窗口：所有任务。
         */
        _allTasksViewModel
            .ShowQuadrantAbbreviations =
                showQuadrantAbbreviations;

        /*
         * 主窗口：已完成。
         */
        _completedViewModel
            .ShowQuadrantAbbreviations =
                showQuadrantAbbreviations;

        /*
         * 主窗口：垃圾箱。
         */
        _trashViewModel
            .ShowQuadrantAbbreviations =
                showQuadrantAbbreviations;

        /*
         * 桌面任务列表小组件。
         *
         * 与主窗口中的任务列表使用完全相同的总设置。
         */
        _desktopTaskListViewModel
            .ShowQuadrantAbbreviations =
                showQuadrantAbbreviations;
    }

    /// <summary>
    /// 从 app_settings 恢复
    /// 四象限简写显示设置。
    ///
    /// 没有保存记录时默认不隐藏，
    /// 保持 LocalTodo 原有显示效果。
    /// </summary>
    private async Task
        LoadTaskListDisplaySettingsAsync()
    {
        try
        {
            string? savedValue =
                await _appSettingRepository
                    .GetValueAsync(
                        HideQuadrantAbbreviationSettingKey);

            IsTaskListQuadrantAbbreviationHidden =
                bool.TryParse(
                    savedValue,
                    out bool hidden) &&
                hidden;
        }
        catch (Exception exception)
        {
            /*
             * 这只是显示偏好。
             * 即使读取失败，也不应该阻止程序启动。
             */
            IsTaskListQuadrantAbbreviationHidden =
                false;

            AppLog.Error(
                "读取四象限简写显示设置失败。",
                exception);
        }

        ApplyTaskListQuadrantVisibility();
    }

    public async Task InitializeAsync()
    {
        /*
         * 首先读取 Windows 开机自启动状态。
         *
         * 主窗口显示以前完成，
         * 因此设置区域第一次显示时
         * 开关就是正确状态。
         */
        SyncStartupState();

        /*
         * 再读取 Widget 偏好。
         */
        await _desktopWidgetStateService
            .LoadAsync();

        SyncDesktopWidgetState();

        /*
         * 主窗口显示以前恢复任务列表显示偏好。
         */
        await LoadTaskListDisplaySettingsAsync();

        await LoadSelectedPageAsync(
            SelectedNavigationItem);

        _displayedNavigationItem =
            SelectedNavigationItem;

        _isNavigationInitialized =
            true;

        NotifyPageHeadingChanged();
    }

    partial void OnSelectedNavigationItemChanged(
        NavigationItem? value)
    {
        if (!_isNavigationInitialized ||
            _isRestoringNavigationSelection)
        {
            return;
        }

        long requestVersion =
            Interlocked.Increment(
                ref _navigationRequestVersion);

        BackgroundTaskObserver.Observe(
            NavigateAfterFlushAsync(
                value,
                requestVersion),
            "切换主窗口页面失败。");
    }

    /// <summary>
    /// 页面内容真正切换以前，先提交当前页面最后一轮编辑。
    /// </summary>
    private async Task NavigateAfterFlushAsync(
        NavigationItem? navigationItem,
        long requestVersion)
    {
        if (navigationItem is null)
        {
            RestoreDisplayedNavigationSelection();
            return;
        }

        await _navigationGate.WaitAsync();

        try
        {
            if (requestVersion !=
                    Volatile.Read(
                        ref _navigationRequestVersion))
            {
                return;
            }

            if (_applicationWindowService
                .IsLifecycleOperationInProgress)
            {
                RestoreDisplayedNavigationSelection();
                return;
            }

            IPendingChanges? currentEditor =
                CurrentPageViewModel
                    as IPendingChanges;

            if (currentEditor is not null)
            {
                bool canNavigate =
                    await _pendingChangesCoordinator
                        .PrepareForTransitionAsync(
                            [currentEditor],
                            result =>
                                _dialogService
                                    .ConfirmDiscardPendingChanges(
                                        "切换页面",
                                        result.Message));

                if (!canNavigate)
                {
                    RestoreDisplayedNavigationSelection();
                    return;
                }
            }

            /*
             * 保存期间用户可能又点击了另一个页面。
             * 只处理最后一次请求，避免先后加载多个过期页面。
             */
            if (requestVersion !=
                    Volatile.Read(
                        ref _navigationRequestVersion))
            {
                return;
            }

            await LoadSelectedPageAsync(
                navigationItem);

            _displayedNavigationItem =
                navigationItem;

            NotifyPageHeadingChanged();
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private void
        RestoreDisplayedNavigationSelection()
    {
        if (_displayedNavigationItem is null)
        {
            return;
        }

        _isRestoringNavigationSelection =
            true;

        try
        {
            SelectedNavigationItem =
                _displayedNavigationItem;
        }
        finally
        {
            _isRestoringNavigationSelection =
                false;
        }

        NotifyPageHeadingChanged();
    }

    private void NotifyPageHeadingChanged()
    {
        OnPropertyChanged(
            nameof(PageTitle));

        OnPropertyChanged(
            nameof(PageDescription));
    }

    /// <summary>
    /// 主窗口隐藏或正式退出时，提交主窗口内全部编辑器。
    /// </summary>
    public Task<FlushResult>
        FlushPendingChangesAsync()
    {
        return _pendingChangesCoordinator
            .FlushAllAsync(
            [
                _allTasksViewModel,
                _completedViewModel,
                _calendarViewModel,
                _matrixViewModel
            ]);
    }

    public void DiscardPendingChanges()
    {
        _pendingChangesCoordinator
            .DiscardAll(
            [
                _allTasksViewModel,
                _completedViewModel,
                _calendarViewModel,
                _matrixViewModel
            ]);
    }

    private async Task LoadSelectedPageAsync(
        NavigationItem? navigationItem)
    {
        if (navigationItem is null)
        {
            return;
        }

        try
        {
            switch (navigationItem.Page)
            {
                case NavigationPage.AllTasks:
                    await ShowTaskPageAsync(
                        _allTasksViewModel);

                    break;

                case NavigationPage.Calendar:
                    CurrentPageViewModel =
                        _calendarViewModel;

                    await _calendarViewModel
                        .LoadAsync();

                    break;

                case NavigationPage.Matrix:
                    CurrentPageViewModel =
                        _matrixViewModel;

                    await _matrixViewModel
                        .LoadAsync();

                    break;

                case NavigationPage.WeeklyPlan:
                    CurrentPageViewModel =
                        _weeklyPlanViewModel;

                    await _weeklyPlanViewModel
                        .LoadAsync();

                    break;

                case NavigationPage.Achievements:
                    CurrentPageViewModel =
                        _achievementViewModel;

                    await _achievementViewModel
                        .LoadAsync();

                    break;

                case NavigationPage.Completed:
                    await ShowTaskPageAsync(
                        _completedViewModel);

                    break;

                case NavigationPage.Trash:
                    CurrentPageViewModel =
                        _trashViewModel;

                    await _trashViewModel
                        .LoadAsync();

                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(navigationItem.Page),
                        navigationItem.Page,
                        "无法识别导航页面。");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(
                $"加载页面失败：" +
                $"{navigationItem.Title}",
                exception);

            CurrentPageViewModel =
                new PlaceholderPageViewModel(
                    navigationItem.Title,
                    $"页面加载失败：" +
                    $"{exception.Message}");
        }
    }

    private async Task ShowTaskPageAsync(
        TaskListViewModel viewModel)
    {
        CurrentPageViewModel =
            viewModel;

        await viewModel.LoadAsync();
    }

    private async void OnTasksChanged(
        object? sender,
        TaskChangedEventArgs e)
    {
        try
        {
            if (e.ChangeType ==
                TaskChangeType.ReminderDelivered)
            {
                return;
            }

            switch (CurrentPageViewModel)
            {
                case TaskListViewModel taskListViewModel:
                    if (e.ChangeSource ==
                        taskListViewModel.ChangeSource)
                    {
                        break;
                    }

                    await taskListViewModel
                        .LoadAsync();

                    break;

                case TrashViewModel trashViewModel:
                    if (e.ChangeSource ==
                        TaskChangeSource.Trash)
                    {
                        break;
                    }

                    await trashViewModel
                        .LoadAsync();

                    break;

                case CalendarViewModel calendarViewModel:

                    if (e.ChangeSource ==
                        TaskChangeSource.Calendar)
                    {
                        break;
                    }

                    /*
                     * Calendar 任务详情正在自行保存时，
                     * TaskService.TasksChanged 是这次本地保存产生的。
                     *
                     * 此时不能立刻重新 LoadAsync，
                     * 否则会在鼠标点击过程中清空并重建 42 个日期格，
                     * 导致下一次任务点击或日期格点击丢失。
                     */
                    if (calendarViewModel
                        .IsSavingTaskEditorLocally)
                    {
                        break;
                    }

                    /*
                     * 来自其他页面或桌面四象限的变化，
                     * 仍然正常刷新 Calendar。
                     */
                    await calendarViewModel
                        .LoadAsync();

                    break;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "同步刷新主窗口任务页面失败。",
                exception);
        }
    }

    /// <summary>
    /// DesktopWidgetStateService 状态变化以后，
    /// 同步主窗口中的两个按钮。
    /// </summary>
    private void OnDesktopWidgetStateChanged(
        object? sender,
        EventArgs e)
    {
        /*
         * 保险起见统一回到 WPF UI 线程。
         *
         * 当前大多数状态变化本来就发生在 UI 线程，
         * 但这样写以后即使未来状态来自其他线程，
         * 也不会出现跨线程 UI 更新问题。
         */
        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            SyncDesktopWidgetState();

            return;
        }

        Application.Current.Dispatcher
            .Invoke(
                SyncDesktopWidgetState);
    }

    /// <summary>
    /// 从唯一状态源同步两个按钮。
    /// </summary>
    private void SyncDesktopWidgetState()
    {
        IsDesktopTaskListWidgetEnabled =
            _desktopWidgetStateService
                .IsDesktopTaskListEnabled;

        IsMatrixWidgetEnabled =
            _desktopWidgetStateService
                .IsMatrixEnabled;
    }

    /// <summary>
    /// 从 Windows 注册表读取
    /// 当前真实的开机自启动状态。
    /// </summary>
    private void SyncStartupState()
    {
        try
        {
            IsStartupEnabled =
                _startupService.IsEnabled();
        }
        catch (Exception exception)
        {
            /*
             * 读取失败时保守显示为关闭。
             *
             * 读取启动设置失败不应该影响
             * LocalTodo 本身正常使用。
             */
            IsStartupEnabled =
                false;

            AppLog.Error(
                "读取 LocalTodo 开机自启动状态失败。",
                exception);
        }
    }
}

public enum NavigationPage
{
    AllTasks,
    Calendar,
    Matrix,
    WeeklyPlan,
    Achievements,
    Completed,
    Trash
}

public sealed record NavigationItem(
    NavigationPage Page,
    string Title,
    string Description);
