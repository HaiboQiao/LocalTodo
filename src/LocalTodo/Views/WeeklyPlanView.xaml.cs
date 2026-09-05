using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.ViewModels;
using Point = System.Windows.Point;

namespace LocalTodo.Views;

public partial class WeeklyPlanView :
    UserControl
{
    private const double DragThreshold = 4;

    private WeeklyPlanInteractionMode
        _interactionMode;

    private WeeklyPlanCardViewModel?
        _interactionCard;

    private WeeklyPlanLayoutSnapshot
        _interactionSnapshot;

    private Point
        _interactionStartPoint;

    private WeeklyDay
        _originalDay;

    private int
        _originalStartMinutes;

    private int
        _originalEndMinutes;

    private WeeklyDay
        _creationDay;

    private int
        _creationAnchorMinutes;

    private int
        _creationStartMinutes;

    private int
        _creationEndMinutes;

    private bool
        _interactionHasMoved;

    private bool
        _interactionCopiesItem;

    private WeeklyPlanCardViewModel?
        _copyPreviewCard;

    private bool
        _copyPreviewCanBePlaced;

    public WeeklyPlanView()
    {
        InitializeComponent();
    }

    private WeeklyPlanViewModel? ViewModel =>
        DataContext as WeeklyPlanViewModel;

    private void OnViewLoaded(
        object sender,
        RoutedEventArgs e) =>
        UpdateViewport();

    private void OnViewUnloaded(
        object sender,
        RoutedEventArgs e) =>
        ViewModel?.SelectItem(null);

    private void OnViewPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ViewModel is not { } viewModel ||
            viewModel.IsEditorVisible ||
            _interactionMode !=
                WeeklyPlanInteractionMode.None ||
            FindPlanCard(e.OriginalSource as DependencyObject)
                is not null)
        {
            return;
        }

        viewModel.SelectItem(null);
    }

    private void OnTimelineSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        UpdateViewport();

    private void UpdateViewport()
    {
        ViewModel?.UpdateViewport(
            WeekSurface.ActualWidth,
            WeekSurface.ActualHeight);
    }

    private void OnTimelineMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ViewModel is not { } viewModel ||
            viewModel.IsBusy ||
            viewModel.IsEditorVisible ||
            FindPlanCard(e.OriginalSource as DependencyObject)
                is not null)
        {
            return;
        }

        Point point =
            e.GetPosition(WeekSurface);
        WeeklyPlanLayoutSnapshot snapshot =
            viewModel.BeginLayoutInteraction();

        if (snapshot.PixelsPerMinute <= 0 ||
            snapshot.DayWidth <= 0)
        {
            viewModel.EndLayoutInteraction();
            return;
        }

        WeeklyDay day =
            WeeklyPlanLayoutEngine.XToDay(
                point.X,
                snapshot);
        int minutes =
            Math.Clamp(
                WeeklyPlanLayoutEngine.YToMinutes(
                    point.Y,
                    snapshot),
                0,
                1440 -
                WeeklyPlanRules.MinimumDurationMinutes);

        if (e.ClickCount == 2)
        {
            viewModel.EndLayoutInteraction();
            viewModel.StartCreateAt(day, minutes);
            e.Handled = true;
            return;
        }

        _interactionMode =
            WeeklyPlanInteractionMode.Create;
        _interactionSnapshot = snapshot;
        _interactionStartPoint = point;
        _creationDay = day;
        _creationAnchorMinutes = minutes;
        _creationStartMinutes = minutes;
        _creationEndMinutes =
            Math.Min(1440, minutes + 60);
        _interactionHasMoved = false;

        Mouse.Capture(
            this,
            CaptureMode.Element);

        e.Handled = true;
    }

    private void OnPlanBlockMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) =>
        BeginPlanInteraction(
            sender,
            e,
            WeeklyPlanInteractionMode.Move);

    private void OnPlanStartHandleMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) =>
        BeginPlanInteraction(
            sender,
            e,
            WeeklyPlanInteractionMode.ResizeStart);

    private void OnPlanEndHandleMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) =>
        BeginPlanInteraction(
            sender,
            e,
            WeeklyPlanInteractionMode.ResizeEnd);

    private void BeginPlanInteraction(
        object sender,
        MouseButtonEventArgs e,
        WeeklyPlanInteractionMode mode)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not
                WeeklyPlanCardViewModel card ||
            ViewModel is not { } viewModel ||
            viewModel.IsBusy ||
            viewModel.IsEditorVisible)
        {
            return;
        }

        viewModel.SelectItem(card.Item);
        Keyboard.Focus(this);

        if (e.ClickCount == 2)
        {
            viewModel.EditCommand.Execute(card.Item);
            e.Handled = true;
            return;
        }

        WeeklyPlanLayoutSnapshot snapshot =
            viewModel.BeginLayoutInteraction();

        if (snapshot.PixelsPerMinute <= 0)
        {
            viewModel.EndLayoutInteraction();
            return;
        }

        _interactionCard = card;
        _interactionMode = mode;
        _interactionSnapshot = snapshot;
        _interactionStartPoint =
            e.GetPosition(WeekSurface);
        _originalDay = card.Item.Day;
        _originalStartMinutes =
            card.Item.StartMinutes;
        _originalEndMinutes =
            card.Item.EndMinutes;
        _interactionHasMoved = false;
        _interactionCopiesItem =
            mode == WeeklyPlanInteractionMode.Move &&
            (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        Mouse.Capture(
            this,
            CaptureMode.Element);

        e.Handled = true;
    }

    private void OnPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_interactionMode ==
                WeeklyPlanInteractionMode.None ||
            e.LeftButton != MouseButtonState.Pressed ||
            ViewModel is not { } viewModel)
        {
            return;
        }

        Point point =
            e.GetPosition(WeekSurface);
        double deltaX =
            point.X - _interactionStartPoint.X;
        double deltaY =
            point.Y - _interactionStartPoint.Y;

        if (!_interactionHasMoved &&
            Math.Sqrt(
                deltaX * deltaX +
                deltaY * deltaY) < DragThreshold)
        {
            return;
        }

        switch (_interactionMode)
        {
            case WeeklyPlanInteractionMode.Create:
                UpdateCreationPreview(point);
                break;

            case WeeklyPlanInteractionMode.Move:
            case WeeklyPlanInteractionMode.ResizeStart:
            case WeeklyPlanInteractionMode.ResizeEnd:
                UpdatePlanInteraction(point, viewModel);
                break;
        }

        e.Handled = true;
    }

    private void UpdateCreationPreview(
        Point point)
    {
        int currentMinutes =
            Math.Clamp(
                WeeklyPlanLayoutEngine.YToMinutes(
                    point.Y,
                    _interactionSnapshot),
                0,
                1440);

        int start =
            Math.Min(
                _creationAnchorMinutes,
                currentMinutes);
        int end =
            Math.Max(
                _creationAnchorMinutes,
                currentMinutes);

        if (end - start <
            WeeklyPlanRules.MinimumDurationMinutes)
        {
            if (point.Y < _interactionStartPoint.Y)
            {
                start =
                    Math.Max(
                        0,
                        _creationAnchorMinutes -
                        WeeklyPlanRules.MinimumDurationMinutes);
                end = _creationAnchorMinutes;
            }
            else
            {
                start = _creationAnchorMinutes;
                end =
                    Math.Min(
                        1440,
                        _creationAnchorMinutes +
                        WeeklyPlanRules.MinimumDurationMinutes);
            }
        }

        if (end <= start)
        {
            return;
        }

        _creationStartMinutes = start;
        _creationEndMinutes = end;
        _interactionHasMoved = true;

        Canvas.SetLeft(
            CreationPreview,
            ((int)_creationDay - 1) *
            _interactionSnapshot.DayWidth +
            WeeklyPlanLayoutEngine.DayInset);
        Canvas.SetTop(
            CreationPreview,
            WeeklyPlanLayoutEngine.MinutesToY(
                start,
                _interactionSnapshot));

        CreationPreview.Width =
            Math.Max(
                0,
                _interactionSnapshot.DayWidth -
                WeeklyPlanLayoutEngine.DayInset * 2);
        CreationPreview.Height =
            Math.Max(
                0,
                (end - start) *
                _interactionSnapshot.PixelsPerMinute);
        CreationPreviewStartText.Text =
            WeeklyPlanLayoutEngine.FormatMinutes(start);
        CreationPreviewEndText.Text =
            WeeklyPlanLayoutEngine.FormatMinutes(end);
        CreationPreview.Visibility =
            Visibility.Visible;
    }

    private void UpdatePlanInteraction(
        Point point,
        WeeklyPlanViewModel viewModel)
    {
        if (_interactionCard is not { } card)
        {
            return;
        }

        WeeklyPlanCardViewModel interactionCard =
            _interactionCopiesItem &&
            _interactionMode == WeeklyPlanInteractionMode.Move
                ? EnsureCopyPreview(card)
                : card;
        WeeklyPlanItem item = interactionCard.Item;
        int deltaMinutes =
            WeeklyPlanLayoutEngine.SnapMinutes(
                (point.Y - _interactionStartPoint.Y) /
                _interactionSnapshot.PixelsPerMinute);

        switch (_interactionMode)
        {
            case WeeklyPlanInteractionMode.Move:
                int duration =
                    _originalEndMinutes -
                    _originalStartMinutes;
                int movedStart =
                    Math.Clamp(
                        _originalStartMinutes +
                        deltaMinutes,
                        0,
                        1440 - duration);
                WeeklyDay movedDay =
                    WeeklyPlanLayoutEngine.XToDay(
                        point.X,
                        _interactionSnapshot);

                item.Day = movedDay;
                item.SetTimeRange(
                    movedStart,
                    movedStart + duration);
                _interactionHasMoved =
                    movedDay != _originalDay ||
                    movedStart != _originalStartMinutes;

                if (_interactionCopiesItem)
                {
                    _copyPreviewCanBePlaced =
                        viewModel.CanPlaceCopy(
                            movedDay,
                            movedStart,
                            movedStart + duration);
                    interactionCard.IsPlacementValid =
                        _copyPreviewCanBePlaced;
                    UpdateCopyPreviewPlacement(
                        interactionCard);
                }
                break;

            case WeeklyPlanInteractionMode.ResizeStart:
                int resizedStart =
                    Math.Clamp(
                        _originalStartMinutes +
                        deltaMinutes,
                        0,
                        _originalEndMinutes -
                        WeeklyPlanRules.MinimumDurationMinutes);

                item.SetTimeRange(
                    resizedStart,
                    _originalEndMinutes);
                _interactionHasMoved =
                    resizedStart != _originalStartMinutes;
                break;

            case WeeklyPlanInteractionMode.ResizeEnd:
                int resizedEnd =
                    Math.Clamp(
                        _originalEndMinutes +
                        deltaMinutes,
                        _originalStartMinutes +
                        WeeklyPlanRules.MinimumDurationMinutes,
                        1440);

                item.SetTimeRange(
                    _originalStartMinutes,
                    resizedEnd);
                _interactionHasMoved =
                    resizedEnd != _originalEndMinutes;
                break;
        }

        if (!_interactionCopiesItem)
        {
            viewModel.RefreshInteractionLayout();
        }
    }

    private WeeklyPlanCardViewModel EnsureCopyPreview(
        WeeklyPlanCardViewModel sourceCard)
    {
        if (_copyPreviewCard is not null)
        {
            return _copyPreviewCard;
        }

        WeeklyPlanItem source = sourceCard.Item;
        WeeklyPlanItem previewItem =
            new()
            {
                Day = source.Day,
                StartMinutes = source.StartMinutes,
                EndMinutes = source.EndMinutes,
                Title = source.Title,
                Description = source.Description,
                Color = source.Color
            };

        _copyPreviewCard =
            new WeeklyPlanCardViewModel(previewItem)
            {
                IsPlacementValid = false
            };
        _copyPreviewCanBePlaced = false;
        CopyPreviewHost.DataContext =
            _copyPreviewCard;
        CopyPreviewHost.Visibility =
            Visibility.Visible;

        return _copyPreviewCard;
    }

    private void UpdateCopyPreviewPlacement(
        WeeklyPlanCardViewModel previewCard)
    {
        WeeklyPlanItem item = previewCard.Item;
        double height =
            Math.Max(
                0,
                (item.EndMinutes - item.StartMinutes) *
                _interactionSnapshot.PixelsPerMinute);
        double width =
            Math.Max(
                0,
                _interactionSnapshot.DayWidth -
                WeeklyPlanLayoutEngine.DayInset * 2);
        double left =
            ((int)item.Day - 1) *
            _interactionSnapshot.DayWidth +
            WeeklyPlanLayoutEngine.DayInset;
        double top =
            WeeklyPlanLayoutEngine.MinutesToY(
                item.StartMinutes,
                _interactionSnapshot);

        previewCard.Apply(
            new WeeklyPlanCardPlacement(
                item,
                left,
                top,
                width,
                height,
                0,
                1,
                height <
                WeeklyPlanLayoutEngine.CompactHeightThreshold));

        Canvas.SetLeft(
            CopyPreviewHost,
            left);
        Canvas.SetTop(
            CopyPreviewHost,
            top);
        CopyPreviewHost.Width = width;
        CopyPreviewHost.Height = height;
    }

    private async void OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_interactionMode ==
            WeeklyPlanInteractionMode.None)
        {
            return;
        }

        WeeklyPlanInteractionMode mode =
            _interactionMode;
        WeeklyPlanCardViewModel? card =
            _interactionCard;
        bool wasMoved =
            _interactionHasMoved;
        bool copiesItem =
            _interactionCopiesItem;
        WeeklyPlanItem? copyTarget =
            _copyPreviewCard?.Item;
        bool copyCanBePlaced =
            _copyPreviewCanBePlaced;
        WeeklyDay originalDay =
            _originalDay;
        int originalStart =
            _originalStartMinutes;
        int originalEnd =
            _originalEndMinutes;
        WeeklyDay creationDay =
            _creationDay;
        int creationStart =
            _creationStartMinutes;
        int creationEnd =
            _creationEndMinutes;

        ClearInteractionState();

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (mode == WeeklyPlanInteractionMode.Create)
        {
            viewModel.EndLayoutInteraction();

            if (wasMoved)
            {
                viewModel.StartCreateRange(
                    creationDay,
                    creationStart,
                    creationEnd);
            }

            e.Handled = true;
            return;
        }

        if (wasMoved && card is not null)
        {
            if (copiesItem)
            {
                if (copyCanBePlaced &&
                    copyTarget is not null)
                {
                    await viewModel.CommitCopyInteractionAsync(
                        card.Item,
                        copyTarget.Day,
                        copyTarget.StartMinutes,
                        copyTarget.EndMinutes);
                }
            }
            else
            {
                await viewModel.CommitInteractionAsync(
                    card.Item,
                    originalDay,
                    originalStart,
                    originalEnd);
            }
        }

        viewModel.EndLayoutInteraction();
        e.Handled = true;
    }

    private void ClearInteractionState()
    {
        _interactionMode =
            WeeklyPlanInteractionMode.None;
        _interactionCard = null;
        _interactionHasMoved = false;
        _interactionCopiesItem = false;
        _copyPreviewCard = null;
        _copyPreviewCanBePlaced = false;
        CopyPreviewHost.DataContext = null;
        CopyPreviewHost.Visibility =
            Visibility.Collapsed;
        CreationPreview.Visibility =
            Visibility.Collapsed;

        if (Mouse.Captured == this)
        {
            Mouse.Capture(null);
        }
    }

    private void CancelInteraction()
    {
        if (_interactionMode ==
            WeeklyPlanInteractionMode.None)
        {
            return;
        }

        if (!_interactionCopiesItem &&
            _interactionCard is { } card)
        {
            card.Item.Day = _originalDay;
            card.Item.SetTimeRange(
                _originalStartMinutes,
                _originalEndMinutes);
            ViewModel?.RefreshInteractionLayout();
        }

        ClearInteractionState();
        ViewModel?.EndLayoutInteraction();
    }

    private void OnLostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_interactionMode !=
            WeeklyPlanInteractionMode.None)
        {
            CancelInteraction();
        }
    }

    private void OnEditorBackdropMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ViewModel?.CancelEditCommand.Execute(null);
        e.Handled = true;
    }

    private async void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Delete &&
            _interactionMode ==
                WeeklyPlanInteractionMode.None &&
            ViewModel is
            {
                IsEditorVisible: false,
                SelectedItem: not null
            } viewModel)
        {
            e.Handled = true;
            await viewModel.DeleteSelectedAsync();
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_interactionMode !=
            WeeklyPlanInteractionMode.None)
        {
            CancelInteraction();
            e.Handled = true;
            return;
        }

        if (ViewModel?.IsEditorVisible == true)
        {
            ViewModel.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnEditorVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                EditorTitleTextBox.Focus();
                EditorTitleTextBox.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private static WeeklyPlanCardViewModel? FindPlanCard(
        DependencyObject? source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is FrameworkElement element &&
                element.DataContext is
                    WeeklyPlanCardViewModel card)
            {
                return card;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(
        DependencyObject current)
    {
        if (current is Visual)
        {
            return VisualTreeHelper.GetParent(current);
        }

        if (current is ContentElement content)
        {
            return ContentOperations.GetParent(content) ??
                   (content as FrameworkContentElement)?.Parent;
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private enum WeeklyPlanInteractionMode
    {
        None,
        Create,
        Move,
        ResizeStart,
        ResizeEnd
    }
}
