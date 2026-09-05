using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalTodo.Services;

namespace LocalTodo.Models;

/// <summary>
/// LocalTodo 中的一条任务记录。
/// </summary>
public partial class TaskItem :
    ObservableObject
{
    [ObservableProperty]
    private string id =
        Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string title =
        string.Empty;

    [ObservableProperty]
    private string description =
        string.Empty;

    [ObservableProperty]
    private TodoStatus status =
        TodoStatus.Pending;

    [ObservableProperty]
    private TaskPriority priority =
        TaskPriority.None;

    [ObservableProperty]
    private bool isImportant;

    /// <summary>
    /// 是否将任务视为从现在持续到截止日期的任务。
    /// 只有存在截止日期时该状态才有效。
    /// </summary>
    [ObservableProperty]
    private bool isContinuous;

    [ObservableProperty]
    private DateTimeOffset? dueAt;

    /// <summary>
    /// 是否明确设置了截止时间。
    ///
    /// false 表示只设置日期；
    /// true 表示设置了具体时分。
    /// </summary>
    [ObservableProperty]
    private bool hasDueTime;

    /// <summary>
    /// 当前任务是否启用提醒。
    ///
    /// false：不提醒；
    /// true：按照 ReminderMinutesBefore
    ///       计算提醒时间。
    /// </summary>
    [ObservableProperty]
    private bool reminderEnabled;

    /// <summary>
    /// 在截止时间之前多少分钟提醒。
    ///
    /// 0    = 到点提醒
    /// 5    = 提前5分钟
    /// 15   = 提前15分钟
    /// 30   = 提前30分钟
    /// 60   = 提前1小时
    /// 240  = 提前4小时
    /// 1440 = 提前1天
    ///
    /// ReminderEnabled = false 时，
    /// 此值不参与提醒计算。
    /// </summary>
    [ObservableProperty]
    private int reminderMinutesBefore;

    /// <summary>
    /// 当前任务的循环方式。
    /// </summary>
    [ObservableProperty]
    private TaskRepeatType repeatType =
        TaskRepeatType.None;

    /// <summary>
    /// 当前任务所属的循环系列 ID。
    ///
    /// 同一循环任务生成出来的所有周期
    /// 都使用相同的 SeriesId。
    ///
    /// 普通不循环任务为 null。
    /// </summary>
    [ObservableProperty]
    private string? recurrenceSeriesId;

    /// <summary>
    /// 每年循环的原始月份锚点。其他循环方式为 null。
    /// </summary>
    [ObservableProperty]
    private int? recurrenceAnchorMonth;

    /// <summary>
    /// 每月或每年循环的原始日期锚点。
    ///
    /// 目标月份没有该日期时临时使用月末，但下一期仍按这里保存的
    /// 原始日期恢复，不会因为 2 月而永久漂移。
    /// </summary>
    [ObservableProperty]
    private int? recurrenceAnchorDay;

    /// <summary>
    /// 当前这一期提醒已经触发的时间。
    ///
    /// null 表示尚未提醒。
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? reminderDeliveredAt;

    [ObservableProperty]
    private QuadrantMode quadrantMode =
        QuadrantMode.Automatic;

    [ObservableProperty]
    private QuadrantType? manualQuadrant;

    [ObservableProperty]
    private DateTimeOffset createdAt =
        SystemClock.Instance.UtcNow;

    [ObservableProperty]
    private DateTimeOffset updatedAt =
        SystemClock.Instance.UtcNow;

    [ObservableProperty]
    private DateTimeOffset? completedAt;

    /// <summary>
    /// 任务最近一次进入垃圾箱的 UTC 时间。
    /// 活动任务为 null；从垃圾箱恢复时重新清空。
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? deletedAt;

    /// <summary>
    /// 数据库乐观并发版本。
    ///
    /// 每次成功写入任务记录后递增。普通详情保存必须携带读取时的
    /// Revision，防止旧页面对象静默覆盖其他窗口的新修改。
    /// </summary>
    public long Revision
    { get; set; }

    /// <summary>
    /// 当前任务是否已经完成。
    /// </summary>
    public bool IsCompleted =>
        Status == TodoStatus.Completed;

    /// <summary>
    /// 当前任务是否属于循环任务。
    ///
    /// RepeatType = None：
    /// 普通任务。
    ///
    /// 其他任何循环类型：
    /// 都属于循环任务。
    ///
    /// 该属性主要供任务列表中的
    /// 循环状态图标使用。
    /// </summary>
    public bool IsRecurring =>
        RepeatType !=
            TaskRepeatType.None;

    /// <summary>
    /// 优先级中文显示文本。
    /// </summary>
    public string PriorityText =>
        Priority switch
        {
            TaskPriority.Low =>
                "低优先级",

            TaskPriority.Medium =>
                "中优先级",

            TaskPriority.High =>
                "高优先级",

            _ =>
                "无优先级"
        };

    /// <summary>
    /// 截止日期本地显示文本。
    /// </summary>
    public string DueDateText
    {
        get
        {
            if (!DueAt.HasValue)
            {
                return "无截止日期";
            }

            DateTimeOffset localDueAt =
                DueAt.Value;

            DateTime localDateTime =
                LocalDueDateTime.GetWallClock(
                    localDueAt);

            string value = HasDueTime
                ? localDateTime.ToString(
                    "yyyy-MM-dd HH:mm")
                : localDateTime.ToString(
                    "yyyy-MM-dd");

            return IsContinuous
                ? $"~ {value}"
                : value;
        }
    }

    /// <summary>
    /// 日历任务条中使用的截止时间文本。
    ///
    /// 只有任务明确设置了具体截止时间时
    /// 才返回本地 HH:mm。
    ///
    /// 例如：
    ///
    /// 09:00
    /// 14:30
    /// 21:00
    ///
    /// 如果任务只有截止日期、没有具体时间，
    /// 或者根本没有截止日期，
    /// 返回空字符串。
    /// </summary>
    public string DueTimeText
    {
        get
        {
            if (!HasDueTime ||
                !DueAt.HasValue)
            {
                return string.Empty;
            }

            return DueAt.Value
                .DateTime
                .ToString("HH:mm");
        }
    }

    /// <summary>
    /// 任务完成时间的本地显示文本。
    ///
    /// 完成时间在数据库中按照 UTC 保存，
    /// 界面显示时转换为当前 Windows 本地时间。
    /// </summary>
    public string CompletedAtText =>
        CompletedAt.HasValue
            ? LocalTimeService.System
                .ToLocalDateTime(
                    CompletedAt.Value)
                .ToString(
                    "yyyy-MM-dd HH:mm")
            : "未记录";

    /// <summary>
    /// 当前未完成任务是否已经过期。
    ///
    /// 规则：
    ///
    /// 1. 没有截止日期：
    ///    永远不算过期。
    ///
    /// 2. 只设置了截止日期，没有设置具体时间：
    ///    截止日期当天仍然属于“今天”，
    ///    到第二天以后才算过期。
    ///
    /// 3. 设置了具体截止时间：
    ///    当前时间达到或超过截止时间以后，
    ///    立即算作过期。
    /// </summary>
    public bool IsOverdue =>
        IsOverdueAt(
            SystemClock.Instance.UtcNow,
            LocalTimeService.System);

    /// <summary>
    /// 使用指定时钟快照和时区判断是否过期，供确定性测试和统一刷新使用。
    /// </summary>
    public bool IsOverdueAt(
        DateTimeOffset now,
        ILocalTimeService localTimeService)
    {
        ArgumentNullException.ThrowIfNull(
            localTimeService);

        if (Status !=
                TodoStatus.Pending ||
            !DueAt.HasValue)
        {
            return false;
        }

        DateTime localDueAt =
            LocalDueDateTime.GetWallClock(
                DueAt.Value);

        if (HasDueTime)
        {
            return localTimeService
                       .ResolveLocalDateTime(
                           localDueAt) <=
                   now;
        }

        DateTime today =
            localTimeService
                .ToLocalDateTime(now)
                .Date;

        return localDueAt.Date < today;
    }

    /// <summary>
    /// 当前象限是否由用户手动指定。
    /// </summary>
    public bool IsManualQuadrant =>
        QuadrantMode ==
        QuadrantMode.Manual;

    /// <summary>
    /// 象限分类方式中文文本。
    /// </summary>
    public string QuadrantModeText =>
        IsManualQuadrant
            ? "手动分类"
            : "自动分类";

    /// <summary>
    /// 当前任务实际使用的象限。
    ///
    /// 旧任务如果还没有 manual_quadrant，
    /// 就根据旧优先级自动映射。
    ///
    /// 注意：
    ///
    /// 所属象限与“标记为重要任务”是两个独立概念。
    ///
    /// 修改象限时只修改：
    ///
    /// 1. QuadrantMode；
    /// 2. ManualQuadrant；
    /// 3. 用于兼容旧数据库的 Priority。
    ///
    /// 不允许自动修改 IsImportant。
    /// </summary>
    public QuadrantType AssignedQuadrant
    {
        get
        {
            if (QuadrantMode ==
                    QuadrantMode.Manual &&
                ManualQuadrant.HasValue)
            {
                return ManualQuadrant.Value;
            }

            return QuadrantMapping
                .FromLegacyPriority(
                    Priority);
        }

        set
        {
            QuadrantMapping
                .ValidateQuadrant(value);

            TaskPriority expectedPriority =
                QuadrantMapping
                    .ToLegacyPriority(value);

            /*
             * 已经位于这个手动象限且旧 Priority 也一致时，
             * 不重复触发属性变化。
             *
             * Priority 不一致时仍要继续执行，修复旧版本中
             * 只写 ManualQuadrant、没有同步兼容字段的数据。
             */
            if (QuadrantMode ==
                    QuadrantMode.Manual &&
                ManualQuadrant ==
                    value &&
                Priority ==
                    expectedPriority)
            {
                return;
            }

            /*
             * 新版本中象限由用户直接指定。
             */
            QuadrantMode =
                QuadrantMode.Manual;

            ManualQuadrant =
                value;

            /*
             * Priority 当前只是旧数据库兼容字段。
             *
             * 为了保证旧数据和旧查询逻辑仍然正常，
             * 修改象限以后继续同步 Priority。
             *
             * 注意：
             * Priority 的同步不代表
             * IsImportant 也应该同步。
             */
            Priority =
                expectedPriority;

            /*
             * 非常重要：
             *
             * 这里绝对不要修改 IsImportant。
             *
             * IsImportant 是用户独立设置的
             * “标记为重要任务 / 重点星标”状态。
             *
             * 修改任务所属象限时，
             * 必须原样保留该状态。
             */

            NotifyQuadrantPropertiesChanged();
        }
    }

    /// <summary>
    /// 列表中显示的简短象限文字。
    /// </summary>
    public string QuadrantText =>
        QuadrantMapping.GetShortTitle(
            AssignedQuadrant);

    /// <summary>
    /// 任务列表中显示的象限罗马数字。
    ///
    /// 第一象限 → Ⅰ
    /// 第二象限 → Ⅱ
    /// 第三象限 → Ⅲ
    /// 第四象限 → Ⅳ
    ///
    /// 只用于任务列表的紧凑显示，
    /// 不影响任务详情中的完整象限名称。
    /// </summary>
    public string QuadrantListText =>
        AssignedQuadrant switch
        {
            QuadrantType.ImportantAndUrgent =>
                "Ⅰ",

            QuadrantType.ImportantNotUrgent =>
                "Ⅱ",

            QuadrantType.UrgentNotImportant =>
                "Ⅲ",

            QuadrantType.NotImportantNotUrgent =>
                "Ⅳ",

            _ =>
                string.Empty
        };

    /// <summary>
    /// 完整象限说明。
    /// </summary>
    public string QuadrantDisplayText =>
        QuadrantMapping.GetDisplayText(
            AssignedQuadrant);

    /// <summary>
    /// 当前象限在四象限语义上是否属于
    /// “重要”一侧。
    ///
    /// 第一、第二象限返回 true；
    /// 第三、第四象限返回 false。
    ///
    /// 注意：
    /// 这与用户手动设置的 IsImportant
    /// 重点星标是两个不同概念。
    /// </summary>
    public bool IsImportantQuadrant =>
        QuadrantMapping.IsImportant(
            AssignedQuadrant);

    partial void OnStatusChanged(
        TodoStatus value)
    {
        OnPropertyChanged(
            nameof(IsCompleted));

        OnPropertyChanged(
            nameof(IsOverdue));
    }

    partial void OnPriorityChanged(
    TaskPriority value)
    {
        OnPropertyChanged(
            nameof(PriorityText));

        NotifyQuadrantPropertiesChanged();
    }

    partial void OnDueAtChanged(
    DateTimeOffset? value)
    {
        OnPropertyChanged(
            nameof(DueDateText));

        /*
         * 截止时间发生变化以后，
         * 日历任务条右侧时间也必须立即刷新。
         */
        OnPropertyChanged(
            nameof(DueTimeText));

        OnPropertyChanged(
            nameof(IsOverdue));
    }

    partial void OnRepeatTypeChanged(
    TaskRepeatType value)
    {
        /*
         * RepeatType 改变以后，
         * 循环任务状态也可能发生变化。
         *
         * 通知任务列表立即刷新
         * 循环状态图标。
         */
        OnPropertyChanged(
            nameof(IsRecurring));
    }

    partial void OnHasDueTimeChanged(
    bool value)
    {
        OnPropertyChanged(
            nameof(DueDateText));

        /*
         * “无时间 → 有时间”
         * 或
         * “有时间 → 无时间”
         *
         * 都会直接影响日历右侧时间是否显示。
         */
        OnPropertyChanged(
            nameof(DueTimeText));

        OnPropertyChanged(
            nameof(IsOverdue));
    }

    partial void OnIsContinuousChanged(
        bool value)
    {
        OnPropertyChanged(
            nameof(DueDateText));
    }

    partial void OnCompletedAtChanged(
    DateTimeOffset? value)
    {
        OnPropertyChanged(
            nameof(CompletedAtText));
    }

    partial void OnQuadrantModeChanged(
    QuadrantMode value)
    {
        OnPropertyChanged(
            nameof(IsManualQuadrant));

        OnPropertyChanged(
            nameof(QuadrantModeText));

        NotifyQuadrantPropertiesChanged();
    }

    partial void OnManualQuadrantChanged(
    QuadrantType? value)
    {
        OnPropertyChanged(
            nameof(IsManualQuadrant));

        OnPropertyChanged(
            nameof(QuadrantModeText));

        NotifyQuadrantPropertiesChanged();
    }

    private void NotifyQuadrantPropertiesChanged()
    {
        OnPropertyChanged(
            nameof(AssignedQuadrant));

        OnPropertyChanged(
            nameof(QuadrantText));

        OnPropertyChanged(
            nameof(QuadrantListText));

        OnPropertyChanged(
            nameof(QuadrantDisplayText));

        OnPropertyChanged(
            nameof(IsImportantQuadrant));
    }
}
