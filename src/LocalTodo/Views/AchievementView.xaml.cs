using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.ViewModels;
using Point = System.Windows.Point;

namespace LocalTodo.Views;

public partial class AchievementView : UserControl
{
    private const string CategoryDragDataFormat =
        "LocalTodo.AchievementCategoryId";
    private const double DragThreshold = 4;
    private const double AutoScrollEdgeZone = 56;
    private const double AutoScrollMaximumStep = 14;
    private static readonly TimeSpan AutoScrollInterval =
        TimeSpan.FromMilliseconds(25);

    private readonly DispatcherTimer _autoScrollTimer;
    private AchievementInteractionMode _interactionMode;
    private AchievementTimelineCardViewModel? _interactionCard;
    private Point _interactionStartPoint;
    private Point _lastPointerInViewport;
    private DateTime _originalStart;
    private DateTime _originalEnd;
    private bool _interactionDragStarted;
    private bool _interactionHasMoved;
    private Point _categoryDragStartPoint;
    private AchievementCategoryDefinition? _categoryDragCandidate;
    private ListBoxItem? _categoryDropIndicatorItem;
    private Window? _hostWindow;

    public AchievementView()
    {
        InitializeComponent();
        _autoScrollTimer = new DispatcherTimer
        {
            Interval = AutoScrollInterval
        };
        _autoScrollTimer.Tick += OnAutoScrollTick;
    }

