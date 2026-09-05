using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 自适应七日时间画布的状态、编辑器和持久化协调。
/// 鼠标坐标解释仍由 View 负责，所有可测试的布局计算委托给布局引擎。
/// </summary>
public partial class WeeklyPlanViewModel :
    ObservableObject
{
    private readonly WeeklyPlanService _service;
    private readonly DialogService _dialogService;
    private DateTimeOffset _editorCreatedAt;
    private WeeklyPlanLayoutSnapshot? _currentSnapshot;
    private WeeklyPlanLayoutSnapshot? _interactionSnapshot;
    private double _pendingCanvasWidth;
    private double _pendingCanvasHeight;
    private bool _hasPendingViewport;

    public ObservableCollection<WeeklyPlanCardViewModel> Cards
    { get; } = [];

    public ObservableCollection<WeeklyPlanGridLine> GridLines
    { get; } = [];

    public IReadOnlyList<WeeklyPlanDayHeader> Days
    { get; }

    public IReadOnlyList<WeeklyDayOption> DayOptions
    { get; }

    public IReadOnlyList<WeeklyTimeOption> StartTimeOptions
    { get; }

    public IReadOnlyList<WeeklyTimeOption> EndTimeOptions
    { get; }

    public IReadOnlyList<WeeklyColorOption> ColorOptions
    { get; }

    [ObservableProperty]
    private string editorId = string.Empty;

    [ObservableProperty]
    private string editorTitle = string.Empty;

    [ObservableProperty]
    private string editorDescription = string.Empty;

    [ObservableProperty]
    private WeeklyDay editorDay = WeeklyDay.Monday;

    [ObservableProperty]
    private int editorStartMinutes = 540;

    [ObservableProperty]
    private int editorEndMinutes = 600;

    [ObservableProperty]
    private WeeklyPlanColor editorColor =
        WeeklyPlanColorStorage.DefaultColor;

    [ObservableProperty]
    private bool isEditingExisting;

    [ObservableProperty]
    private bool isEditorVisible;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string editorErrorMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool hasPlans;

    [ObservableProperty]
    private bool isLayoutLocked;

    [ObservableProperty]
    private int displayStartMinutes =
        WeeklyPlanLayoutEngine.DefaultDisplayStartMinutes;

    [ObservableProperty]
    private int displayEndMinutes =
        WeeklyPlanLayoutEngine.DefaultDisplayEndMinutes;

    [ObservableProperty]
    private double pixelsPerMinute;

    public string EditorHeading =>
        IsEditingExisting
            ? "编辑安排"
            : "新增安排";

    public bool HasEditorError =>
        !string.IsNullOrWhiteSpace(EditorErrorMessage);

    public bool HasStatusMessage =>
        !string.IsNullOrWhiteSpace(StatusMessage);

    public WeeklyPlanItem? SelectedItem =>
        Cards.FirstOrDefault(card =>
            card.IsSelected)?.Item;

    public WeeklyPlanViewModel(
        WeeklyPlanService service,
        DialogService dialogService)
    {
        _service = service;
        _dialogService = dialogService;

        DayOptions =
            Enum.GetValues<WeeklyDay>()
                .Select(day =>
                    new WeeklyDayOption(
                        day,
                        day.GetTitle()))
                .ToArray();

        Days =
            DayOptions
                .Select(option =>
                    new WeeklyPlanDayHeader(
                        option.Value,
                        option.Title))
                .ToArray();

        StartTimeOptions =
            Enumerable.Range(0, 96)
                .Select(index =>
                    CreateTimeOption(index * 15))
                .ToArray();

        EndTimeOptions =
            Enumerable.Range(1, 96)
                .Select(index =>
                    CreateTimeOption(index * 15))
                .ToArray();

        ColorOptions =
        [
            new(WeeklyPlanColor.Blue, "浅蓝"),
            new(WeeklyPlanColor.Green, "浅绿"),
            new(WeeklyPlanColor.Teal, "浅青"),
            new(WeeklyPlanColor.Purple, "浅紫"),
            new(WeeklyPlanColor.Pink, "浅粉"),
            new(WeeklyPlanColor.Orange, "浅橙"),
            new(WeeklyPlanColor.Yellow, "浅黄"),
            new(WeeklyPlanColor.Gray, "浅灰")
        ];
    }

    partial void OnIsEditingExistingChanged(
        bool value) =>
        OnPropertyChanged(nameof(EditorHeading));

    partial void OnEditorErrorMessageChanged(
        string value) =>
        OnPropertyChanged(nameof(HasEditorError));

    partial void OnStatusMessageChanged(
        string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnEditorStartMinutesChanged(
        int value)
    {
        if (EditorEndMinutes <= value)
        {
            EditorEndMinutes =
                Math.Min(1440, value + 60);
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            await ReloadItemsAsync();
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "加载每周计划失败。",
                exception);
            StatusMessage =
                $"加载失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void UpdateViewport(
        double canvasWidth,
        double canvasHeight)
    {
        if (!double.IsFinite(canvasWidth) ||
            !double.IsFinite(canvasHeight) ||
            canvasWidth <= 0 ||
            canvasHeight <= 0)
        {
            return;
        }

        if (IsLayoutLocked)
        {
            _pendingCanvasWidth = canvasWidth;
            _pendingCanvasHeight = canvasHeight;
            _hasPendingViewport = true;
            return;
        }

        ApplyLayout(
            WeeklyPlanLayoutEngine.CreateSnapshot(
                new WeeklyPlanDisplayRange(
                    DisplayStartMinutes,
                    DisplayEndMinutes),
                canvasWidth,
                canvasHeight));
    }

    public WeeklyPlanLayoutSnapshot BeginLayoutInteraction()
    {
        if (_currentSnapshot is null)
        {
            WeeklyPlanDisplayRange range =
                WeeklyPlanLayoutEngine.CalculateDisplayRange(
                    Cards.Select(card => card.Item));

            _currentSnapshot =
                WeeklyPlanLayoutEngine.CreateSnapshot(
                    range,
                    0,
                    0);
        }

        _interactionSnapshot = _currentSnapshot;
        IsLayoutLocked = true;

        return _interactionSnapshot.Value;
    }

    public void RefreshInteractionLayout()
    {
        if (_interactionSnapshot is { } snapshot)
        {
            ApplyCardPlacements(snapshot);
        }
    }

    public void EndLayoutInteraction()
    {
        IsLayoutLocked = false;
        _interactionSnapshot = null;

        double width =
            _hasPendingViewport
                ? _pendingCanvasWidth
                : _currentSnapshot?.CanvasWidth ?? 0;
        double height =
            _hasPendingViewport
                ? _pendingCanvasHeight
                : _currentSnapshot?.CanvasHeight ?? 0;

        _hasPendingViewport = false;
        RecalculateAdaptiveLayout(width, height);
    }

    public void SelectItem(
        WeeklyPlanItem? item)
    {
        foreach (WeeklyPlanCardViewModel card in Cards)
        {
            card.IsSelected =
                ReferenceEquals(card.Item, item);
        }

        OnPropertyChanged(nameof(SelectedItem));
    }

    [RelayCommand]
    private void StartCreate()
    {
        StartCreateAt(
            WeeklyDay.Monday,
            FindSuggestedStart(
                WeeklyDay.Monday));
    }

    [RelayCommand]
    private void StartCreateForDay(
        WeeklyDay day)
    {
        StartCreateAt(
            day,
            FindSuggestedStart(day));
    }

    public void StartCreateAt(
        WeeklyDay day,
        int startMinutes) =>
        StartCreateRange(
            day,
            startMinutes,
            startMinutes + 60);

    public void StartCreateRange(
        WeeklyDay day,
        int startMinutes,
        int endMinutes)
    {
        int normalizedStart =
            Math.Clamp(
                WeeklyPlanLayoutEngine.SnapMinutes(startMinutes),
                0,
                1440 - WeeklyPlanRules.MinimumDurationMinutes);
        int normalizedEnd =
            Math.Clamp(
                WeeklyPlanLayoutEngine.SnapMinutes(endMinutes),
                normalizedStart +
                WeeklyPlanRules.MinimumDurationMinutes,
                1440);

        EditorId = string.Empty;
        EditorTitle = string.Empty;
        EditorDescription = string.Empty;
        EditorDay = day;
        EditorStartMinutes = normalizedStart;
        EditorEndMinutes = normalizedEnd;
        EditorColor = WeeklyPlanColorStorage.DefaultColor;
        EditorErrorMessage = string.Empty;
        _editorCreatedAt = default;
        IsEditingExisting = false;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void Edit(
        WeeklyPlanItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectItem(item);
        EditorId = item.Id;
        EditorTitle = item.Title;
        EditorDescription = item.Description;
        EditorDay = item.Day;
        EditorStartMinutes = item.StartMinutes;
        EditorEndMinutes = item.EndMinutes;
        EditorColor = item.Color;
        EditorErrorMessage = string.Empty;
        _editorCreatedAt = item.CreatedAt;
        IsEditingExisting = true;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorVisible = false;
        EditorErrorMessage = string.Empty;
        SelectItem(null);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        EditorErrorMessage = string.Empty;

        try
        {
            await _service.SaveAsync(
                new WeeklyPlanItem
                {
                    Id = EditorId,
                    Title = EditorTitle,
                    Description = EditorDescription,
                    Day = EditorDay,
                    StartMinutes = EditorStartMinutes,
                    EndMinutes = EditorEndMinutes,
                    Color = EditorColor,
                    CreatedAt = _editorCreatedAt
                });

            await ReloadItemsAsync();
            IsEditorVisible = false;
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "保存每周计划失败。",
                exception);
            EditorErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!IsEditingExisting ||
            string.IsNullOrWhiteSpace(EditorId) ||
            IsBusy ||
            !_dialogService.ConfirmRecordDeletion(
                "安排",
                EditorTitle))
        {
            return;
        }

        IsBusy = true;
        EditorErrorMessage = string.Empty;

        try
        {
            await _service.DeleteAsync(EditorId);
            IsEditorVisible = false;
            await ReloadItemsAsync();
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "删除每周计划失败。",
                exception);
            EditorErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 移动或调整完成后只写入一次数据库。
    /// 保存失败时同时恢复原始日期和时间。
    /// </summary>
    public async Task<bool> CommitInteractionAsync(
        WeeklyPlanItem item,
        WeeklyDay originalDay,
        int originalStartMinutes,
        int originalEndMinutes)
    {
        if (IsBusy)
        {
            RestoreInteraction(
                item,
                originalDay,
                originalStartMinutes,
                originalEndMinutes);
            return false;
        }

        IsBusy = true;

        try
        {
            await _service.SaveAsync(item);
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            RestoreInteraction(
                item,
                originalDay,
                originalStartMinutes,
                originalEndMinutes);

            AppLog.Error(
                "调整每周计划失败。",
                exception);
            StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Ctrl 拖动的预览不会修改原计划；松手时只保存已经验证可放置的副本。
    /// </summary>
    public async Task<bool> CommitCopyInteractionAsync(
        WeeklyPlanItem source,
        WeeklyDay targetDay,
        int targetStartMinutes,
        int targetEndMinutes)
    {
        if (IsBusy ||
            !CanPlaceCopy(
                targetDay,
                targetStartMinutes,
                targetEndMinutes))
        {
            return false;
        }

        IsBusy = true;

        try
        {
            WeeklyPlanItem copy =
                await _service.SaveAsync(
                    new WeeklyPlanItem
                    {
                        Title = source.Title,
                        Description = source.Description,
                        Day = targetDay,
                        StartMinutes = targetStartMinutes,
                        EndMinutes = targetEndMinutes,
                        Color = source.Color
                    });

            await ReloadItemsAsync(copy.Id);
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "拖动复制每周计划失败。",
                exception);
            StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 复制预览只能放在有效的星期和时间范围内，且不能与任何已有计划重叠。
    /// 首尾恰好衔接不属于重叠。
    /// </summary>
    public bool CanPlaceCopy(
        WeeklyDay targetDay,
        int targetStartMinutes,
        int targetEndMinutes)
    {
        if (!Enum.IsDefined(targetDay) ||
            targetStartMinutes < 0 ||
            targetEndMinutes > 1440 ||
            targetEndMinutes - targetStartMinutes <
                WeeklyPlanRules.MinimumDurationMinutes)
        {
            return false;
        }

        return !Cards.Any(card =>
            card.Item.Day == targetDay &&
            targetStartMinutes < card.Item.EndMinutes &&
            targetEndMinutes > card.Item.StartMinutes);
    }

    /// <summary>
    /// 删除时间轴上当前选中的计划，沿用编辑器相同的二次确认与持久化流程。
    /// </summary>
    public async Task<bool> DeleteSelectedAsync()
    {
        WeeklyPlanItem? item = SelectedItem;

        if (item is null ||
            IsBusy ||
            !_dialogService.ConfirmRecordDeletion(
                "安排",
                item.Title))
        {
            return false;
        }

        IsBusy = true;

        try
        {
            await _service.DeleteAsync(item.Id);
            await ReloadItemsAsync();
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "删除选中的每周计划失败。",
                exception);
            StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RestoreInteraction(
        WeeklyPlanItem item,
        WeeklyDay originalDay,
        int originalStartMinutes,
        int originalEndMinutes)
    {
        item.Day = originalDay;
        item.SetTimeRange(
            originalStartMinutes,
            originalEndMinutes);
        RefreshInteractionLayout();
    }

    private async Task ReloadItemsAsync(
        string? selectedItemId = null)
    {
        IReadOnlyList<WeeklyPlanItem> items =
            await _service.GetAllAsync();

        Cards.Clear();

        foreach (WeeklyPlanItem item in items)
        {
            WeeklyPlanCardViewModel card =
                new(item)
                {
                    IsSelected =
                        item.Id == selectedItemId
                };

            Cards.Add(card);
        }

        OnPropertyChanged(nameof(SelectedItem));

        HasPlans = Cards.Count > 0;

        double width =
            _currentSnapshot?.CanvasWidth ?? 0;
        double height =
            _currentSnapshot?.CanvasHeight ?? 0;

        RecalculateAdaptiveLayout(width, height);
    }

    private void RecalculateAdaptiveLayout(
        double canvasWidth,
        double canvasHeight)
    {
        WeeklyPlanDisplayRange range =
            WeeklyPlanLayoutEngine.CalculateDisplayRange(
                Cards.Select(card => card.Item));

        ApplyLayout(
            WeeklyPlanLayoutEngine.CreateSnapshot(
                range,
                canvasWidth,
                canvasHeight));
    }

    private void ApplyLayout(
        WeeklyPlanLayoutSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        DisplayStartMinutes = snapshot.Range.StartMinutes;
        DisplayEndMinutes = snapshot.Range.EndMinutes;
        PixelsPerMinute = snapshot.PixelsPerMinute;

        GridLines.Clear();

        foreach (WeeklyPlanGridLine line in
                 WeeklyPlanLayoutEngine.CreateGridLines(snapshot))
        {
            GridLines.Add(line);
        }

        ApplyCardPlacements(snapshot);
    }

    private void ApplyCardPlacements(
        WeeklyPlanLayoutSnapshot snapshot)
    {
        Dictionary<WeeklyPlanItem, WeeklyPlanCardPlacement>
            placements =
                WeeklyPlanLayoutEngine.CalculateCardPlacements(
                        Cards.Select(card => card.Item),
                        snapshot)
                    .ToDictionary(
                        placement => placement.Item);

        foreach (WeeklyPlanCardViewModel card in Cards)
        {
            if (placements.TryGetValue(
                    card.Item,
                    out WeeklyPlanCardPlacement? placement))
            {
                card.Apply(placement);
            }
        }
    }

    private int FindSuggestedStart(
        WeeklyDay day)
    {
        int candidate = 9 * 60;

        foreach (WeeklyPlanItem item in
                 Cards.Select(card => card.Item)
                     .Where(item => item.Day == day)
                     .OrderBy(item => item.StartMinutes))
        {
            if (candidate + 60 <= item.StartMinutes)
            {
                return candidate;
            }

            if (candidate < item.EndMinutes)
            {
                candidate =
                    WeeklyPlanLayoutEngine.SnapMinutes(
                        item.EndMinutes);
            }
        }

        return candidate <= 1440 - 60
            ? candidate
            : 0;
    }

    private static WeeklyTimeOption CreateTimeOption(
        int minutes) =>
        new(
            minutes,
            WeeklyPlanLayoutEngine.FormatMinutes(minutes));
}

public sealed record WeeklyPlanDayHeader(
    WeeklyDay Day,
    string Title);

public sealed record WeeklyDayOption(
    WeeklyDay Value,
    string Title);

public sealed record WeeklyTimeOption(
    int Minutes,
    string Title);

public sealed record WeeklyColorOption(
    WeeklyPlanColor Value,
    string Title);
