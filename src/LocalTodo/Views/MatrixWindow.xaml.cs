using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;
using LocalTodo.ViewModels;
using System.Windows.Interop;

namespace LocalTodo.Views;

public partial class MatrixWindow :
    Window
{
    private readonly DesktopMatrixSessionViewModel
        _viewModel;

    private readonly WindowPlacementService
        _windowPlacementService;

    private readonly ApplicationWindowService
        _applicationWindowService;

    private readonly DesktopWidgetHostService
        _desktopWidgetHostService;

    private HwndSource?
        _hwndSource;

    private readonly DispatcherTimer
        _placementSaveTimer;

    private bool
        _placementLoaded;

    private bool
        _isApplyingPlacement;

    public MatrixWindow(
    DesktopMatrixSessionViewModel viewModel,
    WindowPlacementService windowPlacementService,
    ApplicationWindowService applicationWindowService,
    DesktopWidgetHostService desktopWidgetHostService)
    {
        /*
         * 在窗口真正显示之前验证全部构造依赖。
         *
         * 如果以后依赖注入配置发生错误，
         * 会在创建窗口时直接指出是哪个参数为空，
         * 而不是等到打开或关闭窗口时才空引用。
         */
        ArgumentNullException.ThrowIfNull(
            viewModel);

        ArgumentNullException.ThrowIfNull(
            windowPlacementService);

        ArgumentNullException.ThrowIfNull(
            applicationWindowService);

        ArgumentNullException.ThrowIfNull(
            desktopWidgetHostService);

        /*
         * 先完成字段赋值。
         *
         * _viewModel 用于窗口代码加载四象限数据；
         * DataContext 则供 MatrixView 的 XAML 绑定使用。
         * 两处都需要设置。
         */
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
                    TimeSpan.FromMilliseconds(
                        500)
            };

        /*
         * 所有必要字段准备完成后再加载 XAML。
         */
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

        /*
         * 桌面部件模式需要的事件。
         */
        SourceInitialized +=
            OnWindowSourceInitialized;

        Closed +=
            OnWindowClosed;
    }

    /// <summary>
    /// 将桌面四象限绑定到 Windows 桌面层。
    /// </summary>
    public bool AttachToDesktop()
    {
        return _desktopWidgetHostService
            .Attach(
                this);
    }

    /// <summary>
    /// 开机恢复时在不抢占焦点的情况下，
    /// 将桌面四象限放到当前前台窗口之后。
    /// </summary>
    public void PlaceBehindForegroundWithoutActivation()
    {
        _desktopWidgetHostService
            .PlaceBehindForegroundWithoutActivation(
                this);
    }

    /// <summary>
    /// 解除桌面层绑定。
    /// 正式退出程序前调用。
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
                    .LoadMatrixWindowPlacementAsync();

            ApplyPlacement(placement);

            _placementLoaded = true;
        }

        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// 立即保存窗口位置。
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
                .SaveMatrixWindowPlacementAsync(
                    placement);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "保存桌面四象限窗口位置失败。",
                exception);
        }
    }

    private void ApplyPlacement(
        WindowPlacement placement)
    {
        _isApplyingPlacement = true;

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
            _isApplyingPlacement = false;
        }
    }

    private WindowPlacement CapturePlacement()
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
        if (unchecked((uint)message) ==
            _desktopWidgetHostService
                .TaskbarCreatedMessage)
        {
            /*
             * Explorer 重启后桌面窗口不会立即全部建立，
             * 稍等一段时间再重新绑定。
             */
            BackgroundTaskObserver.Observe(
                ReattachAfterExplorerRestartAsync(),
                "Explorer 重启后重新绑定桌面四象限失败。");
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

        /*
         * Explorer 重启后只重新建立桌面宿主关系。
         *
         * 不主动改变当前窗口 Z 顺序。
         */
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

    private async void OnWindowClosing(
    object? sender,
    CancelEventArgs e)
    {
        /*
         * LocalTodo 正式退出时，
         * 允许 Window 真正 Close。
         *
         * 特别注意：
         * 正式退出不能把用户的小组件偏好写成 false，
         * 因为下次启动还要恢复。
         */
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
                .HideMatrixWindowAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "隐藏桌面四象限前保存编辑内容失败。",
                exception);
        }
    }

    private void TitleBar_MouseLeftButtonDown(
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
        catch (InvalidOperationException)
        {
            // 鼠标状态改变时 DragMove 可能被取消。
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