    private AchievementViewModel? ViewModel =>
        DataContext as AchievementViewModel;

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);

        if (_hostWindow is not null)
        {
            _hostWindow.Deactivated += OnHostWindowDeactivated;
        }

        ViewModel?.UpdateTimelineViewport(
            TimelineViewport.ActualWidth);
        ScheduleScrollToSelectedYear();
    }

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
            _hostWindow = null;
        }

        _autoScrollTimer.Stop();
        CancelInteraction();
        ViewModel?.ClearRecentTimelineHighlight();
    }

    private void OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_interactionMode == AchievementInteractionMode.None)
        {
            ViewModel?.ClearRecentTimelineHighlight();
        }
    }

    /// <summary>
    /// 只有前台真正切换到其他进程时才关闭抽屉。
    /// DatePicker、确认框等同进程弹层不会误触发自动关闭。
    /// </summary>
    private async void OnHostWindowDeactivated(
        object? sender,
        EventArgs e)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);

        IntPtr foreground = GetForegroundWindow();

        if (foreground == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(
            foreground,
            out uint foregroundProcessId);

        if (foregroundProcessId == (uint)Environment.ProcessId)
        {
            return;
        }

        if (ViewModel is { } viewModel)
        {
            await viewModel.HandleHostWindowDeactivatedAsync();
        }
    }

    private void OnTimelineViewportSizeChanged(
        object sender,
        SizeChangedEventArgs eventArgs)
    {
        ViewModel?.UpdateTimelineViewport(
            eventArgs.NewSize.Width);
        ScheduleScrollToSelectedYear();
    }

    private void OnTimelineScrollChanged(
        object sender,
        ScrollChangedEventArgs eventArgs)
    {
        if (Math.Abs(eventArgs.HorizontalChange) > 0.01)
        {
            TimelineHeaderScrollViewer.ScrollToHorizontalOffset(
                eventArgs.HorizontalOffset);
        }
    }

    private void OnYearNavigationClick(
        object sender,
        RoutedEventArgs e) =>
        ScheduleScrollToSelectedYear();

    private void ScheduleScrollToSelectedYear()
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ViewModel is not { } viewModel)
                {
                    return;
                }

                TimelineScrollViewer.ScrollToHorizontalOffset(
                    viewModel.SelectedYearScrollOffset);
                TimelineHeaderScrollViewer.ScrollToHorizontalOffset(
                    viewModel.SelectedYearScrollOffset);
            },
            DispatcherPriority.Loaded);
    }

    private void OnAchievementCardMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) =>
        BeginInteraction(
            sender,
            e,
            AchievementInteractionMode.Move);

    private void OnAchievementStartHandleMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) =>
        BeginInteraction(
            sender,
            e,
            AchievementInteractionMode.ResizeStart);

    private void OnAchievementEndHandleMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) =>
        BeginInteraction(
            sender,
            e,
            AchievementInteractionMode.ResizeEnd);

    private void BeginInteraction(
        object sender,
        MouseButtonEventArgs e,
        AchievementInteractionMode mode)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not
                AchievementTimelineCardViewModel card ||
            ViewModel is not { } viewModel ||
            viewModel.IsBusy ||
            viewModel.IsEditorVisible ||
            viewModel.IsCategoryManagerVisible)
        {
            return;
        }

        _interactionMode = mode;
        _interactionCard = card;
        _interactionStartPoint =
            e.GetPosition(TimelineSurface);
        _lastPointerInViewport =
            e.GetPosition(TimelineScrollViewer);
        _originalStart = card.Record.PeriodStart.Date;
        _originalEnd = card.Record.CompletedDate;
        _interactionDragStarted = false;
        _interactionHasMoved = false;
        viewModel.BeginTimelineInteraction(card.Record);

        Mouse.Capture(this, CaptureMode.Element);
        e.Handled = true;
    }

    private void OnPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_interactionMode == AchievementInteractionMode.None ||
            e.LeftButton != MouseButtonState.Pressed ||
            _interactionCard is not { } card ||
            ViewModel is not { } viewModel ||
            viewModel.TimelineWidth <= 0)
        {
            return;
        }

        _lastPointerInViewport =
            e.GetPosition(TimelineScrollViewer);
        bool updated = UpdateInteraction(
            e.GetPosition(TimelineSurface));
        UpdateAutoScrollTimer();

        if (updated)
        {
            e.Handled = true;
        }
    }

    private bool UpdateInteraction(Point current)
    {
        if (_interactionMode == AchievementInteractionMode.None ||
            _interactionCard is not { } card ||
            ViewModel is not { } viewModel ||
            viewModel.TimelineWidth <= 0)
        {
            return false;
        }

        double deltaX = current.X - _interactionStartPoint.X;

        if (!_interactionDragStarted &&
            Math.Abs(deltaX) < DragThreshold)
        {
            return false;
        }

        _interactionDragStarted = true;

        DateTime rangeStart = viewModel.TimelineRangeStart;
        DateTime rangeEnd = viewModel.TimelineRangeEnd;
        DateTime newStart = _originalStart;
        DateTime newEnd = _originalEnd;

        switch (_interactionMode)
        {
            case AchievementInteractionMode.Move:
                int daysInRange =
                    (viewModel.TimelineRangeEndExclusive -
                     rangeStart).Days;
                int deltaDays =
                    AchievementTimelineLayoutEngine.DeltaXToDays(
                        deltaX,
                        rangeStart,
                        viewModel.TimelineRangeEndExclusive,
                        viewModel.TimelineWidth);

                if (deltaDays == 0)
                {
                    return false;
                }

                int durationDays =
                    (_originalEnd - _originalStart).Days;

                if (durationDays >= daysInRange)
                {
                    return false;
                }

                newStart = _originalStart.AddDays(deltaDays);
                newEnd = _originalEnd.AddDays(deltaDays);

                if (newStart < rangeStart)
                {
                    newStart = rangeStart;
                    newEnd = newStart.AddDays(durationDays);
                }

                if (newEnd > rangeEnd)
                {
                    newEnd = rangeEnd;
                    newStart = newEnd.AddDays(-durationDays);
                }
                break;

            case AchievementInteractionMode.ResizeStart:
                newStart = ClampDate(
                    AchievementTimelineLayoutEngine.XToStartDate(
                        current.X,
                        rangeStart,
                        viewModel.TimelineRangeEndExclusive,
                        viewModel.TimelineWidth),
                    rangeStart,
                    _originalEnd);
                break;

            case AchievementInteractionMode.ResizeEnd:
                newEnd = ClampDate(
                    AchievementTimelineLayoutEngine.XToEndDate(
                        current.X,
                        rangeStart,
                        viewModel.TimelineRangeEndExclusive,
                        viewModel.TimelineWidth),
                    _originalStart,
                    rangeEnd);
                break;
        }

        bool rangeChanged =
            newStart != _originalStart ||
            newEnd != _originalEnd;

        if (rangeChanged || _interactionHasMoved)
        {
            viewModel.PreviewDateRange(
                card.Record,
                newStart,
                newEnd);
            _interactionHasMoved = true;
            return true;
        }

        return false;
    }

    private void UpdateAutoScrollTimer()
    {
        if (!_interactionDragStarted ||
            _interactionMode == AchievementInteractionMode.None)
        {
            _autoScrollTimer.Stop();
            return;
        }

        double delta = GetAutoScrollDelta();

        if (Math.Abs(delta) < 0.01)
        {
            _autoScrollTimer.Stop();
        }
        else if (!_autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Start();
        }
    }

    private void OnAutoScrollTick(
        object? sender,
        EventArgs e)
    {
        if (_interactionMode == AchievementInteractionMode.None ||
            !_interactionDragStarted ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _autoScrollTimer.Stop();
            return;
        }

        double delta = GetAutoScrollDelta();
        double currentOffset =
            TimelineScrollViewer.HorizontalOffset;
        double targetOffset = Math.Clamp(
            currentOffset + delta,
            0,
            TimelineScrollViewer.ScrollableWidth);

        if (Math.Abs(targetOffset - currentOffset) < 0.01)
        {
            _autoScrollTimer.Stop();
            return;
        }

        TimelineScrollViewer.ScrollToHorizontalOffset(targetOffset);

        // 鼠标相对视口保持不动时，真实时间轴坐标等于视口坐标加滚动偏移。
        UpdateInteraction(
            new Point(
                _lastPointerInViewport.X + targetOffset,
                0));
    }

    private double GetAutoScrollDelta()
    {
        double viewportWidth =
            TimelineScrollViewer.ViewportWidth > 0
                ? TimelineScrollViewer.ViewportWidth
                : TimelineScrollViewer.ActualWidth;

        return AchievementTimelineLayoutEngine
            .CalculateAutoScrollDelta(
                _lastPointerInViewport.X,
                viewportWidth,
                AutoScrollEdgeZone,
                AutoScrollMaximumStep);
    }

    private async void OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_interactionMode == AchievementInteractionMode.None)
        {
            return;
        }

        AchievementInteractionMode mode = _interactionMode;
        AchievementTimelineCardViewModel? card = _interactionCard;
        bool moved = _interactionHasMoved;
        DateTime originalStart = _originalStart;
        DateTime originalEnd = _originalEnd;
        ClearInteractionState();

        if (card is null || ViewModel is not { } viewModel)
        {
            return;
        }

        if (moved)
        {
            bool committed =
                await viewModel.CommitTimelineInteractionAsync(
                card.Record,
                originalStart,
                originalEnd);

            if (committed)
            {
                ScheduleRevealTimelineItem(card.Record.Id);
            }
        }
        else
        {
            viewModel.EndTimelineInteractionWithoutChanges(
                card.Record.Id);

            if (mode == AchievementInteractionMode.Move)
            {
                viewModel.OpenDetailsCommand.Execute(card);
            }
        }

        e.Handled = true;
    }

    private void ScheduleRevealTimelineItem(string recordId)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ViewModel is not { } viewModel ||
                    viewModel.FindTimelineItem(recordId) is not { } item)
                {
                    return;
                }

                TimelineScrollViewer.UpdateLayout();

                double horizontalCenter =
                    item.CardLeft + item.CardWidth / 2;
                double horizontalOffset = Math.Clamp(
                    horizontalCenter -
                    TimelineScrollViewer.ViewportWidth / 2,
                    0,
                    TimelineScrollViewer.ScrollableWidth);
                double verticalCenter =
                    item.Top +
                    AchievementTimelineLayoutEngine.CardHeight / 2;
                double verticalOffset = Math.Clamp(
                    verticalCenter -
                    TimelineScrollViewer.ViewportHeight / 2,
                    0,
                    TimelineScrollViewer.ScrollableHeight);

                TimelineScrollViewer.ScrollToHorizontalOffset(
                    horizontalOffset);
                TimelineHeaderScrollViewer.ScrollToHorizontalOffset(
                    horizontalOffset);
                TimelineScrollViewer.ScrollToVerticalOffset(
                    verticalOffset);
            },
            DispatcherPriority.Loaded);
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_interactionMode != AchievementInteractionMode.None)
        {
            CancelInteraction();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_interactionMode != AchievementInteractionMode.None)
        {
            CancelInteraction();
            e.Handled = true;
            return;
        }

        if (ViewModel?.IsCategoryManagerVisible == true)
        {
            ViewModel.CloseCategoryManagerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ViewModel?.IsEditorVisible == true)
        {
            ViewModel.CloseEditorCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnDrawerBackdropMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.RequestCloseEditorAsync();
        }

        e.Handled = true;
    }

    private void OnCategoryBackdropMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ViewModel?.CloseCategoryManagerCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCategoryListPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox ||
            ViewModel?.IsBusy == true ||
            e.OriginalSource is not DependencyObject source)
        {
            _categoryDragCandidate = null;
            return;
        }

        ListBoxItem? item =
            ItemsControl.ContainerFromElement(listBox, source)
                as ListBoxItem;
        _categoryDragCandidate =
            item?.DataContext as AchievementCategoryDefinition;
        _categoryDragStartPoint = e.GetPosition(listBox);
    }

    private void OnCategoryListPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (sender is not ListBox listBox ||
            e.LeftButton != MouseButtonState.Pressed ||
            _categoryDragCandidate is not { } category)
        {
            return;
        }

        Point current = e.GetPosition(listBox);

        if (Math.Abs(current.X - _categoryDragStartPoint.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _categoryDragStartPoint.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _categoryDragCandidate = null;
        ListBoxItem? draggedItem =
            listBox.ItemContainerGenerator.ContainerFromItem(category)
                as ListBoxItem;

        if (draggedItem is not null)
        {
            draggedItem.Opacity = 0.55;
        }

        try
        {
            DataObject data =
                new(CategoryDragDataFormat, category.Id);
            DragDrop.DoDragDrop(
                listBox,
                data,
                DragDropEffects.Move);
        }
        finally
        {
            if (draggedItem is not null)
            {
                draggedItem.Opacity = 1;
            }

            ClearCategoryDropIndicator();
        }
    }

    private void OnCategoryListPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        _categoryDragCandidate = null;

    private void OnCategoryListDragOver(
        object sender,
        DragEventArgs e)
    {
        if (sender is not ListBox listBox ||
            !e.Data.GetDataPresent(CategoryDragDataFormat) ||
            ViewModel?.IsBusy == true)
        {
            e.Effects = DragDropEffects.None;
            ClearCategoryDropIndicator();
            e.Handled = true;
            return;
        }

        GetCategoryInsertionIndex(
            listBox,
            e.GetPosition(listBox),
            out ListBoxItem? indicatorItem,
            out bool insertAfter);
        SetCategoryDropIndicator(
            indicatorItem,
            insertAfter);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnCategoryListDragLeave(
        object sender,
        DragEventArgs e) =>
        ClearCategoryDropIndicator();

    private async void OnCategoryListDrop(
        object sender,
        DragEventArgs e)
    {
        if (sender is not ListBox listBox ||
            ViewModel is not { } viewModel ||
            e.Data.GetData(CategoryDragDataFormat) is not string categoryId)
        {
            ClearCategoryDropIndicator();
            return;
        }

        int insertionIndex = GetCategoryInsertionIndex(
            listBox,
            e.GetPosition(listBox),
            out _,
            out _);
        ClearCategoryDropIndicator();
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        await viewModel.ReorderCategoryAsync(
            categoryId,
            insertionIndex);
    }

    private static int GetCategoryInsertionIndex(
        ListBox listBox,
        Point pointer,
        out ListBoxItem? indicatorItem,
        out bool insertAfter)
    {
        indicatorItem = null;
        insertAfter = false;

        for (int index = 0;
             index < listBox.Items.Count;
             index++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(index)
                is not ListBoxItem item)
            {
                continue;
            }

            Point topLeft =
                item.TranslatePoint(new Point(0, 0), listBox);
            double midpoint = topLeft.Y + item.ActualHeight / 2;

            if (pointer.Y < midpoint)
            {
                indicatorItem = item;
                return index;
            }

            if (pointer.Y <= topLeft.Y + item.ActualHeight)
            {
                indicatorItem = item;
                insertAfter = true;
                return index + 1;
            }
        }

        if (listBox.Items.Count > 0)
        {
            indicatorItem =
                listBox.ItemContainerGenerator.ContainerFromIndex(
                    listBox.Items.Count - 1) as ListBoxItem;
            insertAfter = true;
        }

        return listBox.Items.Count;
    }

    private void SetCategoryDropIndicator(
        ListBoxItem? item,
        bool insertAfter)
    {
        if (_categoryDropIndicatorItem == item &&
            Equals(
                item?.Tag,
                insertAfter ? "DropAfter" : "DropBefore"))
        {
            return;
        }

        ClearCategoryDropIndicator();
        _categoryDropIndicatorItem = item;

        if (item is not null)
        {
            item.Tag = insertAfter
                ? "DropAfter"
                : "DropBefore";
        }
    }

    private void ClearCategoryDropIndicator()
    {
        if (_categoryDropIndicatorItem is not null)
        {
            _categoryDropIndicatorItem.ClearValue(TagProperty);
            _categoryDropIndicatorItem = null;
        }
    }

    private void CancelInteraction()
    {
        if (_interactionMode == AchievementInteractionMode.None)
        {
            return;
        }

        if (_interactionCard is { } card &&
            ViewModel is { } viewModel)
        {
            viewModel.CancelTimelineInteraction(
                card.Record,
                _originalStart,
                _originalEnd);
        }

        ClearInteractionState();
    }

    private void ClearInteractionState()
    {
        _interactionMode = AchievementInteractionMode.None;
        _interactionCard = null;
        _interactionDragStarted = false;
        _interactionHasMoved = false;
        _autoScrollTimer.Stop();

        if (Mouse.Captured == this)
        {
            Mouse.Capture(null);
        }
    }

    private void OnEditorVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.NewValue is not true)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                EditorTitleTextBox.Focus();
                EditorTitleTextBox.CaretIndex =
                    EditorTitleTextBox.Text.Length;
            },
            DispatcherPriority.Input);
    }

    private static DateTime ClampDate(
        DateTime value,
        DateTime minimum,
        DateTime maximum) =>
        value < minimum
            ? minimum
            : value > maximum
                ? maximum
                : value;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    private enum AchievementInteractionMode
    {
        None,
        Move,
        ResizeStart,
        ResizeEnd
    }
}
