using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

/// <summary>
/// 垃圾箱页面。
///
/// 垃圾箱中的任务只允许查看、恢复和永久删除，
/// 不允许直接编辑，也不会启用自动保存。
/// </summary>
public partial class TrashViewModel :
    ObservableObject
{
    private readonly TaskService
        _taskService;

    private readonly DialogService
        _dialogService;

    public ObservableCollection<TaskItem>
        Tasks
    { get; } = [];

    [ObservableProperty]
    private TaskItem?
        selectedTask;

    [ObservableProperty]
    private bool
        isBusy;

    [ObservableProperty]
    private string
        statusMessage =
            "正在准备垃圾箱";

    /// <summary>
    /// 垃圾箱任务列表中是否显示
    /// Ⅰ / Ⅱ / Ⅲ / Ⅳ 四象限简写。
    ///
    /// 只控制左侧任务列表，
    /// 不影响右侧“所属象限”详情。
    /// </summary>
    [ObservableProperty]
    private bool
        showQuadrantAbbreviations =
            true;

    public bool HasTasks =>
        Tasks.Count > 0;

    public bool IsEmpty =>
        Tasks.Count == 0;

    /// <summary>
    /// 垃圾箱左侧任务列表顶部显示的任务数量。
    ///
    /// 与“所有任务 / 已完成”的 TaskCountText
    /// 使用相同的功能：
    /// 当垃圾箱任务数量发生变化时自动刷新。
    /// </summary>
    public string TaskCountText =>
        $"共 {Tasks.Count} 项已删除";

    /// <summary>
    /// 页面顶部当前是否存在需要显示的操作状态。
    /// </summary>
    public bool HasStatusMessage =>
        !string.IsNullOrWhiteSpace(
            StatusMessage);

    public bool HasSelectedTask =>
        SelectedTask is not null;

    /// <summary>
    /// 右侧只读详情中的截止日期。
    ///
    /// 日期和具体时间分开显示，
    /// 因此这里只显示本地年月日。
    /// </summary>
    public string SelectedDueDateText
    {
        get
        {
            if (SelectedTask?.DueAt is not
                DateTimeOffset dueAt)
            {
                return "无";
            }

            return dueAt
                .DateTime
                .ToString("yyyy/M/d");
        }
    }

    /// <summary>
    /// 右侧只读详情中的具体截止时间。
    ///
    /// HasDueTime=false 时显示“无”。
    /// </summary>
    public string SelectedDueTimeText
    {
        get
        {
            if (SelectedTask is null ||
                !SelectedTask.HasDueTime ||
                !SelectedTask.DueAt.HasValue)
            {
                return "无";
            }

            return SelectedTask
                .DueAt
                .Value
                .DateTime
                .ToString("HH:mm");
        }
    }

    /// <summary>
    /// 右侧只读详情中的提醒方式。
    /// </summary>
    public string SelectedReminderText
    {
        get
        {
            if (SelectedTask is null ||
                !SelectedTask.ReminderEnabled)
            {
                return "不提醒";
            }

            return SelectedTask
                .ReminderMinutesBefore
                switch
            {
                0 =>
                    "到点提醒",

                5 =>
                    "提前5分钟",

                15 =>
                    "提前15分钟",

                30 =>
                    "提前30分钟",

                60 =>
                    "提前1小时",

                240 =>
                    "提前4小时",

                1440 =>
                    "提前1天",

                int minutes =>
                    $"提前{minutes}分钟"
            };
        }
    }

    /// <summary>
    /// 右侧只读详情中的所属象限。
    /// </summary>
    public string SelectedQuadrantText =>
        SelectedTask?.QuadrantText ??
        "未记录";

    /// <summary>
    /// 右侧只读详情中的循环方式。
    /// </summary>
    public string SelectedRepeatText =>
        SelectedTask?.RepeatType
        switch
        {
            TaskRepeatType.Daily =>
                "每天",

            TaskRepeatType.Weekly =>
                "每周",

            TaskRepeatType.Monthly =>
                "每月",

            TaskRepeatType.Yearly =>
                "每年",

            TaskRepeatType.Weekdays =>
                "每周工作日",

            _ =>
                "不循环"
        };

    /// <summary>
    /// 当前垃圾箱任务被删除前的状态。
    ///
    /// 软删除不会改变任务原本的 Status，
    /// 因此可以直接根据当前 Status 显示。
    /// </summary>
    public string SelectedDeletedStatusText =>
        SelectedTask?.Status
        switch
        {
            TodoStatus.Completed =>
                "已完成",

            TodoStatus.Pending =>
                "未完成",

            _ =>
                "未记录"
        };

    /// <summary>
    /// 当前任务进入垃圾箱的时间。
    /// </summary>
    public string SelectedDeletedAtText
    {
        get
        {
            if (SelectedTask is null)
            {
                return "未记录";
            }

            return (SelectedTask.DeletedAt ??
                    SelectedTask.UpdatedAt)
                .ToLocalTime()
                .ToString(
                    "yyyy-MM-dd HH:mm");
        }
    }

    public TrashViewModel(
        TaskService taskService,
        DialogService dialogService)
    {
        _taskService =
            taskService;

        _dialogService =
            dialogService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy =
            true;

        StatusMessage =
            "正在读取垃圾箱";

        try
        {
            await ReloadAsync();

            /*
             * 垃圾箱正常加载完成以后，
             * 任务数量已经由左侧 TaskCountText 负责显示。
             *
             * StatusMessage 从现在开始只用于：
             * 正在加载、恢复成功、永久删除成功和错误提示。
             */
            StatusMessage =
                string.Empty;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "读取垃圾箱失败。",
                exception);

            StatusMessage =
                $"读取垃圾箱失败：" +
                $"{exception.Message}";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    [RelayCommand]
    private async Task RestoreTaskAsync(
        TaskItem? task)
    {
        if (IsBusy ||
            task is null)
        {
            return;
        }

        IsBusy =
            true;

        /*
         * 在恢复之前记录这个垃圾箱任务
         * 当前是否仍然带有循环属性。
         *
         * 删除当前周期：
         * RepeatType 已经是 None。
         *
         * 删除整个周期：
         * RepeatType 仍然是 Daily / Weekly / ...
         */
        bool restoringRecurringTask =
            task.RepeatType !=
                TaskRepeatType.None;

        try
        {
            await _taskService
                .RestoreDeletedTaskAsync(
                    task,
                    changeSource:
                        TaskChangeSource.Trash);

            await ReloadAsync();

            StatusMessage =
                restoringRecurringTask
                    ? task.Status ==
                        TodoStatus.Completed
                        ? $"已恢复循环任务到已完成：" +
                          $"{task.Title}"
                        : $"已恢复循环任务到所有任务：" +
                          $"{task.Title}"
                    : task.Status ==
                        TodoStatus.Completed
                        ? $"已恢复普通任务到已完成：" +
                          $"{task.Title}"
                        : $"已恢复普通任务到所有任务：" +
                          $"{task.Title}";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "恢复垃圾箱任务失败。",
                exception);

            StatusMessage =
                exception.Message;
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    [RelayCommand]
    private async Task PermanentlyDeleteTaskAsync(
        TaskItem? task)
    {
        if (IsBusy ||
            task is null)
        {
            return;
        }

        bool confirmed =
            _dialogService
                .ConfirmPermanentTaskDeletion(
                    task.Title);

        if (!confirmed)
        {
            return;
        }

        IsBusy =
            true;

        try
        {
            await _taskService
                .PermanentlyDeleteTaskAsync(
                    task,
                    changeSource:
                        TaskChangeSource.Trash);

            await ReloadAsync();

            StatusMessage =
                $"已永久删除任务：" +
                $"{task.Title}";
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "永久删除垃圾箱任务失败。",
                exception);

            StatusMessage =
                exception.Message;
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    /// <summary>
    /// StatusMessage 改变时同步刷新顶部提示的可见状态。
    /// </summary>
    partial void OnStatusMessageChanged(
        string value)
    {
        OnPropertyChanged(
            nameof(HasStatusMessage));
    }

    partial void OnSelectedTaskChanged(
        TaskItem? value)
    {
        OnPropertyChanged(
            nameof(HasSelectedTask));

        /*
         * 当前垃圾箱任务变化以后，
         * 右侧所有计算得到的只读详情都需要刷新。
         */
        OnPropertyChanged(
            nameof(SelectedDueDateText));

        OnPropertyChanged(
            nameof(SelectedDueTimeText));

        OnPropertyChanged(
            nameof(SelectedReminderText));

        OnPropertyChanged(
            nameof(SelectedQuadrantText));

        OnPropertyChanged(
            nameof(SelectedRepeatText));

        OnPropertyChanged(
            nameof(SelectedDeletedStatusText));

        OnPropertyChanged(
            nameof(SelectedDeletedAtText));
    }

    private async Task ReloadAsync()
    {
        SelectedTask =
            null;

        Tasks.Clear();

        foreach (TaskItem task
                 in await _taskService
                     .GetDeletedTasksAsync())
        {
            Tasks.Add(task);
        }

        OnPropertyChanged(
            nameof(HasTasks));

        OnPropertyChanged(
            nameof(IsEmpty));

        OnPropertyChanged(
            nameof(TaskCountText));
    }
}
