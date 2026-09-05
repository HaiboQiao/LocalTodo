using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.ViewModels;
using LocalTodo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LocalTodo.Services;

/// <summary>
/// 统一管理 LocalTodo 的窗口。
/// </summary>
public sealed class ApplicationWindowService
{
    private readonly IServiceProvider
        _serviceProvider;

    private readonly DesktopWidgetStateService
        _desktopWidgetStateService;

    private readonly DialogService
        _dialogService;

    private readonly PendingChangesCoordinator
        _pendingChangesCoordinator;

    private readonly bool
        _enableApplicationSideEffects;

    private bool
        _isExitInProgress;

    private MainWindow?
        _mainWindow;

    private MatrixWindow?
        _matrixWindow;

    private DesktopTaskListWindow?
        _desktopTaskListWindow;

    public bool IsExitRequested
    {
        get;
        private set;
    }

    /// <summary>
    /// 正在执行页面不可逆的隐藏或正式退出流程时，
    /// 新的导航和窗口操作应暂时停止。
    /// </summary>
    public bool IsLifecycleOperationInProgress =>
        _isExitInProgress ||
        IsExitRequested;

    public ApplicationWindowService(
        IServiceProvider serviceProvider,
        DesktopWidgetStateService
            desktopWidgetStateService,
        DialogService dialogService,
        PendingChangesCoordinator
            pendingChangesCoordinator)
        : this(
            serviceProvider,
            desktopWidgetStateService,
            dialogService,
            pendingChangesCoordinator,
            enableApplicationSideEffects: true)
    {
    }

    internal ApplicationWindowService(
        IServiceProvider serviceProvider,
        DesktopWidgetStateService
            desktopWidgetStateService,
        DialogService dialogService,
        PendingChangesCoordinator
            pendingChangesCoordinator,
        bool enableApplicationSideEffects)
    {
        ArgumentNullException.ThrowIfNull(
            serviceProvider);

        ArgumentNullException.ThrowIfNull(
            desktopWidgetStateService);

        ArgumentNullException.ThrowIfNull(
            dialogService);

        ArgumentNullException.ThrowIfNull(
            pendingChangesCoordinator);

        _serviceProvider =
            serviceProvider;

        _desktopWidgetStateService =
            desktopWidgetStateService;

        _dialogService =
            dialogService;

        _pendingChangesCoordinator =
            pendingChangesCoordinator;

        _enableApplicationSideEffects =
            enableApplicationSideEffects;
    }

    /// <summary>
    /// Windows 正在注销或关机时调用。
    ///
    /// 系统会关闭 LocalTodo 的窗口，
    /// 但这种窗口关闭不能被解释成：
    /// “用户主动关闭桌面小组件”。
    ///
    /// 因此提前进入应用退出状态，
    /// 让 MatrixWindow 和 DesktopTaskListWindow
    /// 的 Closing 处理器跳过普通 Hide 流程，
    /// 从而保留用户已经保存的小组件启用偏好。
    /// </summary>
    public void BeginSystemSessionEnding()
    {
        _isExitInProgress =
            true;

        IsExitRequested =
            true;

        SetExistingWindowsEnabled(
            false);

        if (_enableApplicationSideEffects)
        {
            AppLog.Information(
                "Windows 会话正在结束，" +
                "保留桌面小组件启用状态。");
        }
    }

    /// <summary>
    /// LocalTodo 启动时恢复用户上次保存的
    /// 桌面小组件启用状态。
    ///
    /// 注意：
    /// 这里只恢复窗口显示，
    /// 不改变保存的偏好值。
    /// </summary>
    public async Task RestoreDesktopWidgetsAsync()
    {
        await _desktopWidgetStateService
            .LoadAsync();

        if (_desktopWidgetStateService
            .IsDesktopTaskListEnabled)
        {
            await ExecuteOnUiThreadAsync(
                () => ShowDesktopTaskListWindowCoreAsync(
                    activate: false));
        }

        if (_desktopWidgetStateService
            .IsMatrixEnabled)
        {
            await ExecuteOnUiThreadAsync(
                () => ShowMatrixWindowCoreAsync(
                    activate: false));
        }
    }

