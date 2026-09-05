using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.ViewModels;

namespace LocalTodo.Views;

/// <summary>
/// 桌面任务列表的界面交互。
///
/// 业务数据和自动保存由 DesktopTaskListViewModel 负责；
/// 此处只处理 Popup、点击、焦点和完成框。
/// </summary>
public partial class DesktopTaskListView :
    UserControl
{
    private Window?
        _hostWindow;

    private Task<bool>?
        _taskDetailsCloseTask;

    private bool
        _ignoreNextTaskDetailsPopupClosed;

    /// <summary>
    /// 当前鼠标点击是否已经用于关闭旧 Popup。
    ///
    /// true 时，本次点击不能继续打开新的 Popup。
    /// </summary>
    private bool
        _consumeCurrentPopupDismissClick;

    /// <summary>
    /// 当前是否正在把焦点从外部程序恢复到
    /// 桌面新增任务 Popup 的独立 HWND。
    ///
    /// 恢复期间宿主窗口可能短暂触发 Deactivated，
    /// 这属于 LocalTodo 内部焦点切换，
    /// 不能关闭仍为空白的新增窗口。
    /// </summary>
    private bool
        _isRestoringQuickAddPopupFocus;

    public DesktopTaskListView()
    {
        InitializeComponent();
    }

    #region 生命周期

    private void OnDesktopTaskListViewLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Window? currentWindow =
            Window.GetWindow(this);

        if (ReferenceEquals(
                currentWindow,
                _hostWindow))
        {
            return;
        }

        DetachHostWindowHandlers();

        _hostWindow =
            currentWindow;

        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(
                OnHostWindowPreviewMouseDown),
            handledEventsToo: true);

        _hostWindow.Deactivated +=
            OnHostWindowDeactivated;
    }

    private async void
        OnDesktopTaskListViewUnloaded(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            await PrepareToHideAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "卸载桌面任务列表时保存详情失败。",
                exception);
        }
        finally
        {
            DetachHostWindowHandlers();
        }
    }

    /// <summary>
    /// 桌面窗口隐藏或退出前调用。
    /// </summary>
    public async Task<bool>
        PrepareToHideAsync()
    {
        _consumeCurrentPopupDismissClick =
            false;

        QuickAddPopup.IsOpen =
            false;

        /*
         * 主动关闭详情 Popup，
         * 避免 Closed 事件和这里重复保存。
         */
        CloseTaskDetailsPopupWithoutClosedHandling();

        if (DataContext
                is not DesktopTaskListViewModel
                    viewModel ||
            viewModel.EditingTask is null)
        {
            return true;
        }

        bool saved =
            await viewModel
                .FlushTaskEditorAutoSaveAsync();

        if (!saved)
        {
            ReopenTaskDetailsPopup();

            return false;
        }

        viewModel.CloseTaskEditor();

        return true;
    }

    private void DetachHostWindowHandlers()
    {
        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.RemoveHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(
                OnHostWindowPreviewMouseDown));

        _hostWindow.Deactivated -=
            OnHostWindowDeactivated;

        _hostWindow =
            null;
    }

    #endregion

    #region 宿主窗口点击

    /// <summary>
    /// Popup 打开时：
    ///
    /// 点击 Popup 内部不关闭；
    /// 点击 Popup 外部第一次只关闭 Popup；
    /// 第二次点击目标才真正执行目标操作。
    /// </summary>
    private void OnHostWindowPreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _consumeCurrentPopupDismissClick =
            false;

        DependencyObject? originalSource =
            e.OriginalSource
            as DependencyObject;

        /*
         * 任务详情。
         */
        if (TaskDetailsPopup.IsOpen)
        {
            bool isInsideTaskDetailsPopup =
                TaskDetailsPopupRoot.IsMouseOver ||
                IsDescendantOf(
                    originalSource,
                    TaskDetailsPopupRoot) ||
                IsPointerInsideElement(
                    TaskDetailsPopupRoot);

            if (isInsideTaskDetailsPopup)
            {
                return;
            }

            _consumeCurrentPopupDismissClick =
                true;

            TaskDetailsPopup.IsOpen =
                false;

            e.Handled =
                true;

            return;
        }

        /*
         * 新增任务。
         */
        if (!QuickAddPopup.IsOpen)
        {
            return;
        }

        bool isInsideQuickAddPopup =
            QuickAddPopupRoot.IsMouseOver ||
            IsDescendantOf(
                originalSource,
                QuickAddPopupRoot) ||
            IsPointerInsideElement(
                QuickAddPopupRoot);

        if (isInsideQuickAddPopup)
        {
            return;
        }

        /*
         * 新增窗口已有标题时，
         * 保留输入，不允许点击外部关闭。
         */
        if (HasQuickAddTitle())
        {
            /*
             * 已经输入标题：
             * Popup 必须保留。
             *
             * 本次宿主窗口点击只负责被消费，
             * 不再尝试通过 WPF Focus 强制聚焦标题框。
             *
             * 用户下一次点击 Popup 内部时，
             * PopupFocusHelper 会恢复 Popup HWND 焦点。
             */
            _consumeCurrentPopupDismissClick =
                true;

            e.Handled =
                true;

            return;
        }

        /*
         * 标题为空：
         * 第一次外部点击只负责关闭。
         */
        _consumeCurrentPopupDismissClick =
            true;

        QuickAddPopup.IsOpen =
            false;

        e.Handled =
            true;
    }

    private void OnHostWindowDeactivated(
        object? sender,
        EventArgs e)
    {
        if (_isRestoringQuickAddPopupFocus)
        {
            return;
        }

        /*
         * 新增标题有内容时继续保留。
         */
        if (QuickAddPopup.IsOpen &&
            !HasQuickAddTitle())
        {
            QuickAddPopup.IsOpen =
                false;
        }
    }

    #endregion

    #region 新增任务

    /// <summary>
    /// 桌面任务列表 Popup 重新被点击时，
    /// 恢复 Popup HWND 的真实键盘焦点。
    /// </summary>
    private void
        OnQuickAddPopupPreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        _isRestoringQuickAddPopupFocus =
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
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    () =>
                    {
                        _isRestoringQuickAddPopupFocus =
                            false;
                    }));
        }
    }

    private bool HasQuickAddTitle()
    {
        return DataContext
                is DesktopTaskListViewModel
                    viewModel &&
            !string.IsNullOrWhiteSpace(
                viewModel.NewTaskTitle);
    }

    private async void OnOpenQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_consumeCurrentPopupDismissClick)
        {
            _consumeCurrentPopupDismissClick =
                false;

            e.Handled =
                true;

            return;
        }

        if (DataContext
                is not DesktopTaskListViewModel
                    viewModel ||
            sender is not Button button)
        {
            return;
        }

        try
        {
            /*
             * 已经打开且已有输入时，
             * 再点“添加”只重新聚焦，不重置内容。
             */
            if (QuickAddPopup.IsOpen &&
                HasQuickAddTitle())
            {
                QuickAddTitleTextBox.Focus();

                Keyboard.Focus(
                    QuickAddTitleTextBox);

                return;
            }

            if (!await
                WaitForTaskDetailsCloseAsync())
            {
                return;
            }

            if (viewModel.EditingTask
                is not null)
            {
                bool saved =
                    await viewModel
                        .FlushTaskEditorAutoSaveAsync();

                if (!saved)
                {
                    ReopenTaskDetailsPopup();

                    return;
                }

                CloseTaskDetailsPopupWithoutClosedHandling();

                viewModel.CloseTaskEditor();
            }

            viewModel.BeginQuickAdd();

            QuickAddPopup.DataContext =
                viewModel;

            QuickAddPopup.PlacementTarget =
                button;

            QuickAddPopup.IsOpen =
                true;

            _ = Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        QuickAddTitleTextBox
                            .Focus();

                        Keyboard.Focus(
                            QuickAddTitleTextBox);
                    }));
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "打开桌面任务列表新增窗口失败。",
                exception);
        }
    }

    private async void OnConfirmQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext
            is not DesktopTaskListViewModel
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
                "确认桌面任务列表新增任务失败。",
                exception);
        }
    }

    private void OnCloseQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        QuickAddPopup.IsOpen =
            false;
    }

    #endregion

    #region 打开任务详情

    private async void OnOpenTaskDetailsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_consumeCurrentPopupDismissClick)
        {
            _consumeCurrentPopupDismissClick =
                false;

            e.Handled =
                true;

            return;
        }

        if (sender is not Button button ||
            button.Tag
                is not TaskItem task ||
            DataContext
                is not DesktopTaskListViewModel
                    viewModel)
        {
            return;
        }

        try
        {
            /*
             * 新增窗口存在时：
             * 有标题则保护输入；
             * 空标题则第一次点击只关闭新增窗口。
             */
            if (QuickAddPopup.IsOpen)
            {
                if (HasQuickAddTitle())
                {
                    QuickAddTitleTextBox.Focus();

                    Keyboard.Focus(
                        QuickAddTitleTextBox);

                    return;
                }

                QuickAddPopup.IsOpen =
                    false;

                return;
            }

            if (!await
                WaitForTaskDetailsCloseAsync())
            {
                return;
            }

            if (viewModel.EditingTask
                is not null)
            {
                bool saved =
                    await viewModel
                        .FlushTaskEditorAutoSaveAsync();

                if (!saved)
                {
                    ReopenTaskDetailsPopup();

                    return;
                }

                CloseTaskDetailsPopupWithoutClosedHandling();

                viewModel.CloseTaskEditor();
            }

            viewModel.OpenTaskEditor(
                task);

            TaskDetailsPopup.DataContext =
                viewModel;

            TaskDetailsPopup.PlacementTarget =
                button;

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(
                    () =>
                    {
                        if (viewModel.HasEditingTask)
                        {
                            TaskDetailsPopup.IsOpen =
                                true;
                        }
                    }));

            e.Handled =
                true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "打开桌面任务详情失败。",
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

    private async void
        OnTaskDetailsPopupClosed(
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
            is not DesktopTaskListViewModel
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
        catch (Exception exception)
        {
            AppLog.Error(
                "关闭桌面任务详情失败。",
                exception);
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
            DesktopTaskListViewModel
                viewModel)
    {
        bool saved =
            await viewModel
                .FlushTaskEditorAutoSaveAsync();

        if (saved)
        {
            viewModel.CloseTaskEditor();

            return true;
        }

        ReopenTaskDetailsPopup();

        return false;
    }

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

    private void ReopenTaskDetailsPopup()
    {
        _ = Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    if (DataContext
                            is DesktopTaskListViewModel
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

    private void
        CloseTaskDetailsPopupWithoutClosedHandling()
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

    #region 删除和完成

    private async void
        OnDeleteTaskDetailsClick(
            object sender,
            RoutedEventArgs e)
    {
        if (DataContext
            is not DesktopTaskListViewModel
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

    private async void
        OnTaskCompletionClick(
            object sender,
            RoutedEventArgs e)
    {
        if (_consumeCurrentPopupDismissClick)
        {
            _consumeCurrentPopupDismissClick =
                false;

            e.Handled =
                true;

            return;
        }

        if (sender is not CheckBox checkBox ||
            checkBox.Tag
                is not TaskItem task ||
            DataContext
                is not DesktopTaskListViewModel
                    viewModel)
        {
            return;
        }

        checkBox.IsEnabled =
            false;

        try
        {
            await viewModel
                .ToggleTaskCompletionAsync(
                    task);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "桌面任务列表完成任务失败。",
                exception);
        }
        finally
        {
            checkBox.IsEnabled =
                true;

            /*
             * 如果任务因为保存失败仍留在列表，
             * 让完成框重新反映模型真实状态。
             */
            checkBox.IsChecked =
                task.IsCompleted;
        }
    }

    #endregion

    #region Popup 范围判断

    private static bool
        IsPointerInsideElement(
            FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(
            element);

        if (!element.IsVisible ||
            element.ActualWidth <= 0 ||
            element.ActualHeight <= 0)
        {
            return false;
        }

        /*
         * 明确使用 WPF Point，
         * 避免与 System.Drawing.Point 冲突。
         */
        System.Windows.Point
            mousePosition =
                Mouse.GetPosition(
                    element);

        System.Windows.Rect
            bounds =
                new(
                    0,
                    0,
                    element.ActualWidth,
                    element.ActualHeight);

        return bounds.Contains(
            mousePosition);
    }

    private static bool IsDescendantOf(
        DependencyObject? current,
        DependencyObject ancestor)
    {
        while (current is not null)
        {
            if (ReferenceEquals(
                    current,
                    ancestor))
            {
                return true;
            }

            current =
                current switch
                {
                    Visual or Visual3D =>
                        VisualTreeHelper
                            .GetParent(
                                current),

                    _ =>
                        LogicalTreeHelper
                            .GetParent(
                                current)
                };
        }

        return false;
    }

    #endregion
}
