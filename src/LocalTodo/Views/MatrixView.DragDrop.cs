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
using WpfBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;

namespace LocalTodo.Views;

public partial class MatrixView
{
    #region 任务单击和拖动

    /// <summary>
    /// 记录鼠标按下位置以及可能被单击或拖动的任务。
    /// </summary>
    private void OnTaskListPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ClearPendingPointerAction();

        _dragStartPoint =
            e.GetPosition(this);

        if (sender is not ListBox listBox ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        /*
         * 点击完成复选框时，只执行完成操作。
         * 操作滚动条时，也不能打开详情或启动拖动。
         */
        if (FindAncestor<CheckBox>(
                source) is not null ||
            FindAncestor<ScrollBar>(
                source) is not null)
        {
            return;
        }

        ListBoxItem? item =
            ItemsControl.ContainerFromElement(
                listBox,
                source)
            as ListBoxItem;

        TaskItem? task =
            item?.DataContext switch
            {
                /*
                 * 日期分组后的任务行使用 TaskListEntry 包装。
                 */
                TaskListEntry entry =>
                    entry.Task,

                /*
                 * 保留直接 TaskItem 的兼容分支，
                 * 方便以后临时切回未分组列表。
                 */
                TaskItem directTask =>
                    directTask,

                _ =>
                    null
            };

        if (task is null)
        {
            return;
        }

        _pendingTask =
            task;

        _pendingContainer =
            item;
    }

    /// <summary>
    /// 未发生拖动时，普通单击任务打开详情。
    ///
    /// 如果上一个详情弹窗刚因“点击外部”而关闭，
    /// 会先等待它的自动保存结束，再打开当前任务。
    /// </summary>
    private async void OnTaskListPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        try
        {
            if (_isDragging ||
                _isPreparingDrag ||
                _pendingTask is null ||
                DataContext
                    is not MatrixViewModel
                        viewModel)
            {
                return;
            }

            TaskItem taskToOpen =
                _pendingTask;

            UIElement? placementTarget =
                _pendingContainer ??
                sender as UIElement;

            /*
             * 点击其他任务时，原 Popup 可能已经因为
             * StaysOpen="False" 自动关闭并进入保存流程。
             */
            if (!await WaitForTaskDetailsCloseAsync())
            {
                e.Handled =
                    true;

                return;
            }

            /*新增窗口标题为空时允许关闭并继续打开任务详情；
             *标题已有内容时，新增窗口保持打开，
             *本次点击任务不再继续执行。
             */
            if (!TryCloseQuickAddPopupFromExternalAction())
            {
                e.Handled =
                    true;

                return;
            }

            /*
             * Popup 仍然打开，或者编辑状态仍然存在时，
             * 先立即保存上一条任务，再切换。
             */
            if (viewModel.EditingTask
                is not null)
            {
                bool saved =
                    await viewModel
                        .FlushTaskEditorAutoSaveAsync();

                if (!saved)
                {
                    e.Handled =
                        true;

                    return;
                }

                CloseTaskDetailsPopupWithoutClosedHandling();

                viewModel.CloseTaskEditor();
            }

            viewModel.OpenTaskEditor(
                taskToOpen);

            TaskDetailsPopup.DataContext =
                viewModel;

            TaskDetailsPopup.PlacementTarget =
                placementTarget;

            /*
             * 不在当前 PreviewMouseLeftButtonUp 中立即打开 Popup。
             *
             * 等当前鼠标事件彻底结束后再打开，
             * 让 StaysOpen=False 能正确建立外部点击捕获。
             */
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(
                    () =>
                    {
                        if (!viewModel.HasEditingTask)
                        {
                            return;
                        }

                        TaskDetailsPopup.IsOpen =
                            true;
                    }));

            e.Handled =
                true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "打开四象限任务详情失败。",
                exception);
        }
        finally
        {
            if (sender is ListBox listBox)
            {
                listBox.SelectedItem =
                    null;
            }

            if (_pendingContainer
                is not null)
            {
                _pendingContainer.IsSelected =
                    false;
            }

            ClearPendingPointerAction();
        }
    }

    /// <summary>
    /// 鼠标移动超过系统拖动阈值后，正式开始拖放。
    ///
    /// 开始拖动前会先提交任务详情中等待保存的修改，
    /// 避免关闭详情时丢失最后一次输入。
    /// </summary>
    private async void OnTaskListPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_isPreparingDrag ||
            e.LeftButton !=
                MouseButtonState.Pressed ||
            _pendingTask is null)
        {
            return;
        }

        WpfPoint currentPoint =
            e.GetPosition(this);

        double horizontalDistance =
            Math.Abs(
                currentPoint.X -
                _dragStartPoint.X);

        double verticalDistance =
            Math.Abs(
                currentPoint.Y -
                _dragStartPoint.Y);

        if (horizontalDistance <
                SystemParameters
                    .MinimumHorizontalDragDistance &&
            verticalDistance <
                SystemParameters
                    .MinimumVerticalDragDistance)
        {
            return;
        }

        _isPreparingDrag =
            true;

        try
        {
            if (DataContext
                is MatrixViewModel viewModel)
            {
                /*
                 * 等待因点击外部触发的关闭保存。
                 */
                if (!await WaitForTaskDetailsCloseAsync())
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
                        return;
                    }

                    CloseTaskDetailsPopupWithoutClosedHandling();

                    viewModel.CloseTaskEditor();
                }
            }

            /*
             * await 期间用户可能已经松开鼠标，
             * 或待拖动对象已经被其他事件清理。
             */
            if (e.LeftButton !=
                    MouseButtonState.Pressed ||
                _pendingTask is null)
            {
                return;
            }

            TaskItem task =
                _pendingTask;

            ListBoxItem? sourceContainer =
                _pendingContainer;

            /*
             * 标题已有内容时不允许通过拖动任务
             * 间接关闭新增窗口，同时停止本次拖动。
             */
            if (!TryCloseQuickAddPopupFromExternalAction())
            {
                return;
            }

            _isDragging =
                true;

            DataObject dragData =
                new(
                    typeof(TaskItem),
                    task);

            try
            {
                if (sourceContainer
                    is not null)
                {
                    sourceContainer.Opacity =
                        0.48;
                }

                DependencyObject dragSource =
                    sourceContainer ??
                    (DependencyObject)sender;

                DragDrop.DoDragDrop(
                    dragSource,
                    dragData,
                    DragDropEffects.Move);
            }
            finally
            {
                if (sourceContainer
                    is not null)
                {
                    sourceContainer.Opacity =
                        1.0;
                }

                ResetDropTarget();
                ClearPendingPointerAction();

                Mouse.SetCursor(
                    Cursors.Arrow);
            }

            e.Handled =
                true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "启动四象限任务拖动失败。",
                exception);
        }
        finally
        {
            _isPreparingDrag =
                false;
        }
    }

    /// <summary>
    /// 拖动过程中继续使用普通箭头光标。
    /// </summary>
    private void OnTaskListGiveFeedback(
        object sender,
        GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors =
            false;

        Mouse.SetCursor(
            Cursors.Arrow);

        e.Handled =
            true;
    }

    #endregion
}
