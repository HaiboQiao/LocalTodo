using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;
using LocalTodo.ViewModels;

namespace LocalTodo.Views;

public partial class MainWindow :
    Window
{
    private readonly ApplicationWindowService
        _applicationWindowService;

    private readonly WindowPlacementService
        _windowPlacementService;

    private readonly DispatcherTimer
        _placementSaveTimer;

    private bool
        _placementLoaded;

    private bool
        _isApplyingPlacement;

    public MainWindow(
        MainWindowViewModel viewModel,
        ApplicationWindowService applicationWindowService,
        WindowPlacementService windowPlacementService)
    {
        InitializeComponent();

        DataContext =
            viewModel;

        _applicationWindowService =
            applicationWindowService;

        _windowPlacementService =
            windowPlacementService;

        _placementSaveTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        500)
            };

        _placementSaveTimer.Tick +=
            OnPlacementSaveTimerTick;

        LocationChanged +=
            OnWindowPlacementChanged;

        SizeChanged +=
            OnWindowPlacementChanged;

        StateChanged +=
            OnWindowPlacementChanged;

        Closing +=
            OnMainWindowClosing;
    }

    /// <summary>
    /// 第一次显示主窗口前，恢复上次保存的位置和尺寸。
    /// </summary>
    public async Task PrepareToShowAsync()
    {
        if (_placementLoaded)
        {
            return;
        }

        WindowPlacement placement =
            await _windowPlacementService
                .LoadMainWindowPlacementAsync();

        ApplyPlacement(
            placement);

        _placementLoaded =
            true;
    }

    /// <summary>
    /// 立即保存当前主窗口的位置和尺寸。
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
                .SaveMainWindowPlacementAsync(
                    placement);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "保存主窗口位置和尺寸失败。",
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

        // 用户拖动或调整窗口大小时，
        // 重新开始 500 毫秒倒计时。
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

    private async void OnMainWindowClosing(
        object? sender,
        CancelEventArgs e)
    {
        if (_applicationWindowService
            .IsExitRequested)
        {
            return;
        }

        // 点击右上角关闭时，程序不退出，
        // 先保存位置，再隐藏到托盘。
        e.Cancel =
            true;

        try
        {
            bool hidden =
                await _applicationWindowService
                    .HideMainWindowAsync();

            if (!hidden)
            {
                return;
            }

        }
        catch (Exception exception)
        {
            AppLog.Error(
                "隐藏主窗口前保存编辑内容失败。",
                exception);
        }
    }

}
