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
/// “成长记录”年度时间轴、直接编辑抽屉和自定义分类的状态。
/// </summary>
public partial class AchievementViewModel : ObservableObject
{
    private const double DefaultTimelineWidth = 960;

    private readonly AchievementService _service;
    private readonly AchievementCategoryService _categoryService;
    private readonly DialogService _dialogService;
    private readonly IClock _clock;
    private readonly ILocalTimeService _localTimeService;
    private IReadOnlyList<AchievementRecord> _allRecords = [];
    private DateTimeOffset _editorCreatedAt;
    private double _timelineViewportWidth = DefaultTimelineWidth;
    private int _selectedYearRecordCount;
    private AchievementEditorSnapshot _editorSnapshot =
        new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            null);
    private bool _isPopulatingEditor;
    private bool _isClosingEditor;
    private IReadOnlyList<string>? _timelineInteractionLaneOrder;
    private string? _activeTimelineInteractionId;
    private string? _recentlyAdjustedRecordId;

    public ObservableCollection<AchievementTimelineCardViewModel>
        TimelineItems
    { get; } = [];

    public ObservableCollection<AchievementTimelineMonth> Months
    { get; } = [];

    public ObservableCollection<AchievementCategoryDefinition> Categories
    { get; } = [];

    public ObservableCollection<AchievementCategoryFilterOption> FilterOptions
    { get; } = [];

    public IReadOnlyList<AchievementColorOption> CategoryColorOptions
    { get; } =
    [
        new("蓝色", "#4F6BED"),
        new("紫色", "#7357E6"),
        new("绿色", "#35A77B"),
        new("深蓝", "#4B6B9A"),
        new("浅蓝", "#3B82F6"),
        new("橙色", "#E58A45"),
        new("玫红", "#D05A8A"),
        new("灰色", "#7C8598")
    ];

    [ObservableProperty]
    private int selectedYear;

    [ObservableProperty]
    private AchievementCategoryFilterOption? selectedFilter;

    [ObservableProperty]
    private AchievementRecord? selectedRecord;

    [ObservableProperty]
    private double timelineContentHeight =
        AchievementTimelineLayoutEngine.MinimumContentHeight;

    [ObservableProperty]
    private double timelineWidth =
        DefaultTimelineWidth *
        AchievementTimelineLayoutEngine.WindowYearCount;

    public DateTime TimelineRangeStart
    { get; private set; } = new(DateTime.Today.Year - 1, 1, 1);

    public DateTime TimelineRangeEndExclusive
    { get; private set; } = new(DateTime.Today.Year + 2, 1, 1);

    public DateTime TimelineRangeEnd =>
        TimelineRangeEndExclusive.AddDays(-1);

    public double SelectedYearScrollOffset
    { get; private set; } = DefaultTimelineWidth;

    [ObservableProperty]
    private string editorId = string.Empty;

    [ObservableProperty]
    private string editorTitle = string.Empty;

    [ObservableProperty]
    private string editorDetails = string.Empty;

    [ObservableProperty]
    private AchievementCategoryDefinition? editorCategory;

    [ObservableProperty]
    private DateTime? editorStartedOn;

    [ObservableProperty]
    private DateTime? editorCompletedOn;

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
    private bool isCategoryManagerVisible;

    [ObservableProperty]
    private AchievementCategoryDefinition? selectedManagedCategory;

    [ObservableProperty]
    private bool isCreatingCategory;

    [ObservableProperty]
    private string categoryDraftName = string.Empty;

    [ObservableProperty]
    private string categoryDraftColor = "#4F6BED";

    [ObservableProperty]
    private string categoryManagerError = string.Empty;

    public bool HasTimelineItems => TimelineItems.Count > 0;
    public bool IsTimelineEmpty => !HasTimelineItems;
    public bool HasEditorError =>
        !string.IsNullOrWhiteSpace(EditorErrorMessage);
    public bool HasStatusMessage =>
        !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasCategoryManagerError =>
        !string.IsNullOrWhiteSpace(CategoryManagerError);
    public bool CanDeleteManagedCategory =>
        !IsCreatingCategory &&
        SelectedManagedCategory?.CanDelete == true;
    public string SelectedYearText => $"{SelectedYear}年";
    public string YearSummaryText =>
        _selectedYearRecordCount == 0
            ? $"{SelectedYear}年 · 尚无成果"
            : $"{SelectedYear}年 · {_selectedYearRecordCount} 项成果";
    public string EditorHeading =>
        IsEditingExisting ? "成果详情" : "记录新成果";
    public string EditorHint =>
        IsEditingExisting
            ? "内容可直接修改，关闭时自动保存"
            : "记录已经取得的成果及其时间跨度";

    private DateTime LocalToday =>
        _localTimeService.ToLocalDateTime(_clock.UtcNow).Date;

    public AchievementViewModel(
        AchievementService service,
        AchievementCategoryService categoryService,
        DialogService dialogService,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
    {
        _service = service;
        _categoryService = categoryService;
        _dialogService = dialogService;
        _clock = clock ?? SystemClock.Instance;
        _localTimeService =
            localTimeService ?? LocalTimeService.System;

        FilterOptions.Add(new("全部分类", null));
        SelectedFilter = FilterOptions[0];
        SelectedYear = LocalToday.Year;
    }

    partial void OnSelectedYearChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedYearText));
        RebuildTimeline();
    }

    partial void OnSelectedFilterChanged(
        AchievementCategoryFilterOption? value) =>
        RebuildTimeline();

    partial void OnIsEditingExistingChanged(bool value)
    {
        OnPropertyChanged(nameof(EditorHeading));
        OnPropertyChanged(nameof(EditorHint));
    }

    partial void OnEditorTitleChanged(string value) =>
        MarkEditorChanged();
    partial void OnEditorDetailsChanged(string value) =>
        MarkEditorChanged();
    partial void OnEditorCategoryChanged(
        AchievementCategoryDefinition? value) =>
        MarkEditorChanged();
    partial void OnEditorStartedOnChanged(DateTime? value) =>
        MarkEditorChanged();
    partial void OnEditorCompletedOnChanged(DateTime? value) =>
        MarkEditorChanged();

    partial void OnEditorErrorMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasEditorError));
    partial void OnStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));
    partial void OnCategoryManagerErrorChanged(string value) =>
        OnPropertyChanged(nameof(HasCategoryManagerError));

    partial void OnSelectedManagedCategoryChanged(
        AchievementCategoryDefinition? value)
    {
        if (value is null || IsCreatingCategory)
        {
            return;
        }

        CategoryDraftName = value.Name;
        CategoryDraftColor = value.ColorHex;
        CategoryManagerError = string.Empty;
        OnPropertyChanged(nameof(CanDeleteManagedCategory));
    }

    partial void OnIsCreatingCategoryChanged(bool value) =>
        OnPropertyChanged(nameof(CanDeleteManagedCategory));

    public void UpdateTimelineViewport(double width)
    {
        if (width < 240 ||
            Math.Abs(width - _timelineViewportWidth) < 0.5)
        {
            return;
        }

        _timelineViewportWidth = width;
        RebuildTimeline();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            await ReloadCategoriesAsync();
            await ReloadRecordsAsync(SelectedRecord?.Id);
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            AppLog.Error("加载成长记录失败。", exception);
            StatusMessage = $"加载失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PreviousYear()
    {
        if (SelectedYear > DateTime.MinValue.Year + 1)
        {
            SelectedYear--;
        }
    }

    [RelayCommand]
    private void NextYear()
    {
        if (SelectedYear < DateTime.MaxValue.Year - 2)
        {
            SelectedYear++;
        }
    }

    [RelayCommand]
    private void OpenDetails(AchievementTimelineCardViewModel? item)
    {
        if (item is not null)
        {
            BeginEdit(item.Record);
        }
    }

    [RelayCommand]
    private void StartCreate()
    {
        DateTime defaultDate =
            SelectedYear == LocalToday.Year
                ? LocalToday
                : new DateTime(SelectedYear, 1, 1);
        string defaultCategoryId =
            FindCategory(SelectedFilter?.CategoryId)?.Id ??
            AchievementCategoryDefinition.OtherCategoryId;

        PopulateEditor(
            id: string.Empty,
            title: string.Empty,
            details: string.Empty,
            categoryId: defaultCategoryId,
            start: defaultDate,
            end: defaultDate,
            createdAt: default,
            editingExisting: false);
    }

    private void BeginEdit(AchievementRecord record)
    {
        SelectedRecord = record;
        PopulateEditor(
            record.Id,
            record.Title,
            record.Details,
            record.CategoryId,
            record.PeriodStart,
            record.CompletedDate,
            record.CreatedAt,
            editingExisting: true);
    }

    private void PopulateEditor(
        string id,
        string title,
        string details,
        string categoryId,
        DateTime start,
        DateTime end,
        DateTimeOffset createdAt,
        bool editingExisting)
    {
        _isPopulatingEditor = true;

        try
        {
            EditorId = id;
            EditorTitle = title;
            EditorDetails = details;
            EditorCategory =
                FindCategory(categoryId) ??
                FindCategory(
                    AchievementCategoryDefinition.OtherCategoryId) ??
                Categories.FirstOrDefault();
            EditorStartedOn = start;
            EditorCompletedOn = end;
            EditorErrorMessage = string.Empty;
            _editorCreatedAt = createdAt;
            IsEditingExisting = editingExisting;
            IsCategoryManagerVisible = false;
            IsEditorVisible = true;
            _editorSnapshot = CaptureEditorSnapshot();
        }
        finally
        {
            _isPopulatingEditor = false;
        }
    }

    [RelayCommand]
    private Task CloseEditorAsync() =>
        RequestCloseEditorAsync();

    /// <summary>
    /// 点击遮罩、切换到其他程序或按 Escape 时统一走这里。
    /// 已有成果自动保存；空白的新成果直接关闭。
    /// </summary>
    public async Task<bool> RequestCloseEditorAsync()
    {
        if (!IsEditorVisible || _isClosingEditor)
        {
            return true;
        }

        _isClosingEditor = true;

        try
        {
            if (!IsEditingExisting &&
                string.IsNullOrWhiteSpace(EditorTitle))
            {
                IsEditorVisible = false;
                EditorErrorMessage = string.Empty;
                return true;
            }

            if (CaptureEditorSnapshot() != _editorSnapshot)
            {
                return await SaveEditorAsync(closeAfterSave: true);
            }

            IsEditorVisible = false;
            EditorErrorMessage = string.Empty;
            return true;
        }
        finally
        {
            _isClosingEditor = false;
        }
    }

    public async Task HandleHostWindowDeactivatedAsync()
    {
        if (IsCategoryManagerVisible)
        {
            IsCategoryManagerVisible = false;
        }

        await RequestCloseEditorAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await SaveEditorAsync(closeAfterSave: true);
    }

    private async Task<bool> SaveEditorAsync(bool closeAfterSave)
    {
        if (IsBusy)
        {
            return false;
        }

        if (!EditorStartedOn.HasValue)
        {
            EditorErrorMessage = "请选择成果的开始日期。";
            return false;
        }

        if (!EditorCompletedOn.HasValue)
        {
            EditorErrorMessage = "请选择成果的完成日期。";
            return false;
        }

        AchievementCategoryDefinition? category =
            Categories.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    EditorCategory?.Id,
                    StringComparison.OrdinalIgnoreCase));

        if (category is null)
        {
            EditorErrorMessage = "请选择有效的成果分类。";
            return false;
        }

        IsBusy = true;
        EditorErrorMessage = string.Empty;

        try
        {
            AchievementRecord saved =
                await _service.SaveAsync(
                    new AchievementRecord
                    {
                        Id = EditorId,
                        Title = EditorTitle,
                        Details = EditorDetails,
                        Category = GetLegacyCategory(category.Id),
                        CategoryId = category.Id,
                        CategoryName = category.Name,
                        CategoryColor = category.ColorHex,
                        PeriodStart = EditorStartedOn.Value,
                        PeriodEnd = EditorCompletedOn.Value,
                        CompletedOn = EditorCompletedOn.Value,
                        Cycle = AchievementCycle.OneTime,
                        Status = AchievementStatus.Completed,
                        ProgressPercent = 100,
                        CreatedAt = _editorCreatedAt
                    });

            await ReloadRecordsAsync(saved.Id);
            SelectedRecord = _allRecords.FirstOrDefault(record =>
                record.Id == saved.Id);
            EditorId = saved.Id;
            _editorCreatedAt = saved.CreatedAt;
            IsEditingExisting = true;
            _editorSnapshot = CaptureEditorSnapshot();

            if (closeAfterSave)
            {
                IsEditorVisible = false;
            }

            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("保存成长记录失败。", exception);
            EditorErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(AchievementRecord? record)
    {
        record ??= SelectedRecord;

        if (record is null ||
            IsBusy ||
            !_dialogService.ConfirmRecordDeletion(
                "成长记录",
                record.Title))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _service.DeleteAsync(record.Id);
            IsEditorVisible = false;
            SelectedRecord = null;
            await ReloadRecordsAsync();
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            AppLog.Error("删除成长记录失败。", exception);
            EditorErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void PreviewDateRange(
        AchievementRecord record,
        DateTime start,
        DateTime end)
    {
        record.PeriodStart = start.Date;
        record.PeriodEnd = end.Date;
        record.CompletedOn = end.Date;
        RebuildTimeline();
    }

    /// <summary>
    /// 拖拽开始时冻结整条时间轴的轨道顺序。
    /// 日期可以实时预览，但卡片在松开前不会因重新排序而上下跳动。
    /// </summary>
    public void BeginTimelineInteraction(
        AchievementRecord record)
    {
        _timelineInteractionLaneOrder = TimelineItems
            .OrderBy(item => item.Top)
            .Select(item => item.Record.Id)
            .ToArray();
        _activeTimelineInteractionId = record.Id;
        _recentlyAdjustedRecordId = null;
        UpdateTimelineInteractionStates();
    }

    public void CancelTimelineInteraction(
        AchievementRecord record,
        DateTime originalStart,
        DateTime originalEnd)
    {
        record.PeriodStart = originalStart.Date;
        record.PeriodEnd = originalEnd.Date;
        record.CompletedOn = originalEnd.Date;
        EndTimelineInteraction(
            record.Id,
            markAsAdjusted: false,
            rebuildTimeline: true);
    }

    public void EndTimelineInteractionWithoutChanges(
        string recordId) =>
        EndTimelineInteraction(
            recordId,
            markAsAdjusted: false,
            rebuildTimeline: false);

    public void ClearRecentTimelineHighlight()
    {
        if (_activeTimelineInteractionId is not null ||
            _recentlyAdjustedRecordId is null)
        {
            return;
        }

        _recentlyAdjustedRecordId = null;
        UpdateTimelineInteractionStates();
    }

    public AchievementTimelineCardViewModel?
        FindTimelineItem(string recordId) =>
        TimelineItems.FirstOrDefault(item =>
            item.Record.Id == recordId);

    public async Task<bool> CommitTimelineInteractionAsync(
        AchievementRecord record,
        DateTime originalStart,
        DateTime originalEnd)
    {
        if (record.PeriodStart.Date == originalStart.Date &&
            record.CompletedDate == originalEnd.Date)
        {
            EndTimelineInteractionWithoutChanges(record.Id);
            return false;
        }

        // 只有鼠标松开后才解除轨道冻结并按新日期重新排序。
        EndTimelineInteraction(
            record.Id,
            markAsAdjusted: true,
            rebuildTimeline: true);
        IsBusy = true;

        try
        {
            await _service.SaveAsync(record);
            await ReloadRecordsAsync(record.Id);
            StatusMessage = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            record.PeriodStart = originalStart;
            record.PeriodEnd = originalEnd;
            record.CompletedOn = originalEnd;
            _recentlyAdjustedRecordId = null;
            RebuildTimeline();
            StatusMessage = $"调整时间失败：{exception.Message}";
            AppLog.Error("拖动调整成果时间失败。", exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void EndTimelineInteraction(
        string recordId,
        bool markAsAdjusted,
        bool rebuildTimeline)
    {
        _activeTimelineInteractionId = null;
        _timelineInteractionLaneOrder = null;
        _recentlyAdjustedRecordId = markAsAdjusted
            ? recordId
            : null;

        if (rebuildTimeline)
        {
            RebuildTimeline();
        }
        else
        {
            UpdateTimelineInteractionStates();
        }
    }

    private void UpdateTimelineInteractionStates()
    {
        foreach (AchievementTimelineCardViewModel item in TimelineItems)
        {
            item.SetInteractionState(
                item.Record.Id == _activeTimelineInteractionId,
                item.Record.Id == _recentlyAdjustedRecordId);
        }
    }

    [RelayCommand]
    private void OpenCategoryManager()
    {
        IsEditorVisible = false;
        IsCreatingCategory = false;
        IsCategoryManagerVisible = true;
        SelectedManagedCategory = Categories.FirstOrDefault();

        if (SelectedManagedCategory is not null)
        {
            CategoryDraftName = SelectedManagedCategory.Name;
            CategoryDraftColor = SelectedManagedCategory.ColorHex;
        }

        CategoryManagerError = string.Empty;
    }

    [RelayCommand]
    private void CloseCategoryManager()
    {
        IsCategoryManagerVisible = false;
        CategoryManagerError = string.Empty;
    }

    [RelayCommand]
    private void StartCreateCategory()
    {
        IsCreatingCategory = true;
        SelectedManagedCategory = null;
        CategoryDraftName = string.Empty;
        CategoryDraftColor = "#4F6BED";
        CategoryManagerError = string.Empty;
    }

    [RelayCommand]
    private void SelectCategoryColor(AchievementColorOption? option)
    {
        if (option is not null)
        {
            CategoryDraftColor = option.ColorHex;
        }
    }

    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CategoryManagerError = string.Empty;

        try
        {
            AchievementCategoryDefinition draft =
                new()
                {
                    Id = IsCreatingCategory
                        ? string.Empty
                        : SelectedManagedCategory?.Id ?? string.Empty,
                    Name = CategoryDraftName,
                    ColorHex = CategoryDraftColor,
                    SortOrder = IsCreatingCategory
                        ? 0
                        : SelectedManagedCategory?.SortOrder ?? 0,
                    IsBuiltIn =
                        SelectedManagedCategory?.IsBuiltIn == true
                };

            AchievementCategoryDefinition saved =
                await _categoryService.SaveAsync(draft);
            await ReloadCategoriesAsync(saved.Id);
            await ReloadRecordsAsync(SelectedRecord?.Id);
            IsCreatingCategory = false;
            SelectedManagedCategory = Categories.FirstOrDefault(item =>
                item.Id == saved.Id);
            EditorCategory = FindCategory(saved.Id);
        }
        catch (Exception exception)
        {
            CategoryManagerError = exception.Message;
            AppLog.Error("保存成果分类失败。", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        AchievementCategoryDefinition? category =
            SelectedManagedCategory;

        if (category is null || IsBusy)
        {
            return;
        }

        if (!category.CanDelete)
        {
            CategoryManagerError =
                "“其他”是未分类成果的保底分类，不能删除。";
            return;
        }

        if (!_dialogService.ConfirmRecordDeletion(
                "成果分类",
                category.Name))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _categoryService.DeleteAsync(category);
            await ReloadCategoriesAsync();
            await ReloadRecordsAsync(SelectedRecord?.Id);
            SelectedManagedCategory = Categories.FirstOrDefault();
            IsCreatingCategory = false;
        }
        catch (Exception exception)
        {
            CategoryManagerError = exception.Message;
            AppLog.Error("删除成果分类失败。", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 按分类管理列表中的插入位置调整顺序。
    /// insertionIndex 使用拖拽前列表的“间隙”索引，范围为 0..Count。
    /// </summary>
    public async Task ReorderCategoryAsync(
        string categoryId,
        int insertionIndex)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(categoryId))
        {
            return;
        }

        AchievementCategoryDefinition? sourceCategory =
            FindCategory(categoryId);
        int sourceIndex = sourceCategory is null
            ? -1
            : Categories.IndexOf(sourceCategory);

        if (sourceIndex < 0 ||
            sourceIndex >= Categories.Count)
        {
            return;
        }

        int boundedInsertionIndex = Math.Clamp(
            insertionIndex,
            0,
            Categories.Count);
        int targetIndex = boundedInsertionIndex > sourceIndex
            ? boundedInsertionIndex - 1
            : boundedInsertionIndex;
        targetIndex = Math.Clamp(
            targetIndex,
            0,
            Categories.Count - 1);

        if (targetIndex == sourceIndex)
        {
            return;
        }

        List<AchievementCategoryDefinition> reordered =
            Categories.ToList();
        AchievementCategoryDefinition moved =
            reordered[sourceIndex];
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(targetIndex, moved);

        IsBusy = true;
        CategoryManagerError = string.Empty;

        try
        {
            await _categoryService.ReorderAsync(
                reordered.Select(category => category.Id).ToArray());
            await ReloadCategoriesAsync(moved.Id);
        }
        catch (Exception exception)
        {
            CategoryManagerError = exception.Message;
            AppLog.Error("调整成果分类顺序失败。", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void MarkEditorChanged()
    {
        if (!_isPopulatingEditor)
        {
            EditorErrorMessage = string.Empty;
        }
    }

    private AchievementEditorSnapshot CaptureEditorSnapshot() =>
        new(
            EditorId,
            EditorTitle,
            EditorDetails,
            EditorCategory?.Id ?? string.Empty,
            EditorStartedOn?.Date,
            EditorCompletedOn?.Date);

    private async Task ReloadCategoriesAsync(
        string? preferredManagedCategoryId = null)
    {
        string? selectedFilterId = SelectedFilter?.CategoryId;
        string? managedId =
            preferredManagedCategoryId ??
            SelectedManagedCategory?.Id;
        string? editorCategoryId =
            EditorCategory?.Id;
        IReadOnlyList<AchievementCategoryDefinition> loaded =
            await _categoryService.GetAllAsync();

        Categories.Clear();
        foreach (AchievementCategoryDefinition category in loaded)
        {
            Categories.Add(category);
        }

        FilterOptions.Clear();
        FilterOptions.Add(new("全部分类", null));
        foreach (AchievementCategoryDefinition category in Categories)
        {
            FilterOptions.Add(
                new AchievementCategoryFilterOption(
                    category.Name,
                    category.Id));
        }

        SelectedFilter =
            FilterOptions.FirstOrDefault(option =>
                option.CategoryId == selectedFilterId) ??
            FilterOptions[0];
        SelectedManagedCategory =
            Categories.FirstOrDefault(category =>
                category.Id == managedId) ??
            Categories.FirstOrDefault();
        EditorCategory =
            FindCategory(editorCategoryId) ??
            FindCategory(
                AchievementCategoryDefinition.OtherCategoryId) ??
            Categories.FirstOrDefault();
    }

    private AchievementCategoryDefinition? FindCategory(
        string? categoryId) =>
        Categories.FirstOrDefault(category =>
            string.Equals(
                category.Id,
                categoryId,
                StringComparison.OrdinalIgnoreCase));

    private async Task ReloadRecordsAsync(
        string? preferredSelectionId = null)
    {
        _allRecords = await _service.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(preferredSelectionId))
        {
            SelectedRecord = _allRecords.FirstOrDefault(record =>
                record.Id == preferredSelectionId);
        }

        RebuildTimeline();
    }

    private void RebuildTimeline()
    {
        if (SelectedYear < DateTime.MinValue.Year ||
            SelectedYear >= DateTime.MaxValue.Year)
        {
            return;
        }

        IReadOnlyList<AchievementRecord> filtered =
            string.IsNullOrWhiteSpace(SelectedFilter?.CategoryId)
                ? _allRecords.ToArray()
                : _allRecords.Where(record =>
                    record.CategoryId == SelectedFilter!.CategoryId)
                    .ToArray();

        DateTime selectedYearStart =
            new(SelectedYear, 1, 1);
        DateTime selectedYearEnd =
            selectedYearStart.AddYears(1);
        _selectedYearRecordCount = filtered.Count(record =>
            record.CompletedDate >= selectedYearStart &&
            record.PeriodStart.Date < selectedYearEnd);

        AchievementTimelineLayout layout =
            AchievementTimelineLayoutEngine.BuildWindow(
                filtered,
                SelectedYear,
                _timelineViewportWidth);

        IReadOnlyList<AchievementTimelinePlacement> placements =
            ApplyFrozenLaneOrder(layout.Placements);

        TimelineItems.Clear();
        foreach (AchievementTimelinePlacement placement in placements)
        {
            AchievementTimelineCardViewModel item =
                new(placement);
            item.SetInteractionState(
                item.Record.Id == _activeTimelineInteractionId,
                item.Record.Id == _recentlyAdjustedRecordId);
            TimelineItems.Add(item);
        }

        Months.Clear();
        foreach (AchievementTimelineMonth month in layout.Months)
        {
            Months.Add(month);
        }

        TimelineWidth = layout.TimelineWidth;
        TimelineContentHeight = layout.ContentHeight;
        TimelineRangeStart = layout.RangeStart;
        TimelineRangeEndExclusive = layout.RangeEndExclusive;
        SelectedYearScrollOffset = layout.SelectedYearLeft;
        OnPropertyChanged(nameof(TimelineRangeStart));
        OnPropertyChanged(nameof(TimelineRangeEndExclusive));
        OnPropertyChanged(nameof(TimelineRangeEnd));
        OnPropertyChanged(nameof(SelectedYearScrollOffset));
        OnPropertyChanged(nameof(HasTimelineItems));
        OnPropertyChanged(nameof(IsTimelineEmpty));
        OnPropertyChanged(nameof(YearSummaryText));
    }

    private IReadOnlyList<AchievementTimelinePlacement>
        ApplyFrozenLaneOrder(
            IReadOnlyList<AchievementTimelinePlacement> placements)
    {
        if (_timelineInteractionLaneOrder is null)
        {
            return placements;
        }

        Dictionary<string, int> laneByRecordId =
            _timelineInteractionLaneOrder
                .Select((recordId, lane) => (recordId, lane))
                .ToDictionary(item => item.recordId, item => item.lane);

        return placements
            .OrderBy(placement =>
                laneByRecordId.TryGetValue(
                    placement.Record.Id,
                    out int lane)
                    ? lane
                    : int.MaxValue)
            .ThenBy(placement => placement.TrackIndex)
            .Select((placement, lane) => placement with
            {
                TrackIndex = lane,
                Top = AchievementTimelineLayoutEngine.ContentTop +
                    lane * AchievementTimelineLayoutEngine.LaneHeight
            })
            .ToArray();
    }

    private static AchievementCategory GetLegacyCategory(string categoryId) =>
        categoryId switch
        {
            "builtin-skill" => AchievementCategory.Skill,
            "builtin-project" => AchievementCategory.Project,
            "builtin-learning" => AchievementCategory.Learning,
            "builtin-work" => AchievementCategory.Work,
            "builtin-life" => AchievementCategory.Life,
            "builtin-health" => AchievementCategory.Health,
            "builtin-breakthrough" => AchievementCategory.Breakthrough,
            _ => AchievementCategory.Other
        };

    private sealed record AchievementEditorSnapshot(
        string Id,
        string Title,
        string Details,
        string CategoryId,
        DateTime? Start,
        DateTime? End);
}

public sealed partial class AchievementTimelineCardViewModel : ObservableObject
{
    public AchievementRecord Record { get; }
    public double CanvasWidth { get; }
    public double Left { get; }
    public double Width { get; }
    public double EndPoint => Left + Width;
    public double CardLeft { get; }
    public double CardWidth { get; }
    public double PointerLeft { get; }
    public double PointerOffset => PointerLeft - CardLeft;
    public double TrackTop { get; }
    public bool IsSingleDay { get; }
    public bool IsCompactSpan => !IsSingleDay && Width < 16;
    public double StartHandleLeft => Math.Max(0, Left - 7);
    public double StartHandleLineOffset => Left - StartHandleLeft;
    public double EndHandleLeft => Math.Clamp(
        EndPoint - 7,
        0,
        Math.Max(0, CanvasWidth - 14));
    public double EndHandleLineOffset => EndPoint - EndHandleLeft;
    public double MoveHitLeft => Math.Max(0, Left - 5);
    public double MoveHitWidth => Math.Min(
        CanvasWidth - MoveHitLeft,
        Math.Max(10, Width + 10));
    public double NodeTop => TrackTop - 3.5;
    public double SingleNodeLeft => Left - 6;
    public double SingleNodeTop => TrackTop - 4.5;
    public double StartNodeLeft => Left - 5;
    public double EndNodeLeft => EndPoint - 5;
    public double StartNodeOffset =>
        StartNodeLeft - StartHandleLeft;
    public double EndNodeOffset =>
        EndNodeLeft - EndHandleLeft;
    public double HandleHeight => IsCompactSpan ? 10 : 22;
    public double StartHandleTop => TrackTop - 9;
    public double EndHandleTop => IsCompactSpan
        ? TrackTop + 2
        : TrackTop - 9;
    public double StartNodeTopOffset =>
        NodeTop - StartHandleTop;
    public double EndNodeTopOffset =>
        NodeTop - EndHandleTop;
    public double SingleStartHandleLeft =>
        Math.Max(0, Left - 12);
    public double SingleEndHandleLeft =>
        Math.Min(
            Math.Max(0, CanvasWidth - 10),
            Left + 2);
    public double Top { get; }
    public double Height => AchievementTimelineLayoutEngine.LaneHeight;

    [ObservableProperty]
    private bool isInteractionActive;

    [ObservableProperty]
    private bool isRecentlyAdjusted;
    public string Title => Record.Title;
    public string CategoryText => Record.CategoryText;
    public string CategoryColor => Record.CategoryColorHex;
    public string CategorySoftColor => Record.CategorySoftColorHex;
    public string StartText { get; }
    public string EndText { get; }
    public string TimelineDateText => IsSingleDay
        ? StartText
        : $"{StartText}  →  {EndText}";
    public AchievementTimelineCardViewModel(
        AchievementTimelinePlacement placement)
    {
        Record = placement.Record;
        CanvasWidth = placement.CanvasWidth;
        Left = placement.Left;
        Width = placement.Width;
        CardLeft = placement.CardLeft;
        CardWidth = placement.CardWidth;
        PointerLeft = placement.PointerLeft;
        TrackTop = placement.TrackTop;
        IsSingleDay = placement.IsSingleDay;
        Top = placement.Top;
        StartText = Record.PeriodStart.ToString("yyyy.MM.dd");
        EndText = Record.CompletedDate.ToString("yyyy.MM.dd");
    }

    public void SetInteractionState(
        bool interactionActive,
        bool recentlyAdjusted)
    {
        IsInteractionActive = interactionActive;
        IsRecentlyAdjusted = recentlyAdjusted;
    }
}

public sealed record AchievementCategoryFilterOption(
    string Title,
    string? CategoryId);

public sealed record AchievementColorOption(
    string Name,
    string ColorHex);
