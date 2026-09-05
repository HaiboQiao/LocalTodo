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

using WpfScrollBar =
    System.Windows.Controls.Primitives.ScrollBar;

namespace LocalTodo.Views;

/// <summary>
/// CalendarView.xaml 的界面交互代码。
///
/// 业务数据和自动保存由 CalendarViewModel 负责；
/// 此文件处理两个 Popup 的打开、关闭、定位和宿主窗口点击。
/// </summary>
public partial class CalendarView :
    UserControl
{
    private Window?
        _hostWindow;

    private Task<bool>?
        _taskDetailsCloseTask;

    private bool
        _ignoreNextTaskDetailsPopupClosed;

    /// <summary>
    /// 当前鼠标点击是否已经被用于关闭 Popup。
    ///
    /// true 表示这一次点击只能关闭原 Popup，
    /// 不能继续打开其他任务详情或新增任务窗口。
    /// </summary>
    private bool
        _consumeCurrentPopupDismissClick;

    /// <summary>
    /// 当前是否正在主动恢复快速新增 Popup
    /// 自身 HWND 的原生键盘焦点。
    ///
    /// PopupFocusHelper 内部调用 SetFocus 时，
    /// 宿主 Window 可能短暂触发 Deactivated。
    ///
    /// 这种失活属于 LocalTodo 内部焦点切换，
    /// 不能被识别成“用户离开程序”，
    /// 否则空白新增窗口会被错误关闭。
    /// </summary>
    private bool
        _isRestoringQuickAddPopupNativeFocus;

    /*
     * WPF 标准鼠标滚轮每一格通常是 120。
     * 使用累积值可以兼容部分高精度滚轮设备
     * 一次只发送较小 Delta 的情况。
     */
    private const int MouseWheelDeltaPerNotch =
        120;

    private int
        _calendarMouseWheelDelta;

    private const string CalendarDayTaskScrollerTag =
        "CalendarDayTaskScroller";

    private const double CalendarDayTaskScrollStep =
        34.0;

    public CalendarView()
    {
        InitializeComponent();
    }

    #region 页面生命周期与宿主窗口点击

    /// <summary>
    /// 在日历区域滚动鼠标滚轮切换月份。
    ///
    /// 向上滚动：上个月。
    /// 向下滚动：下个月。
    ///
    /// 新增任务或任务详情弹窗打开期间，
    /// 不切换月份，避免正在输入的内容被当前月份变化影响。
    /// </summary>
    private void OnCalendarPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        /*
 * Popup 打开时需要区分两种滚轮操作：
 *
 * 1. 鼠标位于 Popup 内部：
 *    必须把滚轮事件放行。
 *
 *    例如任务说明 TextBox 自己拥有内部 ScrollViewer，
 *    文字过多时应该由它正常处理鼠标滚轮。
 *
 * 2. Popup 已打开，但鼠标位于底层日历：
 *    仍然阻止日历切换月份，
 *    避免编辑任务时误操作月份。
 */
        DependencyObject? popupEventSource =
            e.OriginalSource
            as DependencyObject;


        bool isInsideQuickAddPopup =
            QuickAddPopup.IsOpen &&
            (
                QuickAddPopupRoot.IsMouseOver ||
                IsDescendantOf(
                    popupEventSource,
                    QuickAddPopupRoot) ||
                IsPointerInsideElement(
                    QuickAddPopupRoot)
            );


        bool isInsideTaskDetailsPopup =
            TaskDetailsPopup.IsOpen &&
            (
                TaskDetailsPopupRoot.IsMouseOver ||
                IsDescendantOf(
                    popupEventSource,
                    TaskDetailsPopupRoot) ||
                IsPointerInsideElement(
                    TaskDetailsPopupRoot)
            );


        /*
         * 鼠标确实在 Popup 内部：
         *
         * 不设置 e.Handled，
         * 直接退出 Calendar 自己的月份滚动逻辑。
         *
         * 这样滚轮事件会继续交给 TextBox /
         * ScrollViewer 等 Popup 内部控件处理。
         */
        if (isInsideQuickAddPopup ||
            isInsideTaskDetailsPopup)
        {
            return;
        }


        /*
         * Popup 已经打开，但滚轮发生在 Popup 外部：
         *
         * 仍然消费滚轮事件，
         * 防止底层日历切换月份。
         */
        if (QuickAddPopup.IsOpen ||
            TaskDetailsPopup.IsOpen)
        {
            e.Handled =
                true;

            return;
        }

        if (DataContext
            is not CalendarViewModel viewModel)
        {
            return;
        }

        /*
         * 鼠标位于某个日期格的任务滚动区时，
         * 优先滚动该日期格，而不是切换月份。
         */
        DependencyObject? originalSource =
            e.OriginalSource
            as DependencyObject;

        ScrollViewer? dayTaskScroller =
            FindAncestor<ScrollViewer>(
                originalSource);

        if (dayTaskScroller is not null &&
            string.Equals(
                dayTaskScroller.Tag as string,
                CalendarDayTaskScrollerTag,
                StringComparison.Ordinal) &&
            dayTaskScroller.ScrollableHeight > 0)
        {
            double wheelNotches =
                e.Delta /
                (double)MouseWheelDeltaPerNotch;

            double targetOffset =
                dayTaskScroller.VerticalOffset -
                wheelNotches *
                CalendarDayTaskScrollStep;

            dayTaskScroller.ScrollToVerticalOffset(
                Math.Clamp(
                    targetOffset,
                    0,
                    dayTaskScroller.ScrollableHeight));

            /*
             * 即使已经到达顶部或底部，
             * 也不让这次滚轮操作意外切换月份。
             */
            e.Handled =
                true;

            return;
        }

        _calendarMouseWheelDelta +=
            e.Delta;

        /*
         * 高精度鼠标或触控板可能一次只发送
         * 小于 120 的 Delta，因此先累积到一格。
         */
        if (Math.Abs(
                _calendarMouseWheelDelta) <
            MouseWheelDeltaPerNotch)
        {
            e.Handled =
                true;

            return;
        }

        bool moveToPreviousMonth =
            _calendarMouseWheelDelta > 0;

        _calendarMouseWheelDelta =
            0;

        if (moveToPreviousMonth)
        {
            if (viewModel
                .PreviousMonthCommand
                .CanExecute(null))
            {
                viewModel
                    .PreviousMonthCommand
                    .Execute(null);
            }
        }
        else
        {
            if (viewModel
                .NextMonthCommand
                .CanExecute(null))
            {
                viewModel
                    .NextMonthCommand
                    .Execute(null);
            }
        }

        /*
         * 阻止滚轮事件继续传递给外层控件，
         * 避免切换月份的同时滚动整个主窗口。
         */
        e.Handled =
            true;
    }

    private void OnCalendarViewLoaded(
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

        _hostWindow.Deactivated +=
            OnHostWindowDeactivated;
    }

    private async void OnCalendarViewUnloaded(
    object sender,
    RoutedEventArgs e)
    {
        _calendarMouseWheelDelta =
            0;

        _consumeCurrentPopupDismissClick =
            false;

        DetachHostWindowMouseHandler();

        QuickAddPopup.IsOpen =
            false;

        if (DataContext
            is CalendarViewModel viewModel)
        {
            bool saved =
                await viewModel
                    .FlushTaskEditorAutoSaveAsync();

            if (saved)
            {
                viewModel.CloseTaskEditor();
            }
        }

        CloseTaskDetailsPopupWithoutClosedHandling();
    }

    /// <summary>
    /// 处理宿主窗口中的外部点击。
    ///
    /// 当任务详情或空白新增窗口已经打开时，
    /// 第一次点击其他位置只负责关闭当前 Popup，
    /// 不继续执行被点击位置原本的操作。
    /// </summary>
    private void OnHostWindowPreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        /*
         * 如果上一次点击关闭 Popup 后没有命中
         * 日期格或任务按钮，
         * 那个消费标记可能还没有被对应处理器取走。
         *
         * 现在已经开始了一次新的 MouseDown，
         * 因此旧标记一定已经失效。
         */
        _consumeCurrentPopupDismissClick =
            false;

        DependencyObject? originalSource =
            e.OriginalSource
            as DependencyObject;

        /*
 * 第一优先级：任务详情 Popup。
 */
        if (TaskDetailsPopup.IsOpen)
        {
            /*
             * Popup 使用独立窗口承载。
             *
             * 因此这里使用三种方式共同判断：
             *
             * 1. WPF 当前是否认为鼠标位于 PopupRoot 上；
             * 2. 当前事件源是否属于 PopupRoot；
             * 3. 鼠标实际坐标是否位于 PopupRoot 的矩形范围内。
             *
             * 任意一种判断为 true，
             * 都说明这是 Popup 内部点击。
             */
            bool isInsideTaskDetailsPopup =
                TaskDetailsPopupRoot.IsMouseOver ||
                IsDescendantOf(
                    originalSource,
                    TaskDetailsPopupRoot) ||
                IsPointerInsideElement(
                    TaskDetailsPopupRoot);

            if (isInsideTaskDetailsPopup)
            {
                /*
                 * Popup 内部点击：
                 *
                 * 什么都不要做，
                 * 也绝对不能设置 e.Handled。
                 *
                 * 这样 TextBox、ComboBox、DatePicker、
                 * CheckBox 和按钮仍然能够正常接收点击。
                 */
                return;
            }

            /*
             * 只有确认点击发生在任务详情 Popup 外部以后，
             * 才消费本次点击并关闭 Popup。
             */
            _consumeCurrentPopupDismissClick =
                true;

            TaskDetailsPopup.IsOpen =
                false;

            e.Handled =
                true;

            return;
        }

        /*
         * 第二优先级：快速新增 Popup。
         *
         * Popup 本身是独立窗口，
         * 因此事件能够进入宿主 Window 时，
         * 就说明用户点击的是 Popup 外部。
         */
        if (!QuickAddPopup.IsOpen)
        {
            return;
        }

        /*
         * 点击快速新增 Popup 自身内部时，
         * 绝不能被当作外部点击。
         *
         * 这里同样使用三层判断，
         * 避免 Popup 独立窗口导致事件源判断不准确。
         */
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
         * 已经输入有效标题：
         * 保留你当前的数据保护行为。
         *
         * 不关闭 Popup，
         * 用户仍然需要点击 × 或“添加任务”关闭。
         */
        if (HasQuickAddTitle())
        {
            /*
             * 标题已有内容：
             *
             * 保留 Popup，
             * 同时消费这一次宿主窗口点击，
             * 防止底层控件继续执行操作。
             *
             * 不再强制把 WPF 焦点塞回标题框。
             * 用户下一次点击 Popup 内任意控件时，
             * OnQuickAddPopupPreviewMouseLeftButtonDown
             * 会恢复 Popup HWND 的真实焦点。
             */
            e.Handled =
                true;

            return;
        }

        /*
         * 标题为空：
         *
         * 允许外部点击关闭新增窗口，
         * 但本次点击只用于关闭，
         * 不允许继续打开另外一个 Popup。
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
        if (!QuickAddPopup.IsOpen)
        {
            return;
        }

        /*
         * 如果这一次宿主窗口失活，
         * 是 QuickAddPopup 自己恢复原生 HWND 焦点
         * 导致的内部焦点切换，
         * 绝对不能关闭新增窗口。
         *
         * 否则用户第一次点击标题框、说明框、
         * CheckBox、DatePicker、ComboBox 等控件时，
         * Popup 就会立即消失。
         */
        if (_isRestoringQuickAddPopupNativeFocus)
        {
            return;
        }

        /*
         * 已经输入标题时继续保留现有的数据保护行为。
         *
         * 用户即使真正切换到其他程序，
         * 当前新增内容也不会因为窗口失活而丢失。
         */
        if (HasQuickAddTitle())
        {
            return;
        }

        /*
         * 只有：
         *
         * 1. Popup 仍然打开；
         * 2. 不是 Popup 自身恢复焦点；
         * 3. 标题仍然为空；
         *
         * 才认为用户真正离开当前新增操作，
         * 允许关闭空白 Popup。
         */
        QuickAddPopup.IsOpen =
            false;
    }

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

        _hostWindow.Deactivated -=
            OnHostWindowDeactivated;

        _hostWindow =
            null;
    }

    #endregion



    #region 可视树辅助方法

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
    /// 判断当前鼠标位置是否真正位于指定元素的
    /// 实际可见矩形范围内。
    ///
    /// Popup 使用独立窗口承载，单纯依赖
    /// OriginalSource 或可视树关系有时不足以准确判断
    /// 鼠标是否位于 Popup 内部。
    ///
    /// 因此这里直接把鼠标坐标转换到元素自身坐标系，
    /// 再根据 ActualWidth / ActualHeight 做几何判断。
    /// </summary>
    private static bool IsPointerInsideElement(
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

        System.Windows.Point mousePosition =
            Mouse.GetPosition(
                element);

        Rect elementBounds =
            new(
                0,
                0,
                element.ActualWidth,
                element.ActualHeight);

        return elementBounds.Contains(
            mousePosition);
    }

    #endregion
}
