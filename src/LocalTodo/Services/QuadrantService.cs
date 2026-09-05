using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Data;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 负责四象限计算和手动象限管理。
/// </summary>
public sealed class QuadrantService
{
    private const string
        UrgencyThresholdSettingKey =
            "UrgencyThresholdDays";

    private const int
        DefaultUrgencyThresholdDays = 2;

    private const int
        MaximumUrgencyThresholdDays = 30;

    private readonly TaskService
        _taskService;

    private readonly AppSettingRepository
        _appSettingRepository;

    public QuadrantService(
        TaskService taskService,
        AppSettingRepository appSettingRepository)
    {
        _taskService = taskService;
        _appSettingRepository =
            appSettingRepository;
    }

    /// <summary>
    /// 读取并计算全部四象限任务。
    /// </summary>
    public async Task<QuadrantSnapshot>
        GetSnapshotAsync(
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskItem> tasks =
            await _taskService.GetTasksAsync(
                TodoStatus.Pending,
                cancellationToken);

        int urgencyThresholdDays =
            await GetUrgencyThresholdDaysAsync(
                cancellationToken);

        List<TaskItem> importantAndUrgent = [];
        List<TaskItem> importantNotUrgent = [];
        List<TaskItem> urgentNotImportant = [];
        List<TaskItem> notImportantNotUrgent = [];

        foreach (TaskItem task in tasks)
        {
            QuadrantType quadrant =
                GetEffectiveQuadrant(
                    task,
                    urgencyThresholdDays);

            switch (quadrant)
            {
                case QuadrantType.ImportantAndUrgent:
                    importantAndUrgent.Add(task);
                    break;

                case QuadrantType.ImportantNotUrgent:
                    importantNotUrgent.Add(task);
                    break;

                case QuadrantType.UrgentNotImportant:
                    urgentNotImportant.Add(task);
                    break;

                case QuadrantType.NotImportantNotUrgent:
                    notImportantNotUrgent.Add(task);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(quadrant),
                        quadrant,
                        "无法识别象限类型。");
            }
        }

        return new QuadrantSnapshot
        {
            ImportantAndUrgentTasks =
                importantAndUrgent,

            ImportantNotUrgentTasks =
                importantNotUrgent,

            UrgentNotImportantTasks =
                urgentNotImportant,

            NotImportantNotUrgentTasks =
                notImportantNotUrgent,

            UrgencyThresholdDays =
                urgencyThresholdDays
        };
    }

    /// <summary>
    /// 将任务手动移动到指定象限。
    /// </summary>
    public async Task SetManualQuadrantAsync(
        TaskItem task,
        QuadrantType quadrant,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        TaskEditBaseline baseline =
            TaskEditBaseline.FromTask(
                task);

        TaskEditSaveResult saveResult =
            await _taskService
                .SaveTaskEditAsync(
                    new TaskEditRequest(
                        baseline,
                        TaskEditDraft
                            .FromTask(
                                task) with
                        {
                            Quadrant =
                                quadrant
                        },
                        TaskEditFields.Quadrant),
                    cancellationToken,
                    changeSource);

        if (!saveResult.IsSaved ||
            saveResult.Current is null)
        {
            throw new TaskConcurrencyException(
                task.Id);
        }

        saveResult.Current.ApplyTo(
            task);
    }

    /// <summary>
    /// 清除手动象限并恢复自动计算。
    /// </summary>
    public async Task RestoreAutomaticAsync(
        TaskItem task,
        CancellationToken cancellationToken = default,
        TaskChangeSource changeSource =
            TaskChangeSource.Unknown)
    {
        ArgumentNullException.ThrowIfNull(task);

        TaskEditBaseline originalState =
            TaskEditBaseline.FromTask(
                task);

        task.QuadrantMode =
            QuadrantMode.Automatic;

        task.ManualQuadrant =
            null;

        try
        {
            await _taskService.UpdateTaskAsync(
                task,
                cancellationToken,
                changeSource);
        }
        catch
        {
            originalState.ApplyTo(
                task);

            throw;
        }
    }

    /// <summary>
    /// 获取任务当前所属象限。
    /// 新版本中象限由用户直接指定。
    /// </summary>
    public QuadrantType GetEffectiveQuadrant(
        TaskItem task,
        int urgencyThresholdDays)
    {
        ArgumentNullException.ThrowIfNull(task);

        /*
         * 为了暂时兼容原方法签名，
         * 保留参数但不再参与象限计算。
         */
        _ = urgencyThresholdDays;

        return task.AssignedQuadrant;
    }

    /// <summary>
    /// 判断任务是否紧急。
    /// </summary>
    public static bool IsUrgent(
        TaskItem task,
        int urgencyThresholdDays,
        DateTime? currentDate = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.DueAt.HasValue)
        {
            return false;
        }

        int normalizedThreshold =
            Math.Clamp(
                urgencyThresholdDays,
                0,
                MaximumUrgencyThresholdDays);

        DateTime dueDate =
            task.DueAt.Value
                .DateTime
                .Date;

        DateTime urgentEndDate =
            (currentDate?.Date ??
             LocalTimeService.System
                 .ToLocalDateTime(
                     SystemClock.Instance.UtcNow)
                 .Date)
            .AddDays(
                normalizedThreshold);

        return dueDate <= urgentEndDate;
    }

    public static string GetQuadrantTitle(
        QuadrantType quadrant)
    {
        return quadrant switch
        {
            QuadrantType.ImportantAndUrgent =>
                "第一象限：重要且紧急",

            QuadrantType.ImportantNotUrgent =>
                "第二象限：重要但不紧急",

            QuadrantType.UrgentNotImportant =>
                "第三象限：紧急但不重要",

            QuadrantType.NotImportantNotUrgent =>
                "第四象限：不重要且不紧急",

            _ => "未知象限"
        };
    }

    private async Task<int>
        GetUrgencyThresholdDaysAsync(
            CancellationToken cancellationToken)
    {
        string? settingValue =
            await _appSettingRepository
                .GetValueAsync(
                    UrgencyThresholdSettingKey,
                    cancellationToken);

        if (!int.TryParse(
                settingValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int thresholdDays))
        {
            return DefaultUrgencyThresholdDays;
        }

        return Math.Clamp(
            thresholdDays,
            0,
            MaximumUrgencyThresholdDays);
    }
}