    /// <summary>
    /// 显示并激活主窗口。
    /// </summary>
    public async Task ShowMainWindowAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return;
        }

        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            await ShowMainWindowCoreAsync();
            return;
        }

        await Application.Current.Dispatcher
            .InvokeAsync(
                ShowMainWindowCoreAsync)
            .Task
            .Unwrap();
    }

    /// <summary>
    /// 显示桌面四象限，
    /// 并记录用户已经启用了这个桌面组件。
    /// </summary>
    public async Task
        ShowMatrixWindowAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return;
        }

        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            await ShowMatrixWindowCoreAsync(
                activate: true);
        }
        else
        {
            await Application.Current.Dispatcher
                .InvokeAsync(
                    () => ShowMatrixWindowCoreAsync(
                        activate: true))
                .Task
                .Unwrap();
        }

        await _desktopWidgetStateService
            .SetMatrixEnabledAsync(
                true);
    }

    /// <summary>
    /// 隐藏桌面四象限，并记录用户已经关闭它。
    /// </summary>
    public async Task<bool> HideMatrixWindowAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return false;
        }

        bool hidden;

        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            hidden =
                await HideMatrixWindowCoreAsync();
        }
        else
        {
            hidden =
                await Application.Current.Dispatcher
                    .InvokeAsync(
                        HideMatrixWindowCoreAsync)
                    .Task
                    .Unwrap();
        }

        if (!hidden)
        {
            return false;
        }

        await _desktopWidgetStateService
            .SetMatrixEnabledAsync(
                false);

        return true;
    }

    /// <summary>
    /// 显示桌面任务列表，
    /// 并记录用户已经启用了这个桌面组件。
    /// </summary>
    public async Task
        ShowDesktopTaskListWindowAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return;
        }

        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            await ShowDesktopTaskListWindowCoreAsync(
                activate: true);
        }
        else
        {
            await Application.Current.Dispatcher
                .InvokeAsync(
                    () => ShowDesktopTaskListWindowCoreAsync(
                        activate: true))
                .Task
                .Unwrap();
        }

        /*
         * 只有窗口真正显示以后，
         * 才把持久状态改成 true。
         *
         * DesktopWidgetStateService 会同时触发
         * StateChanged，主窗口按钮因此同步更新。
         */
        await _desktopWidgetStateService
            .SetDesktopTaskListEnabledAsync(
                true);
    }

    /// <summary>
    /// 隐藏桌面任务列表，并记录用户已经关闭它。
    ///
    /// 返回 false 表示当前详情存在无法保存的内容，
    /// 因而没有关闭窗口。
    /// </summary>
    public async Task<bool>
        HideDesktopTaskListWindowAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return false;
        }

        bool hidden;

        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            hidden =
                await HideDesktopTaskListWindowCoreAsync();
        }
        else
        {
            hidden =
                await Application.Current.Dispatcher
                    .InvokeAsync(
                        HideDesktopTaskListWindowCoreAsync)
                    .Task
                    .Unwrap();
        }

        if (!hidden)
        {
            return false;
        }

        await _desktopWidgetStateService
            .SetDesktopTaskListEnabledAsync(
                false);

        return true;
    }

    /// <summary>
    /// 点击主窗口关闭按钮时，先保存全部主窗口编辑器，
    /// 成功后才隐藏到托盘。
    /// </summary>
    public async Task<bool> HideMainWindowAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return false;
        }

        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            return await HideMainWindowCoreAsync();
        }

        return await Application.Current.Dispatcher
            .InvokeAsync(
                HideMainWindowCoreAsync)
            .Task
            .Unwrap();
    }

    /// <summary>
    /// 正式退出程序。
    /// </summary>
    public async Task ExitApplicationAsync()
    {
        if (IsLifecycleOperationInProgress)
        {
            return;
        }

        _isExitInProgress =
            true;

        try
        {
            if (Application.Current.Dispatcher
                .CheckAccess())
            {
                await ExitApplicationCoreAsync();
                return;
            }

            await Application.Current.Dispatcher
                .InvokeAsync(
                    ExitApplicationCoreAsync)
                .Task
                .Unwrap();
        }
        finally
        {
            if (!IsExitRequested)
            {
                _isExitInProgress =
                    false;
            }
        }
    }

    /// <summary>
    /// Windows 注销或关机时使用的有界尽力保存。
    /// 该方法不修改桌面小组件启用偏好。
    /// </summary>
    public Task<FlushResult>
        FlushForSystemSessionEndingAsync(
            TimeSpan timeout)
    {
        return _pendingChangesCoordinator
            .FlushAllWithinAsync(
                GetApplicationEditors(),
                timeout);
    }

    private async Task ShowMainWindowCoreAsync()
    {
        MainWindow window =
            GetMainWindow();

        await window.PrepareToShowAsync();

        if (Application.Current.MainWindow
            is null)
        {
            Application.Current.MainWindow =
                window;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState ==
            WindowState.Minimized)
        {
            window.WindowState =
                WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    }

    private async Task
    ShowMatrixWindowCoreAsync(
        bool activate)
    {
        MatrixWindow window =
            GetMatrixWindow();

        await window.PrepareToShowAsync();

        if (!window.IsVisible)
        {
            window.ShowActivated =
                activate;

            window.Show();
        }

        if (window.WindowState ==
            WindowState.Minimized)
        {
            window.WindowState =
                WindowState.Normal;
        }

        /*
         * Show() 后 HWND 已经创建，
         * 此时建立桌面宿主关系。
         *
         * 不再人为调整 Z 顺序，
         * 后续前后层级完全交给 Windows。
        */
        window.AttachToDesktop();

        if (activate)
        {
            window.Activate();
            window.Focus();
        }
        else
        {
            window.PlaceBehindForegroundWithoutActivation();
        }
    }

    private async Task
    ShowDesktopTaskListWindowCoreAsync(
        bool activate)
    {
        DesktopTaskListWindow window =
            GetDesktopTaskListWindow();

        await window.PrepareToShowAsync();

        if (!window.IsVisible)
        {
            window.ShowActivated =
                activate;

            window.Show();
        }

        if (window.WindowState ==
            WindowState.Minimized)
        {
            window.WindowState =
                WindowState.Normal;
        }

        window.AttachToDesktop();

        if (activate)
        {
            window.Activate();
            window.Focus();
        }
        else
        {
            window.PlaceBehindForegroundWithoutActivation();
        }
    }

    private async Task
        ExitApplicationCoreAsync()
    {
        SetExistingWindowsEnabled(
            false);

        bool canExit;

        try
        {
            canExit =
                await _pendingChangesCoordinator
                    .PrepareForTransitionAsync(
                        GetApplicationEditors(),
                        result =>
                            _dialogService
                                .ConfirmDiscardPendingChanges(
                                    "退出 LocalTodo",
                                    result.Message));
        }
        catch
        {
            SetExistingWindowsEnabled(
                true);

            throw;
        }

        if (!canExit)
        {
            SetExistingWindowsEnabled(
                true);

            return;
        }

        /*
         * 只有保存成功或用户明确放弃以后，
         * 才允许各窗口的 Closing 处理器真正关闭窗口。
         */
        IsExitRequested =
            true;

        if (_matrixWindow is not null)
        {
            await _matrixWindow
                .SavePlacementNowAsync();
        }

        if (_desktopTaskListWindow
            is not null)
        {
            await _desktopTaskListWindow
                .SavePlacementNowAsync();
        }

        if (_mainWindow is not null)
        {
            await _mainWindow
                .SavePlacementNowAsync();

        }

        /*
         * 编辑器和窗口位置都已提交，
         * 现在停止所有可能产生新操作的后台入口。
         */
        _serviceProvider
            .GetService<ReminderService>()?
            .Stop();

        _serviceProvider
            .GetService<TrayIconService>()?
            .Stop();

        if (_matrixWindow is not null)
        {
            _matrixWindow
                .DetachFromDesktop();

            _matrixWindow.Close();
        }

        if (_desktopTaskListWindow
            is not null)
        {
            _desktopTaskListWindow
                .DetachFromDesktop();

            _desktopTaskListWindow
                .Close();
        }

        if (_mainWindow is not null)
        {

            _mainWindow.Close();
        }

        Application.Current.Shutdown();
    }

    private MainWindow GetMainWindow()
    {
        return _mainWindow ??=
            _serviceProvider
                .GetRequiredService<
                    MainWindow>();
    }

    private MatrixWindow GetMatrixWindow()
    {
        return _matrixWindow ??=
            _serviceProvider
                .GetRequiredService<
                    MatrixWindow>();
    }

    private DesktopTaskListWindow
    GetDesktopTaskListWindow()
    {
        return _desktopTaskListWindow ??=
            _serviceProvider
                .GetRequiredService<
                    DesktopTaskListWindow>();
    }

    private static async Task
    ExecuteOnUiThreadAsync(
        Func<Task> action)
    {
        if (Application.Current.Dispatcher
            .CheckAccess())
        {
            await action();
            return;
        }

        await Application.Current.Dispatcher
            .InvokeAsync(action)
            .Task
            .Unwrap();
    }

    private async Task<bool>
    HideMatrixWindowCoreAsync()
    {
        if (_matrixWindow is null)
        {
            return true;
        }

        _matrixWindow.IsEnabled =
            false;

        try
        {
            DesktopMatrixSessionViewModel viewModel =
                _serviceProvider
                    .GetRequiredService<
                        DesktopMatrixSessionViewModel>();

            bool canHide =
                await _pendingChangesCoordinator
                    .PrepareForTransitionAsync(
                        [viewModel],
                        result =>
                            _dialogService
                                .ConfirmDiscardPendingChanges(
                                    "隐藏桌面四象限",
                                    result.Message));

            if (!canHide)
            {
                return false;
            }

            await _matrixWindow
                .SavePlacementNowAsync();

            _matrixWindow.Hide();

            return true;
        }
        finally
        {
            _matrixWindow.IsEnabled =
                true;
        }
    }

    private async Task<bool>
    HideDesktopTaskListWindowCoreAsync()
    {
        if (_desktopTaskListWindow
            is null)
        {
            return true;
        }

        _desktopTaskListWindow.IsEnabled =
            false;

        try
        {
            DesktopTaskListViewModel viewModel =
                _serviceProvider
                    .GetRequiredService<
                        DesktopTaskListViewModel>();

            bool canHide =
                await _pendingChangesCoordinator
                    .PrepareForTransitionAsync(
                        [viewModel],
                        result =>
                            _dialogService
                                .ConfirmDiscardPendingChanges(
                                    "隐藏桌面任务列表",
                                    result.Message));

            if (!canHide)
            {
                return false;
            }

            /*
             * 保留现有行为：隐藏桌面任务列表时关闭详情 Popup。
             * 前面的统一保存屏障已完成，因此这里不会丢失输入。
             */
            await _desktopTaskListWindow
                .PrepareToCloseAsync();

            await _desktopTaskListWindow
                .SavePlacementNowAsync();

            _desktopTaskListWindow.Hide();

            return true;
        }
        finally
        {
            _desktopTaskListWindow.IsEnabled =
                true;
        }
    }

    private async Task<bool>
        HideMainWindowCoreAsync()
    {
        if (_mainWindow is null)
        {
            return true;
        }

        _mainWindow.IsEnabled =
            false;

        try
        {
            MainWindowViewModel viewModel =
                _serviceProvider
                    .GetRequiredService<
                        MainWindowViewModel>();

            bool canHide =
                await _pendingChangesCoordinator
                    .PrepareForTransitionAsync(
                        [viewModel],
                        result =>
                            _dialogService
                                .ConfirmDiscardPendingChanges(
                                    "隐藏主窗口",
                                    result.Message));

            if (!canHide)
            {
                return false;
            }

            await _mainWindow
                .SavePlacementNowAsync();

            _mainWindow.Hide();

            return true;
        }
        finally
        {
            _mainWindow.IsEnabled =
                true;
        }
    }

    private IReadOnlyList<IPendingChanges>
        GetApplicationEditors()
    {
        return
        [
            _serviceProvider
                .GetRequiredService<
                    MainWindowViewModel>(),

            _serviceProvider
                .GetRequiredService<
                    DesktopTaskListViewModel>(),

            _serviceProvider
                .GetRequiredService<
                    DesktopMatrixSessionViewModel>()
        ];
    }

    private void SetExistingWindowsEnabled(
        bool isEnabled)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.IsEnabled =
                isEnabled;
        }

        if (_matrixWindow is not null)
        {
            _matrixWindow.IsEnabled =
                isEnabled;
        }

        if (_desktopTaskListWindow
            is not null)
        {
            _desktopTaskListWindow.IsEnabled =
                isEnabled;
        }
    }

    public async Task
    ToggleDesktopTaskListWindowAsync()
    {
        await _desktopWidgetStateService
            .LoadAsync();

        if (_desktopWidgetStateService
            .IsDesktopTaskListEnabled)
        {
            await HideDesktopTaskListWindowAsync();
        }
        else
        {
            /*
             * 注意：
             * 这里调用公开的 Show 方法，
             * 不要直接调用 Show...CoreAsync。
             *
             * 因为公开 Show 方法还负责保存 true。
             */
            await ShowDesktopTaskListWindowAsync();
        }
    }

    public async Task
    ToggleMatrixWindowAsync()
    {
        await _desktopWidgetStateService
            .LoadAsync();

        if (_desktopWidgetStateService
            .IsMatrixEnabled)
        {
            await HideMatrixWindowAsync();
        }
        else
        {
            await ShowMatrixWindowAsync();
        }
    }

}
