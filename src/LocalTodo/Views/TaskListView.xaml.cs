using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.ViewModels;

namespace LocalTodo.Views;

/// <summary>
/// “所有任务 / 已完成”页面的界面交互。
///
/// 当前 code-behind 仅负责“所有任务”页面
/// 快速新增 Popup 的打开、关闭和焦点。
///
/// 任务详情自动保存等业务仍然全部由
/// TaskListViewModel 负责。
/// </summary>
public partial class TaskListView :
    UserControl
{
    public TaskListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 当前新增窗口是否已经输入有效标题。
    /// </summary>
    private bool HasQuickAddTitle()
    {
        return DataContext
                is TaskListViewModel viewModel &&
            !string.IsNullOrWhiteSpace(
                viewModel.NewTaskTitle);
    }

    /// <summary>
    /// 用户重新点击新增任务 Popup 内部时，
    /// 先恢复 Popup 自身 HWND 的原生键盘焦点。
    ///
    /// 之后当前鼠标事件继续传播，
    /// 真正被点击的 TextBox / ComboBox / DatePicker
    /// 会正常获得焦点。
    /// </summary>
    private void
        OnTaskListQuickAddPopupPreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        PopupFocusHelper
            .RestoreFocusForPointerInput(
                TaskListQuickAddPopup,
                e.OriginalSource
                    as DependencyObject);
    }

    /// <summary>
    /// 根据标题是否已有内容，
    /// 决定 Popup 能否因为外部点击而关闭。
    ///
    /// 标题为空：
    /// 可以点击外部关闭。
    ///
    /// 标题已有内容：
    /// 防止误点导致输入内容丢失。
    /// </summary>
    private void
        OnTaskListQuickAddTitleTextChanged(
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

        TaskListQuickAddPopup.StaysOpen =
            hasValidTitle;
    }

    /// <summary>
    /// Popup 关闭以后恢复默认模式。
    /// </summary>
    private void OnTaskListQuickAddPopupClosed(
        object? sender,
        EventArgs e)
    {
        TaskListQuickAddPopup.StaysOpen =
            false;
    }

    /// <summary>
    /// 点击任务列表右上角“＋ 添加”。
    /// </summary>
    private void OnOpenTaskListQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (sender
                    is not Button button ||
                DataContext
                    is not TaskListViewModel
                        viewModel ||
                !viewModel.CanAddTask)
            {
                return;
            }

            /*
             * Popup 已经打开并已有标题时，
             * 再点击“＋ 添加”不要重置现有输入。
             */
            if (TaskListQuickAddPopup.IsOpen &&
                HasQuickAddTitle())
            {
                TaskListQuickAddTitleTextBox
                    .Focus();

                Keyboard.Focus(
                    TaskListQuickAddTitleTextBox);

                return;
            }

            viewModel.BeginQuickAdd();

            TaskListQuickAddPopup.DataContext =
                viewModel;

            TaskListQuickAddPopup.PlacementTarget =
                button;

            TaskListQuickAddPopup.StaysOpen =
                false;

            TaskListQuickAddPopup.IsOpen =
                true;

            _ = Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        TaskListQuickAddTitleTextBox
                            .Focus();

                        Keyboard.Focus(
                            TaskListQuickAddTitleTextBox);
                    }));
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "打开所有任务快速新增窗口失败。",
                exception);
        }
    }

    /// <summary>
    /// 点击“添加任务”。
    /// </summary>
    private async void
        OnConfirmTaskListQuickAddClick(
            object sender,
            RoutedEventArgs e)
    {
        if (DataContext
            is not TaskListViewModel
                viewModel)
        {
            return;
        }

        try
        {
            bool succeeded =
                await viewModel
                    .AddQuickTaskAsync();

            if (!succeeded)
            {
                return;
            }

            /*
             * 成功创建是允许关闭 Popup 的操作。
             */
            TaskListQuickAddPopup.StaysOpen =
                false;

            TaskListQuickAddPopup.IsOpen =
                false;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "所有任务快速新增失败。",
                exception);
        }
    }

    /// <summary>
    /// 点击新增窗口右上角 ×。
    ///
    /// 无论当前是否已经输入标题，
    /// 用户主动点击 × 都允许关闭。
    /// </summary>
    private void OnCloseTaskListQuickAddClick(
        object sender,
        RoutedEventArgs e)
    {
        TaskListQuickAddPopup.StaysOpen =
            false;

        TaskListQuickAddPopup.IsOpen =
            false;
    }
}
