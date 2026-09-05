using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;
using LocalTodo.ViewModels;

namespace LocalTodo.Views;

public partial class DesktopTaskListWindow :
    Window
{
    private readonly
        DesktopTaskListViewModel
            _viewModel;

    private readonly
        WindowPlacementService
            _windowPlacementService;

    private readonly
        ApplicationWindowService
            _applicationWindowService;

    private readonly
        DesktopWidgetHostService
            _desktopWidgetHostService;

    private HwndSource?
        _hwndSource;

    private readonly DispatcherTimer
        _placementSaveTimer;

    private bool
        _placementLoaded;

    private bool
        _isApplyingPlacement;

    public DesktopTaskListWindow(
        DesktopTaskListViewModel viewModel,
        WindowPlacementService
            windowPlacementService,
        ApplicationWindowService
            applicationWindowService,
        DesktopWidgetHostService
            desktopWidgetHostService)
    {
        ArgumentNullException.ThrowIfNull(
            viewModel);

        ArgumentNullException.ThrowIfNull(
            windowPlacementService);

        ArgumentNullException.ThrowIfNull(
            applicationWindowService);

        ArgumentNullException.ThrowIfNull(
            desktopWidgetHostService);

        _viewModel =
            viewModel;

        _windowPlacementService =
            windowPlacementService;

        _applicationWindowService =
            applicationWindowService;

        _desktopWidgetHostService =
            desktopWidgetHostService;

        _placementSaveTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan
                        .FromMilliseconds(
                            500)
            };

        InitializeComponent();

        DataContext =
            _viewModel;

        _placementSaveTimer.Tick +=
            OnPlacementSaveTimerTick;

        LocationChanged +=
            OnWindowPlacementChanged;

        SizeChanged +=
            OnWindowPlacementChanged;

        StateChanged +=
            OnWindowPlacementChanged;

        Closing +=
            OnWindowClosing;

        SourceInitialized +=
            OnWindowSourceInitialized;

        Closed +=
            OnWindowClosed;
    }

    /// <summary>
    /// 将窗口绑定到 Windows 桌面层。
    /// </summary>
    public bool AttachToDesktop()
    {
        return _desktopWidgetHostService
            .Attach(
                this);
    }

    /// <summary>
    /// 开机恢复时在不抢占焦点的情况下，
    /// 将桌面任务列表放到当前前台窗口之后。
    /// </summary>
    public void PlaceBehindForegroundWithoutActivation()
    {
        _desktopWidgetHostService
            .PlaceBehindForegroundWithoutActivation(
                this);
    }

    /// <summary>
    /// 解除桌面宿主。
    /// </summary>
    public void DetachFromDesktop()
    {
        _desktopWidgetHostService
            .Detach(
                this);
    }

    /// <summary>
    /// 每次显示窗口前调用。
    /// </summary>
    public async Task PrepareToShowAsync()
    {
        if (!_placementLoaded)
        {
            WindowPlacement placement =
                await _windowPlacementService
                    .LoadDesktopTaskListWindowPlacementAsync();

            ApplyPlacement(
                placement);

            _placementLoaded =
                true;
        }

        await _viewModel
            .LoadAsync();
    }

    /// <summary>
    /// 隐藏或退出前保存详情。
    /// </summary>
    public Task<bool>
        PrepareToCloseAsync()
    {
        return DesktopTaskListViewControl
            .PrepareToHideAsync();
    }

    /// <summary>
    /// 立即保存窗口位置和尺寸。
    /// </summary>
    public async Task SavePlacementNowAsync()
    {
        if (!_placementLoaded)
        {
            return;
        }

        _placementSaveTimer.Stop();

        try
        {
            WindowPlacement placement =
                CapturePlacement();

            await _windowPlacementService
                .SaveDesktopTaskListWindowPlacementAsync(
                    placement);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "保存桌面任务列表窗口位置和尺寸失败。",
                exception);
        }
    }

    private void ApplyPlacement(
        WindowPlacement placement)
    {
        _isApplyingPlacement =
            true;

        try
        {
            WindowStartupLocation =
                WindowStartupLocation.Manual;

            Left =
                placement.Left;

            Top =
                placement.Top;

            Width =
                placement.Width;

            Height =
                placement.Height;
        }
        finally
        {
            _isApplyingPlacement =
                false;
        }
    }

    private WindowPlacement
        CapturePlacement()
    {
        Rect bounds =
            WindowState ==
                WindowState.Normal
                ? new Rect(
                    Left,
                    Top,
                    ActualWidth,
                    ActualHeight)
                : RestoreBounds;

        return new WindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height);
    }

    private void OnWindowPlacementChanged(
        object? sender,
        EventArgs e)
    {
        if (!_placementLoaded ||
            _isApplyingPlacement ||
            WindowState ==
                WindowState.Minimized)
        {
            return;
        }

        /*
         * 拖动或缩放时 500ms 防抖保存。
         */
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private async void
        OnPlacementSaveTimerTick(
            object? sender,
            EventArgs e)
    {
        await SavePlacementNowAsync();
    }

    #region Explorer 桌面宿主

    private void OnWindowSourceInitialized(
        object? sender,
        EventArgs e)
    {
        WindowInteropHelper helper =
            new(this);

        _hwndSource =
            HwndSource.FromHwnd(
                helper.Handle);

        _hwndSource?.AddHook(
            WindowMessageHook);
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (unchecked(
                (uint)message) ==
            _desktopWidgetHostService
                .TaskbarCreatedMessage)
        {
            /*
             * Explorer 重启以后，
             * 稍等桌面宿主重新创建。
             */
            BackgroundTaskObserver.Observe(
                ReattachAfterExplorerRestartAsync(),
                "Explorer 重启后重新绑定桌面任务列表失败。");
        }

        return IntPtr.Zero;
    }

    private async Task
        ReattachAfterExplorerRestartAsync()
    {
        await Task.Delay(
            1200);

        if (!IsVisible)
        {
            return;
        }

        _desktopWidgetHostService
            .Attach(
                this);
    }
    private void OnWindowClosed(
        object? sender,
        EventArgs e)
    {
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(
                WindowMessageHook);

            _hwndSource =
                null;
        }

        DetachFromDesktop();
    }

    #endregion

    private async void OnWindowClosing(
    object? sender,
    CancelEventArgs e)
    {
        if (_applicationWindowService
            .IsExitRequested)
        {
            return;
        }

        e.Cancel =
            true;

        try
        {
            await _applicationWindowService
                .HideDesktopTaskListWindowAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "隐藏桌面任务列表前保存编辑内容失败。",
                exception);
        }
    }

    private void
        TitleBar_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        if (e.ChangedButton !=
            MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState =
                WindowState ==
                    WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;

            return;
        }

        try
        {
            DragMove();
        }
        catch (
            InvalidOperationException)
        {
            /*
             * 鼠标状态在 DragMove 中发生改变时，
             * WPF 可能取消此次拖动。
             */
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
