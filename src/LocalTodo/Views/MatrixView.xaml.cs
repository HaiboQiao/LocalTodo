using System;
using System.Collections.Generic;
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
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace LocalTodo.Views;

/// <summary>
/// 四象限页面的界面交互代码。
///
/// 这里仅处理：
/// 1. 单击任务打开详情；
/// 2. 拖动任务切换象限；
/// 3. 象限内快速新增；
/// 4. 任务详情 Popup 的自动关闭；
/// 5. Popup 关闭前提交等待中的自动保存。
///
/// 具体的数据保存和业务逻辑由 MatrixViewModel 负责。
/// </summary>
public partial class MatrixView :
    UserControl
{
    private static readonly WpfBrush
        DropTargetBorderBrush =
            new SolidColorBrush(
                WpfColor.FromRgb(
                    96,
                    165,
                    250));

    private static readonly WpfBrush
        DropTargetBackgroundBrush =
            new SolidColorBrush(
                WpfColor.FromRgb(
                    239,
                    246,
                    255));

    private WpfPoint
        _dragStartPoint;

    private TaskItem?
        _pendingTask;

    private ListBoxItem?
        _pendingContainer;

    private bool
        _isDragging;

    /*
     * OnTaskListPreviewMouseMove 是 async void。
     * 等待提交详情自动保存时，可能继续收到 MouseMove。
     * 该字段用于防止重复启动拖拽。
     */
    private bool
        _isPreparingDrag;

    private Border?
        _activeDropTarget;

    private WpfBrush?
        _activeDropTargetOriginalBorderBrush;

    private WpfBrush?
        _activeDropTargetOriginalBackground;

    /*
     * 程序主动关闭 Popup 时，
     * 不让 Closed 事件重复执行保存和清理。
     */
    private bool
        _ignoreNextTaskDetailsPopupClosed;

    /*
     * 用户点击 Popup 外部时，Closed 事件会异步保存。
     * 随后同一次点击可能又命中另一条任务。
     * 保存任务保存在这里，确保打开下一条任务前可以等待它完成。
     */
    private Task<bool>?
        _taskDetailsCloseTask;

    /*
     * MatrixView 既可能显示在主窗口中，
     * 也可能显示在独立桌面四象限窗口中。
     *
     * 监听当前宿主 Window 的 PreviewMouseDown，
     * 才能覆盖左侧导航、页面标题、顶部按钮等
     * MatrixView 之外但仍属于软件窗口的区域。
     */
    private Window?
        _hostWindow;

    public MatrixView()
    {
        InitializeComponent();
    }


    #region 象限拖放目标

    private void OnQuadrantDragOver(
        object sender,
        DragEventArgs e)
    {
        if (sender is not Border border ||
            border.Tag
                is not QuadrantType ||
            !e.Data.GetDataPresent(
                typeof(TaskItem)))
        {
            e.Effects =
                DragDropEffects.None;

            e.Handled =
                true;

            return;
        }

        e.Effects =
            DragDropEffects.Move;

        HighlightDropTarget(
            border);

        e.Handled =
            true;
    }

    private void OnQuadrantDragLeave(
        object sender,
        DragEventArgs e)
    {
        if (sender is Border border &&
            ReferenceEquals(
                border,
                _activeDropTarget))
        {
            ResetDropTarget();
        }

        e.Handled =
            true;
    }

    private async void OnQuadrantDrop(
        object sender,
        DragEventArgs e)
    {
        try
        {
            if (sender is not Border border ||
                border.Tag
                    is not QuadrantType
                        targetQuadrant ||
                !e.Data.GetDataPresent(
                    typeof(TaskItem)))
            {
                e.Effects =
                    DragDropEffects.None;

                return;
            }

            TaskItem? task =
                e.Data.GetData(
                    typeof(TaskItem))
                as TaskItem;

            if (task is null ||
                DataContext
                    is not MatrixViewModel
                        viewModel)
            {
                e.Effects =
                    DragDropEffects.None;

                return;
            }

            ResetDropTarget();

            await viewModel
                .MoveTaskToQuadrantAsync(
                    task,
                    targetQuadrant);

            e.Effects =
                DragDropEffects.Move;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "处理四象限拖放操作失败。",
                exception);

            e.Effects =
                DragDropEffects.None;
        }
        finally
        {
            ResetDropTarget();

            e.Handled =
                true;
        }
    }

    private void HighlightDropTarget(
        Border border)
    {
        if (ReferenceEquals(
                border,
                _activeDropTarget))
        {
            return;
        }

        ResetDropTarget();

        _activeDropTarget =
            border;

        _activeDropTargetOriginalBorderBrush =
            border.BorderBrush;

        _activeDropTargetOriginalBackground =
            border.Background;

        border.BorderBrush =
            DropTargetBorderBrush;

        border.Background =
            DropTargetBackgroundBrush;
    }

    private void ResetDropTarget()
    {
        if (_activeDropTarget
            is null)
        {
            return;
        }

        if (_activeDropTargetOriginalBorderBrush
            is not null)
        {
            _activeDropTarget.BorderBrush =
                _activeDropTargetOriginalBorderBrush;
        }
        else
        {
            _activeDropTarget
                .SetResourceReference(
                    Border.BorderBrushProperty,
                    "Brush.Border");
        }

        if (_activeDropTargetOriginalBackground
            is not null)
        {
            _activeDropTarget.Background =
                _activeDropTargetOriginalBackground;
        }
        else
        {
            _activeDropTarget
                .SetResourceReference(
                    Border.BackgroundProperty,
                    "Brush.CardBackground");
        }

        _activeDropTarget =
            null;

        _activeDropTargetOriginalBorderBrush =
            null;

        _activeDropTargetOriginalBackground =
            null;
    }

    #endregion

    #region 象限内快速新增

    /// <summary>
    /// 用户重新点击四象限新增任务 Popup 时，
    /// 恢复 Popup 独立 HWND 的键盘焦点。
    /// </summary>
    private void
        OnQuickAddPopupPreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        PopupFocusHelper
            .RestoreFocusForPointerInput(
                QuickAddPopup,
                e.OriginalSource
                    as DependencyObject);
    }

    /// <summary>
    /// 根据四象限新增任务标题是否有有效内容，
    /// 动态控制弹窗能否通过点击外部关闭。
    ///
    /// 标题为空或只有空格：
    /// 点击外部可以关闭。
    ///
    /// 标题有有效内容：
    /// 弹窗固定，只能通过右上角 ×
    /// 或“添加任务”按钮关闭。
    /// </summary>
    private void OnMatrixQuickAddTitleTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (sender
            is not TextBox titleTextBox)
        {
            return;
        }

        bool hasValidTitle =
            !string.IsNullOrWhiteSpace(
                titleTextBox.Text);

        QuickAddPopup.StaysOpen =
            hasValidTitle;
    }

    /// <summary>
    /// 新增任务弹窗关闭后恢复默认关闭模式。
    ///
    /// 下一次重新打开时，标题默认为空，
    /// 因而应允许点击外部关闭。
    /// </summary>
    private void OnQuickAddPopupClosed(
        object? sender,
        EventArgs e)
    {
        QuickAddPopup.StaysOpen =
            false;
    }

    /// <summary>
    /// 当前四象限新增窗口是否已经输入了有效标题。
    /// </summary>
    private bool HasQuickAddTitle()
    {
        return DataContext
                is MatrixViewModel viewModel &&
            !string.IsNullOrWhiteSpace(
                viewModel.NewMatrixTaskTitle);
    }

    /// <summary>
    /// 外部操作尝试关闭新增窗口。
    ///
    /// 返回 true：
    /// 弹窗原本没有打开，或者标题为空并已成功关闭，
    /// 调用方可以继续执行原来的点击或拖动操作。
    ///
    /// 返回 false：
    /// 标题已有内容，弹窗必须保持打开，
    /// 调用方应停止当前外部操作。
    /// </summary>
    private bool TryCloseQuickAddPopupFromExternalAction()
    {
        if (!QuickAddPopup.IsOpen)
        {
            return true;
        }

        if (HasQuickAddTitle())
        {
            /*
             * 已经存在标题：
             * 不允许外部操作关闭 Popup。
             *
             * 不再强制修改 WPF 焦点。
             * 用户重新点击 Popup 内部时，
             * 统一由 PopupFocusHelper 恢复原生焦点。
             */
            return false;
        }

        QuickAddPopup.StaysOpen =
            false;

        QuickAddPopup.IsOpen =
            false;

        return true;
    }

    private async void OnOpenQuickAddPopupClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button ||
                button.Tag
                    is not QuadrantType
                        quadrant ||
                DataContext
                    is not MatrixViewModel
                        viewModel)
            {
                return;
            }

            /*
             * 新增窗口已经打开并且标题已有内容时，
             * 点击其他象限的“＋添加”不能重置当前输入。
             *
             * 只把焦点重新放回标题输入框。
             */
            if (QuickAddPopup.IsOpen &&
                HasQuickAddTitle())
            {
                MatrixQuickAddTitleTextBox.Focus();

                Keyboard.Focus(
                    MatrixQuickAddTitleTextBox);

                return;
            }

            if (!await WaitForTaskDetailsCloseAsync())
            {
                return;
            }

            /*
             * 打开快速新增前，先保存并关闭任务详情。
             */
            if (viewModel.EditingTask
                is not null)
            {
                bool saved =
                    await viewModel
                        .FlushTaskEditorAutoSaveAsync();

                if (!saved)
                {
                    return;
                }

                CloseTaskDetailsPopupWithoutClosedHandling();

                viewModel.CloseTaskEditor();
            }

            viewModel.BeginQuickAdd(
                quadrant);

            QuickAddPopup.DataContext =
                viewModel;

            QuickAddPopup.PlacementTarget =
                button;

            QuickAddPopup.StaysOpen =
                false;

            QuickAddPopup.IsOpen =
                true;

            _ = Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        MatrixQuickAddTitleTextBox
                            .Focus();

                        Keyboard.Focus(
                            MatrixQuickAddTitleTextBox);
                    }));
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "打开四象限快速新增窗口失败。",
                exception);
        }
    }

    private async void OnConfirmQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext
            is not MatrixViewModel
                viewModel)
        {
            return;
        }

        bool succeeded =
            await viewModel
                .AddTaskToCurrentQuadrantAsync();

        if (succeeded)
        {
            /*
             * 成功创建任务属于允许关闭的方式之一。
             */
            QuickAddPopup.StaysOpen =
                false;

            QuickAddPopup.IsOpen =
                false;
        }
    }

    /// <summary>
    /// 点击新增任务窗口右上角 ×，
    /// 无论标题是否已有内容，都允许关闭。
    /// </summary>
    private void OnCloseQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        QuickAddPopup.StaysOpen =
            false;

        QuickAddPopup.IsOpen =
            false;
    }

    #endregion

    #region 任务详情弹窗

    /// <summary>
    /// MatrixView 加载后，监听它所在的整个窗口。
    ///
    /// 这样点击左侧导航、页面标题、顶部按钮，
    /// 或四象限区域本身时，都可以关闭任务详情。
    /// </summary>
    private void OnMatrixViewLoaded(
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

        DetachHostWindowMouseHandler();

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
    }

    /// <summary>
    /// 点击软件窗口内、任务详情 Popup 之外的区域时，
    /// 关闭任务详情。
    ///
    /// Popup 内部点击必须放行，否则标题、说明、
    /// 重点标记、截止日期、截止时间、提醒、象限和循环
    /// 都无法正常操作。
    /// </summary>
    private void OnHostWindowPreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!TaskDetailsPopup.IsOpen)
        {
            return;
        }

        DependencyObject? originalSource =
            e.OriginalSource
            as DependencyObject;

        if (TaskDetailsPopupRoot.IsMouseOver ||
            IsDescendantOf(
                originalSource,
                TaskDetailsPopupRoot))
        {
            return;
        }

        /*
         * 只关闭 Popup，不拦截本次点击。
         *
         * 例如用户点击另一条任务时：
         * 旧详情先关闭并保存，
         * 随后原点击继续打开新任务详情。
         */
        TaskDetailsPopup.IsOpen =
            false;
    }

    /// <summary>
    /// 取消宿主 Window 的鼠标监听。
    /// </summary>
    private void DetachHostWindowMouseHandler()
    {
        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.RemoveHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(
                OnHostWindowPreviewMouseDown));

        _hostWindow =
            null;
    }


    /// <summary>
    /// 点击顶部 × 时关闭 Popup。
    ///
    /// 真正的自动保存和编辑状态清理由 Closed 事件完成。
    /// </summary>
    private void OnCloseTaskDetailsClick(
        object sender,
        RoutedEventArgs e)
    {
        TaskDetailsPopup.IsOpen =
            false;
    }

    /// <summary>
    /// 点击 Popup 外部、点击顶部 ×，
    /// 或者 Popup 因其他原因自动关闭时触发。
    /// </summary>
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

        Task<bool> closeTask =
            CompleteTaskDetailsCloseAsync(
                reopenOnFailure: true);

        _taskDetailsCloseTask =
            closeTask;

        try
        {
            await closeTask;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "关闭四象限任务详情失败。",
                exception);
        }
        finally
        {
            ClearAllTaskListSelections();

            if (ReferenceEquals(
                    _taskDetailsCloseTask,
                    closeTask))
            {
                _taskDetailsCloseTask =
                    null;
            }
        }
    }

    /// <summary>
    /// 删除当前详情中的任务。
    /// </summary>
    private async void OnDeleteTaskDetailsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext
            is not MatrixViewModel
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

        /*
         * 删除方法已经清除了 EditingTask，
         * 这里只负责关闭 Popup，并跳过 Closed 重复处理。
         */
        CloseTaskDetailsPopupWithoutClosedHandling();
    }

    /// <summary>
    /// 完成 Popup 关闭前的最后一次自动保存。
    /// </summary>
    private async Task<bool>
        CompleteTaskDetailsCloseAsync(
            bool reopenOnFailure)
    {
        if (DataContext
                is not MatrixViewModel
                    viewModel ||
            viewModel.EditingTask
                is null)
        {
            return true;
        }

        bool saved =
            await viewModel
                .FlushTaskEditorAutoSaveAsync();

        if (!saved)
        {
            if (reopenOnFailure)
            {
                /*
                 * 标题为空或数据库保存失败时，
                 * 保留编辑缓冲并重新打开 Popup，
                 * 防止用户修改静默丢失。
                 */
                _ = Dispatcher.BeginInvoke(
                    new Action(
                        () =>
                        {
                            if (viewModel.HasEditingTask)
                            {
                                TaskDetailsPopup.IsOpen =
                                    true;
                            }
                        }));
            }

            return false;
        }

        viewModel.CloseTaskEditor();

        return true;
    }

    /// <summary>
    /// 等待正在进行的 Popup 关闭保存。
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

    /// <summary>
    /// 程序主动关闭 Popup 时，
    /// 不让 Closed 事件重复执行自动保存。
    /// </summary>
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

    /// <summary>
    /// 视图从可视树卸载时，提交最后一次等待保存，
    /// 例如切换页面或关闭桌面四象限窗口。
    /// </summary>
    private async void OnMatrixViewUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            QuickAddPopup.IsOpen =
                false;

            /*
             * 主动关闭 Popup，避免 Closed 事件和卸载流程
             * 同时重复执行保存。
             */
            CloseTaskDetailsPopupWithoutClosedHandling();

            if (!await WaitForTaskDetailsCloseAsync())
            {
                return;
            }

            if (DataContext
                    is MatrixViewModel
                        viewModel &&
                viewModel.EditingTask
                    is not null)
            {
                bool saved =
                    await viewModel
                        .FlushTaskEditorAutoSaveAsync();

                if (saved)
                {
                    viewModel.CloseTaskEditor();
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "卸载四象限视图时保存任务详情失败。",
                exception);
        }
        finally
        {
            DetachHostWindowMouseHandler();
            ClearAllTaskListSelections();
            ResetDropTarget();
            ClearPendingPointerAction();
        }
    }

    /// <summary>
    /// 清除四个象限所有 ListBox 的选择状态。
    ///
    /// 日期分组使用 ListBox 只是为了复用虚拟化和拖动，
    /// 业务上并不存在“长期选中任务”的概念。
    /// </summary>
    private void ClearAllTaskListSelections()
    {
        foreach (ListBox listBox
                 in FindVisualChildren<ListBox>(
                     this))
        {
            listBox.SelectedItem =
                null;

            listBox.UnselectAll();
        }
    }

    private void ClearPendingPointerAction()
    {
        _pendingTask =
            null;

        _pendingContainer =
            null;

        _isDragging =
            false;
    }


    /// <summary>
    /// 查找指定根元素下的全部某类型可视子元素。
    /// </summary>
    private static IEnumerable<T>
        FindVisualChildren<T>(
            DependencyObject root)
        where T : DependencyObject
    {
        int childCount =
            VisualTreeHelper.GetChildrenCount(
                root);

        for (int index = 0;
             index < childCount;
             index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(
                    root,
                    index);

            if (child is T result)
            {
                yield return result;
            }

            foreach (T descendant
                     in FindVisualChildren<T>(
                         child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// 判断输入源是否属于指定祖先元素的内部。
    ///
    /// 同时兼容 Popup 中的可视元素和逻辑元素，
    /// 例如 TextBoxView、ComboBoxItem、Run 等。
    /// </summary>
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
                        VisualTreeHelper.GetParent(
                            current),

                    _ =>
                        LogicalTreeHelper.GetParent(
                            current)
                };
        }

        return false;
    }

    /// <summary>
    /// 向上查找指定类型的可视或逻辑父元素。
    /// </summary>
    private static T? FindAncestor<T>(
        DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T result)
            {
                return result;
            }

            current =
                current switch
                {
                    Visual or Visual3D =>
                        VisualTreeHelper.GetParent(
                            current),

                    _ =>
                        LogicalTreeHelper.GetParent(
                            current)
                };
        }

        return null;
    }
}
