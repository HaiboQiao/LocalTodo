using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.ViewModels;

using WpfScrollBar =
    System.Windows.Controls.Primitives.ScrollBar;

namespace LocalTodo.Views;

public partial class CalendarView
{
    #region 快速新增

    private void
    OnQuickAddPopupPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        /*
         * QuickAddPopup 使用独立 HWND。
         *
         * PopupFocusHelper 会先恢复应用前台状态，
         * 再把原生和 WPF 键盘焦点交给
         * 用户实际点击的控件。
         *
         * 这个过程中宿主 Window 可能触发
         * Deactivated。
         *
         * 这并不是用户真正离开 LocalTodo，
         * 而只是主窗口和自己 Popup 之间的
         * 内部焦点切换。
         *
         * 因此临时设置保护标记，
         * 防止 OnHostWindowDeactivated()
         * 把空白新增窗口错误关闭。
         */
        _isRestoringQuickAddPopupNativeFocus =
            true;

        try
        {
            PopupFocusHelper
                .RestoreFocusForPointerInput(
                    QuickAddPopup,
                    e.OriginalSource
                        as DependencyObject);
        }
        finally
        {
            /*
             * 不在这里立即恢复 false。
             *
             * WPF 对 Window.Deactivated 的处理
             * 可能落在当前输入消息后续阶段。
             *
             * 等当前鼠标输入和焦点切换彻底结束以后
             * 再取消保护状态。
             */
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    () =>
                    {
                        _isRestoringQuickAddPopupNativeFocus =
                            false;
                    }));
        }
    }

    /// <summary>
    /// 当前新增任务窗口是否已经输入有效标题。
    /// </summary>
    private bool HasQuickAddTitle()
    {
        return DataContext
                is CalendarViewModel viewModel &&
            !string.IsNullOrWhiteSpace(
                viewModel.QuickAddTitle);
    }

    /// <summary>
    /// 点击日期格中的非任务区域时，
    /// 为该日期打开新增任务窗口。
    /// </summary>
    private async void OnCalendarDayCellMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Border dayCell ||
            dayCell.DataContext
                is not CalendarDayItem day ||
            DataContext
                is not CalendarViewModel
                    viewModel)
        {
            return;
        }

        /*
         * 当前 MouseDown 如果已经用于关闭旧 Popup，
         * 这一次 MouseUp 不能继续打开新增任务窗口。
         */
        if (_consumeCurrentPopupDismissClick)
        {
            _consumeCurrentPopupDismissClick =
                false;

            e.Handled =
                true;

            return;
        }

        DependencyObject? originalSource =
            e.OriginalSource
            as DependencyObject;

        /*
         * 点击任务按钮时打开任务详情；
         * 点击滚动条时只操作滚动条。
         * 两种情况都不能触发新增任务。
         */
        if (FindAncestor<Button>(
                originalSource)
                is not null ||
            FindAncestor<WpfScrollBar>(
                originalSource)
                is not null)
        {
            return;
        }

        e.Handled =
            true;

        if (QuickAddPopup.IsOpen)
        {
            CalendarTaskTitleTextBox.Focus();

            Keyboard.Focus(
                CalendarTaskTitleTextBox);

            return;
        }

        if (!await WaitForTaskDetailsCloseAsync())
        {
            return;
        }

        if (viewModel.EditingTask is not null)
        {
            bool saved =
                await viewModel
                    .FlushTaskEditorAutoSaveAsync();

            if (!saved)
            {
                ReopenTaskDetailsPopup();
                return;
            }

            viewModel.CloseTaskEditor();
        }

        CloseTaskDetailsPopupWithoutClosedHandling();

        viewModel.BeginQuickAdd(
            day.Date);

        QuickAddPopup.DataContext =
            viewModel;

        QuickAddPopup.PlacementTarget =
            dayCell;

        QuickAddPopup.IsOpen =
            true;

        _ = Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    CalendarTaskTitleTextBox
                        .Focus();

                    Keyboard.Focus(
                        CalendarTaskTitleTextBox);
                }));
    }

    private async void OnConfirmQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext
            is not CalendarViewModel
                viewModel)
        {
            return;
        }

        try
        {
            bool succeeded =
                await viewModel
                    .AddQuickTaskAsync();

            if (succeeded)
            {
                QuickAddPopup.IsOpen =
                    false;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "确认日历快速新增任务失败。",
                exception);
        }
    }

    /// <summary>
    /// 用户点击新增窗口右上角 × 时关闭。
    /// 点击窗口外部不会触发此方法。
    /// </summary>
    private void OnCloseQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        QuickAddPopup.IsOpen =
            false;
    }

    #endregion
    #region 任务详情

    /// <summary>
    /// 点击日历任务条，打开与四象限相同的任务详情编辑弹窗。
    /// </summary>
    private async void OnOpenTaskDetailsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag
                is not TaskItem task ||
            DataContext
                is not CalendarViewModel
                    viewModel)
        {
            return;
        }

        /*
         * 当前点击如果已经用于关闭原来的 Popup，
         * 就不能继续打开当前任务详情。
         */
        if (_consumeCurrentPopupDismissClick)
        {
            _consumeCurrentPopupDismissClick =
                false;

            e.Handled =
                true;

            return;
        }

        try
        {
            /*
             * 新增窗口打开期间，点击日历任务或其他内容
             * 都不能把它关闭。
             *
             * 为避免两个 Popup 同时叠加，
             * 此时也不再打开任务详情。
             */
            if (QuickAddPopup.IsOpen)
            {
                CalendarTaskTitleTextBox.Focus();

                Keyboard.Focus(
                    CalendarTaskTitleTextBox);

                return;
            }

            if (!await WaitForTaskDetailsCloseAsync())
            {
                return;
            }

            if (viewModel.EditingTask is not null)
            {
                bool saved =
                    await viewModel
                        .FlushTaskEditorAutoSaveAsync();

                if (!saved)
                {
                    ReopenTaskDetailsPopup();
                    return;
                }
            }

            CloseTaskDetailsPopupWithoutClosedHandling();

            viewModel.OpenTaskEditor(
                task);

            TaskDetailsPopup.DataContext =
                viewModel;

            TaskDetailsPopup.PlacementTarget =
                button;

            _ = Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        TaskDetailsPopup.IsOpen =
                            true;
                    }));
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "打开日历任务详情失败。",
                exception);
        }
    }

    private void OnCloseTaskDetailsClick(
        object sender,
        RoutedEventArgs e)
    {
        TaskDetailsPopup.IsOpen =
            false;
    }

    private async void OnTaskDetailsPopupClosed(
        object? sender,
        EventArgs e)
    {
        if (_ignoreNextTaskDetailsPopupClosed)
        {
            _ignoreNextTaskDetailsPopupClosed =
                false;

            return;
        }

        if (DataContext
            is not CalendarViewModel
                viewModel)
        {
            return;
        }

        Task<bool> closeTask =
            CompleteTaskDetailsCloseAsync(
                viewModel);

        _taskDetailsCloseTask =
            closeTask;

        try
        {
            await closeTask;
        }
        finally
        {
            if (ReferenceEquals(
                    _taskDetailsCloseTask,
                    closeTask))
            {
                _taskDetailsCloseTask =
                    null;
            }
        }
    }

    private async Task<bool>
        CompleteTaskDetailsCloseAsync(
            CalendarViewModel viewModel)
    {
        bool saved =
            await viewModel
                .FlushTaskEditorAutoSaveAsync();

        if (saved)
        {
            viewModel.CloseTaskEditor();
            return true;
        }

        /*
         * 标题为空或保存失败时不能静默丢失编辑内容，
         * 因此重新打开详情窗口。
         */
        ReopenTaskDetailsPopup();
        return false;
    }

    /// <summary>
    /// 等待正在进行中的任务详情关闭保存。
    ///
    /// 返回 false 表示旧任务没有成功保存，
    /// 当前点击不能继续切换到其他任务或新增任务。
    /// </summary>
    private async Task<bool>
        WaitForTaskDetailsCloseAsync()
    {
        Task<bool>? closeTask =
            _taskDetailsCloseTask;

        if (closeTask is null)
        {
            return true;
        }

        return await closeTask;
    }

    private async void OnDeleteTaskDetailsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext
            is not CalendarViewModel
                viewModel)
        {
            return;
        }

        bool deleted =
            await viewModel
                .DeleteEditingTaskAsync();

        if (!deleted)
        {
            return;
        }

        CloseTaskDetailsPopupWithoutClosedHandling();
    }

    private void ReopenTaskDetailsPopup()
    {
        _ = Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    if (DataContext
                        is CalendarViewModel
                            viewModel &&
                        viewModel.EditingTask
                            is not null)
                    {
                        TaskDetailsPopup.DataContext =
                            viewModel;

                        TaskDetailsPopup.IsOpen =
                            true;
                    }
                }));
    }

    /// <summary>
    /// 关闭 Popup 但忽略下一次 Closed 事件。
    /// 用于主动切换任务或删除成功后的清理。
    /// </summary>
    private void CloseTaskDetailsPopupWithoutClosedHandling()
    {
        if (!TaskDetailsPopup.IsOpen)
        {
            return;
        }

        _ignoreNextTaskDetailsPopupClosed =
            true;

        TaskDetailsPopup.IsOpen =
            false;
    }

    #endregion
}
