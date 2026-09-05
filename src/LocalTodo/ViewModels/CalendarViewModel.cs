using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 月视图日历页面。
///
/// 负责：
/// 1. 生成 6 行 × 7 列的月视图；
/// 2. 读取未完成和已完成任务并按截止日期分组；
/// 3. 在指定日期快速新增任务；
/// 4. 打开任务详情并通过独立编辑缓冲区自动保存。
/// </summary>
public partial class CalendarViewModel :
    ObservableObject,
    IPendingChanges
{
    private readonly TaskService
        _taskService;

    private readonly LunarCalendarService
        _lunarCalendarService;

    private readonly DialogService
        _dialogService;

    private readonly IClock
        _clock;

    private readonly ILocalTimeService
        _localTimeService;

    private readonly TaskEditorAutoSaveCoordinator
        _taskEditorAutoSave;

    private readonly SemaphoreSlim
        _taskEditorSaveGate =
            new(1, 1);

    /// <summary>
    /// 当前是否还有编辑内容等待自动保存。
    /// </summary>
    private bool _hasPendingTaskEditorSave
    {
        get =>
            _taskEditorAutoSave.HasPendingSave;

        set =>
            _taskEditorAutoSave.HasPendingSave =
                value;
    }

    /// <summary>
    /// 当前编辑器最近一次确认过的数据库版本。
    /// </summary>
    private TaskEditBaseline?
        _taskEditorBaseline;

    /// <summary>
    /// 当前编辑器中尚未成功保存的字段组。
    /// </summary>
    private TaskEditFields _taskEditorDirtyFields
    {
        get =>
            _taskEditorAutoSave.DirtyFields;

        set =>
            _taskEditorAutoSave.DirtyFields =
                value;
    }

    /// <summary>
    /// 当前是否存在需要用户确认后重试的同字段冲突。
    /// </summary>
    private bool _taskEditorHasConflict
    {
        get =>
            _taskEditorAutoSave.HasConflict;

        set =>
            _taskEditorAutoSave.HasConflict =
                value;
    }

    /// <summary>
    /// 当前是否正在由 Calendar 自己保存任务详情。
    ///
    /// TaskService 保存成功会触发 TasksChanged。
    /// MainWindowViewModel 使用这个标记判断：
    /// 本次变化是否已经由 Calendar 自己处理，
    /// 从而避免立即重建整个日历。
    /// </summary>
    private bool
        _isSavingTaskEditorLocally;

    private bool
        _isLoadingTaskEditor;

    private long
        _loadVersion;

    private DateTime
        _displayMonth;

    /// <summary>
    /// 月视图固定显示 6 行 × 7 列，共 42 天。
    /// </summary>
    public ObservableCollection<CalendarDayItem>
        Days
    { get; } = [];

    /// <summary>
    /// 日历快速新增和任务详情共同使用的象限选项。
    /// </summary>
    public IReadOnlyList<TaskQuadrantOption>
        QuadrantOptions
    { get; } =
        TaskEditorOptionCatalog
            .Quadrants;

    /// <summary>
    /// 日历快速新增和任务详情共同使用的截止时间选项。
    /// 第一项“无”表示只有截止日期，没有具体时分。
    /// </summary>
    public IReadOnlyList<TaskDueTimeOption>
        DueTimeOptions
    { get; } =
        TaskEditorOptionCatalog
            .DueTimes;

    /// <summary>
    /// 日历快速新增和任务详情共同使用的提醒选项。
    /// 与“所有任务”新增/详情保持一致。
    /// </summary>
    public IReadOnlyList<TaskReminderOption>
        ReminderOptions
    { get; } =
        TaskEditorOptionCatalog
            .Reminders;

    /// <summary>
    /// 日历快速新增和任务详情共同使用的循环选项。
    /// </summary>
    public IReadOnlyList<TaskRepeatOption>
        RepeatOptions
    { get; } =
        TaskEditorOptionCatalog
            .Repeats;

    /// <summary>
    /// 当前正在查看的月份。
    /// 始终保存为该月第一天。
    /// </summary>
    public DateTime DisplayMonth
    {
        get =>
            _displayMonth;

        private set
        {
            DateTime normalizedValue =
                new(
                    value.Year,
                    value.Month,
                    1);

            if (!SetProperty(
                    ref _displayMonth,
                    normalizedValue))
            {
                return;
            }

            OnPropertyChanged(
                nameof(MonthTitle));
        }
    }

    public string MonthTitle =>
        DisplayMonth.ToString(
            "yyyy年M月",
            CultureInfo
                .GetCultureInfo(
                    "zh-CN"));

    [ObservableProperty]
    private bool isBusy;

    /*
     * 状态文字仍保留给错误处理和调试使用，
     * 但 CalendarView.xaml 不再在日历底部显示它。
     */
    [ObservableProperty]
    private string statusMessage =
        "正在准备日历";

    #region 快速新增

    [ObservableProperty]
    private string quickAddTitle =
        string.Empty;

    [ObservableProperty]
    private string quickAddDescription =
        string.Empty;

    [ObservableProperty]
    private bool quickAddIsImportant;

    [ObservableProperty]
    private bool quickAddIsContinuous;

    [ObservableProperty]
    private QuadrantType quickAddQuadrant =
        QuadrantType
            .NotImportantNotUrgent;

    [ObservableProperty]
    private DateTime quickAddDate;

    /// <summary>
    /// 日历新增任务的具体截止时间。
    ///
    /// null 表示只设置日期，
    /// 没有具体截止时刻。
    /// </summary>
    [ObservableProperty]
    private TimeSpan?
        quickAddDueTime;

    /// <summary>
    /// 日历新增任务选择的提醒方式。
    /// </summary>
    [ObservableProperty]
    private TaskReminderOption?
        quickAddReminderOption;

    /// <summary>
    /// 日历新增任务选择的循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType
        quickAddRepeatType =
            TaskRepeatType.None;

    public string QuickAddDateText =>
        QuickAddDate.ToString(
            "yyyy年M月d日 dddd",
            CultureInfo
                .GetCultureInfo(
                    "zh-CN"));

    /// <summary>
    /// 日历新增任务始终已经有截止日期。
    ///
    /// 只有选择了具体截止时间以后，
    /// “是否提醒”才允许使用。
    /// </summary>
    public bool CanSetQuickAddReminder =>
        QuickAddDueTime.HasValue;

    #endregion


    public CalendarViewModel(
        TaskService taskService,
        LunarCalendarService lunarCalendarService,
        DialogService dialogService,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
    {
        _taskService =
            taskService;

        _lunarCalendarService =
            lunarCalendarService;

        _dialogService =
            dialogService;

        _clock =
            clock ??
            SystemClock.Instance;

        _localTimeService =
            localTimeService ??
            LocalTimeService.System;

        DateTime today =
            GetToday();

        quickAddDate =
            today;

        _displayMonth =
            new DateTime(
                today.Year,
                today.Month,
                1);

        _taskEditorAutoSave =
            new TaskEditorAutoSaveCoordinator(
                TimeSpan.FromMilliseconds(
                    600),
                () => EditingTask is not null,
                AutoSaveTaskEditorAsync,
                OnTaskEditorAutoSaveFailed);
    }

    /// <summary>
    /// 读取当前 42 天月视图中的任务并重建日期格。
    /// </summary>
    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        long loadVersion =
            Interlocked.Increment(
                ref _loadVersion);

        IsBusy =
            true;

        StatusMessage =
            "正在加载日历";

        try
        {
            DateTime firstDayOfMonth =
                DisplayMonth;

            int offsetFromMonday =
                ((int)firstDayOfMonth.DayOfWeek +
                 6) %
                7;

            DateTime gridStart =
                firstDayOfMonth.AddDays(
                    -offsetFromMonday);

            DateTime gridEndExclusive =
                gridStart.AddDays(42);

            DateTimeOffset startUtc =
                _localTimeService
                    .ResolveLocalDateTime(
                        gridStart)
                    .ToUniversalTime();

            DateTimeOffset endUtc =
                _localTimeService
                    .ResolveLocalDateTime(
                        gridEndExclusive)
                    .ToUniversalTime();

            IReadOnlyList<TaskItem> allTasks =
                await _taskService
                    .GetCalendarTasksAsync(
                        startUtc,
                        endUtc,
                        cancellationToken);

            if (loadVersion !=
                Volatile.Read(
                    ref _loadVersion))
            {
                return;
            }

            Dictionary<DateTime, List<TaskItem>>
                tasksByDate =
                    allTasks
                        .Where(
                            task =>
                                task.DueAt
                                    .HasValue)
                        .Select(
                            task =>
                                new
                                {
                                    Task = task,

                                    LocalDate =
                                        task.DueAt!
                                            .Value
                                            .DateTime
                                            .Date
                                })
                        .Where(
                            item =>
                                item.LocalDate >=
                                    gridStart &&
                                item.LocalDate <
                                    gridEndExclusive)
                        .GroupBy(
                            item =>
                                item.LocalDate)
                        .ToDictionary(
                            group =>
                                group.Key,

                            group =>
                                group
                                    .Select(
                                        item =>
                                            item.Task)
                                    .OrderBy(
                                        task =>
                                            task.Status)
                                    .ThenByDescending(
                                        task =>
                                            task.IsImportant)
                                    .ThenBy(
                                        task =>
                                            task.Title,
                                        StringComparer
                                            .CurrentCultureIgnoreCase)
                                    .ToList());

            List<CalendarDayItem>
                rebuiltDays =
                    new(42);

            DateTime today =
                GetToday();

            for (int dayOffset = 0;
                 dayOffset < 42;
                 dayOffset++)
            {
                DateTime date =
                    gridStart.AddDays(
                        dayOffset);

                tasksByDate.TryGetValue(
                    date,
                    out List<TaskItem>?
                        dateTasks);

                rebuiltDays.Add(
                    new CalendarDayItem
                    {
                        Date =
                            date,

                        IsCurrentMonth =
                            date.Month ==
                                DisplayMonth.Month &&
                            date.Year ==
                                DisplayMonth.Year,

                        IsToday =
                            date.Date ==
                                today,

                        SolarDayText =
                            CreateSolarDayText(
                                date),

                        LunarText =
                            _lunarCalendarService
                                .GetDisplayText(
                                    date),

                        Tasks =
                            dateTasks ??
                            []
                    });
            }

            Days.Clear();

            foreach (CalendarDayItem day
                     in rebuiltDays)
            {
                Days.Add(
                    day);
            }

            /*
             * 如果任务详情正在打开，
             * 将 EditingTask 引用更新为刷新后的对象。
             * EditorTitle 等编辑缓冲区不被覆盖。
             */
            if (EditingTask is not null)
            {
                string editingTaskId =
                    EditingTask.Id;

                TaskItem? refreshedEditingTask =
                    FindTaskById(
                        editingTaskId);

                if (refreshedEditingTask is not null)
                {
                    EditingTask =
                        refreshedEditingTask;
                }
            }

            int visibleTaskCount =
                rebuiltDays.Sum(
                    day =>
                        day.TaskCount);

            StatusMessage =
                visibleTaskCount == 0
                    ? "本月视图中没有带截止日期的任务"
                    : $"当前月视图共显示 " +
                      $"{visibleTaskCount} 条任务";
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "日历加载已取消";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "加载日历失败。",
                exception);

            StatusMessage =
                $"日历加载失败：" +
                $"{exception.Message}";
        }
        finally
        {
            if (loadVersion ==
                Volatile.Read(
                    ref _loadVersion))
            {
                IsBusy =
                    false;
            }
        }
    }

    #region 快速新增

    /// <summary>
    /// 准备在指定日期新增任务。
    /// </summary>
    public void BeginQuickAdd(
        DateTime date)
    {
        QuickAddDate =
            date.Date;

        QuickAddTitle =
            string.Empty;

        QuickAddDescription =
            string.Empty;

        QuickAddIsImportant =
            false;

        QuickAddIsContinuous =
            false;

        QuickAddQuadrant =
            QuadrantType
                .NotImportantNotUrgent;

        /*
         * 每次打开日历新增任务窗口，
         * 都恢复与“所有任务”新增窗口一致的默认值。
         */
        QuickAddDueTime =
            null;

        QuickAddReminderOption =
            ReminderOptions[0];

        QuickAddRepeatType =
            TaskRepeatType.None;

        OnPropertyChanged(
            nameof(QuickAddDateText));

        OnPropertyChanged(
            nameof(CanSetQuickAddReminder));

        StatusMessage =
            $"准备在 " +
            $"{QuickAddDate:yyyy-MM-dd} " +
            $"新增任务";
    }

    /// <summary>
    /// 在当前选择日期新增任务。
    /// </summary>
    public async Task<bool> AddQuickTaskAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                QuickAddTitle))
        {
            StatusMessage =
                "请输入任务标题";

            return false;
        }

        IsBusy =
            true;

        try
        {
            TaskReminderOption
                reminderOption =
                    QuickAddReminderOption ??
                    ReminderOptions[0];

            TaskEditDraft draft =
                new(
                    QuickAddTitle,
                    QuickAddDescription,
                    QuickAddDate,
                    QuickAddDueTime,
                    reminderOption.Enabled,
                    reminderOption.MinutesBefore,
                    QuickAddRepeatType,
                    QuickAddIsContinuous,
                    QuickAddIsImportant,
                    QuickAddQuadrant);

            /*
             * 直接使用 TaskService 的完整创建版本。
             *
             * 一次性保存：
             * 标题、说明、象限、日期、具体时间、
             * 重点、提醒方式和循环方式。
             *
             * 不再使用旧的 CreateTaskAsync 后
             * 再 UpdateTaskAsync 补写说明/重点的流程。
             */
            TaskItem createdTask =
                await _taskService
                    .CreateTaskAsync(
                        draft,
                        cancellationToken,
                        TaskChangeSource.Calendar);

            string createdTitle =
                createdTask.Title;

            /*
             * 成功以后恢复默认状态。
             * 下一次打开 Popup 时 BeginQuickAdd()
             * 还会再次统一初始化。
             */
            QuickAddTitle =
                string.Empty;

            QuickAddDescription =
                string.Empty;

            QuickAddIsImportant =
                false;

            QuickAddIsContinuous =
                false;

            QuickAddDueTime =
                null;

            QuickAddReminderOption =
                ReminderOptions[0];

            QuickAddRepeatType =
                TaskRepeatType.None;

            StatusMessage =
                $"已新增任务：" +
                $"{createdTitle}";

            await LoadAsync(
                cancellationToken);

            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "从日历新增任务失败。",
                exception);

            StatusMessage =
                exception.Message;

            return false;
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    partial void OnQuickAddDateChanged(
        DateTime value)
    {
        OnPropertyChanged(
            nameof(QuickAddDateText));
    }

    /// <summary>
    /// 新增任务的截止时间变化时，
    /// 同步刷新提醒控件的可用状态。
    ///
    /// 时间改回“无”以后，
    /// 提醒自动恢复为“不提醒”。
    /// </summary>
    partial void OnQuickAddDueTimeChanged(
        TimeSpan? value)
    {
        OnPropertyChanged(
            nameof(CanSetQuickAddReminder));

        if (!value.HasValue)
        {
            QuickAddReminderOption =
                ReminderOptions[0];
        }
    }

    #endregion


    #region 月份命令

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        DisplayMonth =
            DisplayMonth.AddMonths(-1);

        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonthAsync()
    {
        DisplayMonth =
            DisplayMonth.AddMonths(1);

        return LoadAsync();
    }

    [RelayCommand]
    private Task GoToTodayAsync()
    {
        DateTime today =
            GetToday();

        DisplayMonth =
            new DateTime(
                today.Year,
                today.Month,
                1);

        return LoadAsync();
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return LoadAsync();
    }

    #endregion

    private TaskItem? FindTaskById(
        string taskId)
    {
        return Days
            .SelectMany(
                day =>
                    day.Tasks)
            .FirstOrDefault(
                task =>
                    task.Id ==
                    taskId);
    }

    private static string CreateSolarDayText(
        DateTime date)
    {
        return date.Day == 1
            ? $"{date.Month}月1日"
            : date.Day.ToString(
                CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 根据任务当前保存的提醒数据，
    /// 找到详情提醒下拉框对应的选项。
    /// </summary>
    private TaskReminderOption
        FindReminderOption(
            TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        if (!task.ReminderEnabled)
        {
            return ReminderOptions[0];
        }

        TaskReminderOption? option =
            ReminderOptions
                .FirstOrDefault(
                    item =>
                        item.Enabled &&
                        item.MinutesBefore ==
                            task.ReminderMinutesBefore);

        return option ??
            ReminderOptions[0];
    }

    private static DateTimeOffset
        CreateLocalDateStart(
            DateTime date)
    {
        return TaskRules.CreateLocalDueAt(
                   date,
                   dueTime: null) ??
               throw new InvalidOperationException(
                   "无法创建日历日期。");
    }

    private DateTime GetToday()
    {
        return _localTimeService
            .ToLocalDateTime(
                _clock.UtcNow)
            .Date;
    }

}

