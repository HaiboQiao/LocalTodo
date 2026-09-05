using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.ViewModels;

public partial class TaskListViewModel
{
    /// <summary>
    /// 配置“所有任务”的日期分组和完整业务排序。
    /// 已完成页面继续使用普通平铺列表。
    /// </summary>
    private void ConfigureTaskEntriesView()
    {
        if (Status != TodoStatus.Pending)
        {
            return;
        }

        TaskDateGroupingService.ConfigureView(
            TaskEntriesView,
            TaskDateGroupSortProfile.Detailed);
    }

    /// <summary>
    /// 将已经不属于当前页面状态的任务
    /// 直接从当前内存集合中移除。
    ///
    /// 例如：
    /// “所有任务”中的 Pending 被标记完成以后，
    /// 它已经不属于当前页面，因此不需要重新读取
    /// 和重建整个任务列表。
    ///
    /// 使用增量删除可以避免 ListBox / ScrollViewer
    /// 被整体重建，从而尽量保持用户当前滚动位置。
    /// </summary>
    private void RemoveTaskFromCurrentView(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        string taskId =
            task.Id;

        /*
         * 如果被完成的任务正好是右侧当前选中的任务，
         * 先主动取消选择。
         *
         * 如果用户完成的是另一条任务，
         * 则保持当前右侧详情不变。
         */
        if (SelectedEntry?.Task.Id ==
            taskId)
        {
            SelectedEntry =
                null;
        }
        else if (SelectedTask?.Id ==
                 taskId)
        {
            SelectedTask =
                null;
        }

        /*
         * 只删除对应的显示包装条目。
         *
         * ObservableCollection 的 Remove
         * 会让 ICollectionView 自动收到删除通知。
         *
         * 不要在这里调用：
         *
         * TaskEntries.Clear()
         * TaskEntriesView.Refresh()
         *
         * 否则又会导致整个列表重新布局。
         */
        TaskListEntry? entryToRemove =
            TaskEntries.FirstOrDefault(
                entry =>
                    entry.Task.Id ==
                    taskId);

        if (entryToRemove is not null)
        {
            TaskEntries.Remove(
                entryToRemove);
        }

        /*
         * 同步删除原始任务集合中的对象。
         */
        TaskItem? taskToRemove =
            Tasks.FirstOrDefault(
                item =>
                    item.Id ==
                    taskId);

        if (taskToRemove is not null)
        {
            Tasks.Remove(
                taskToRemove);
        }

        /*
         * 更新依赖任务数量的界面属性。
         */
        OnPropertyChanged(
            nameof(HasTasks));

        OnPropertyChanged(
            nameof(IsEmpty));

        /*
         * 你现在“所有任务”左上角已经显示：
         * “共 X 项待办”
         *
         * 如果当前 TaskListViewModel 中已经有
         * TaskCountText 属性，这个通知必须保留。
         */
        OnPropertyChanged(
            nameof(TaskCountText));
    }

    /// <summary>
    /// 将新生成的循环任务
    /// 增量加入当前“所有任务”页面。
    ///
    /// 与 RemoveTaskFromCurrentView 配套使用。
    ///
    /// 这里必须坚持增量更新：
    ///
    /// 旧任务只 Remove；
    /// 新任务只 Add。
    ///
    /// 不重新读取数据库，
    /// 也不 Refresh 整个 CollectionView，
    /// 从而尽量保持当前滚动位置。
    /// </summary>
    private void AddTaskToCurrentView(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        /*
         * 防止同一任务重复加入。
         */
        if (Tasks.Any(
                existingTask =>
                    existingTask.Id ==
                    task.Id))
        {
            return;
        }

        /*
         * 加入原始任务集合。
         */
        Tasks.Add(
            task);

        /*
         * 为新循环周期建立显示包装。
         *
         * 分组、截止日期排序等字段
         * 在 Create() 时就已经计算完成。
         */
        TaskListEntry entry =
            TaskListEntry.Create(
                task,
                GetToday(),
                Tasks.Count - 1);

        /*
         * 只执行增量 Add。
         *
         * 不再调用：
         *
         * TaskEntriesView.Refresh();
         *
         * Refresh 会重新创建整个 CollectionView，
         * 容易引起 ListBox 的 CurrentItem、
         * Selection、虚拟化容器和滚动位置重新计算。
         */
        TaskEntries.Add(
            entry);

        /*
         * 更新依赖任务数量的界面属性。
         */
        OnPropertyChanged(
            nameof(HasTasks));

        OnPropertyChanged(
            nameof(IsEmpty));

        OnPropertyChanged(
            nameof(TaskCountText));
    }

    private async Task ReloadAndSelectAsync(
        string? selectedTaskId)
    {
        _isReloadingTasks =
            true;

        try
        {
            IReadOnlyList<TaskItem> tasks =
                await _taskService
                    .GetTasksAsync(
                        Status);

            SelectedEntry =
                null;

            SelectedTask =
                null;

            Tasks.Clear();

            foreach (TaskItem task in tasks)
            {
                _deletedTaskIds.Remove(
                    task.Id);

                Tasks.Add(task);
            }

            RebuildTaskEntries(
                selectedTaskId);

            OnPropertyChanged(
                nameof(HasTasks));

            OnPropertyChanged(
                nameof(IsEmpty));

            OnPropertyChanged(
                nameof(TaskCountText));
        }
        finally
        {
            _isReloadingTasks =
                false;
        }
    }

    /// <summary>
    /// 根据当前日期重新构建任务条目和日期分组。
    /// </summary>
    private void RebuildTaskEntries(
        string? selectedTaskId)
    {
        DateTime today =
            GetToday();

        _lastGroupingDate =
            today;

        /*
         * 先在普通 List 中计算全部条目，
         * 不在 CollectionView 刷新期间修改源集合。
         */
        IReadOnlyList<TaskListEntry>
            rebuiltEntries =
                TaskDateGroupingService
                    .CreateEntries(
                        Tasks,
                        today);

        /*
         * 清空前取消选择，避免 CurrentItem
         * 指向即将删除的旧包装条目。
         */
        SelectedEntry =
            null;

        TaskEntries.Clear();

        foreach (TaskListEntry entry
                 in rebuiltEntries)
        {
            TaskEntries.Add(
                entry);
        }

        TaskEntriesView.Refresh();

        if (string.IsNullOrWhiteSpace(
                selectedTaskId))
        {
            return;
        }

        SelectedEntry =
            TaskEntries.FirstOrDefault(
                entry =>
                    entry.Task.Id ==
                    selectedTaskId);
    }

    /// <summary>
    /// 每分钟检查一次日期分组是否需要更新。
    ///
    /// 两种情况需要重建：
    ///
    /// 1. 跨过午夜，日期发生变化；
    ///
    /// 2. 某个设置了具体截止时间的任务
    ///    在当前这一分钟已经从“今天”变成“已过期”。
    ///
    /// 如果分组状态没有变化，则什么都不做，
    /// 避免每分钟无意义地重建整个任务列表。
    /// </summary>
    private void OnTimeRefreshRequested(
        object? sender,
        TaskTimeRefreshEventArgs e)
    {
        DateTime today =
            e.Today;

        bool dateChanged =
            today !=
            _lastGroupingDate;

        /*
         * 检查当前 TaskListEntry 中保存的分组，
         * 是否已经与 TaskItem 当前真实过期状态不一致。
         *
         * GroupOrder == 0
         * 表示这个条目目前位于“已过期”。
         */
        bool overdueGroupingChanged =
            TaskDateGroupingService
                .RequiresRegroup(
                    TaskEntries);

        /*
         * 日期没变，
         * 也没有任务刚刚跨过截止时间，
         * 就不需要重建列表。
         */
        if (!dateChanged &&
            !overdueGroupingChanged)
        {
            return;
        }

        string? selectedTaskId =
            SelectedTask?.Id;

        /*
         * 这是程序内部的自动分组刷新，
         * 不是用户主动切换任务。
         *
         * 临时标记为重新加载状态，
         * 避免重建 TaskEntries 时触发
         * 不必要的自动保存或状态文字变化。
         */
        bool previousReloadingState =
            _isReloadingTasks;

        _isReloadingTasks =
            true;

        try
        {
            RebuildTaskEntries(
                selectedTaskId);
        }
        finally
        {
            _isReloadingTasks =
                previousReloadingState;
        }

        /*
         * 只有真正跨天时才显示日期更新提示。
         *
         * 单纯某个任务到点进入“已过期”时，
         * 不覆盖用户当前的自动保存状态文字。
         */
        if (dateChanged)
        {
            StatusMessage =
                Tasks.Count == 0
                    ? EmptyMessage
                    : $"当前共显示 " +
                      $"{Tasks.Count} 条任务";
        }
    }

    partial void OnSelectedEntryChanged(
        TaskListEntry? value)
    {
        SelectedTask =
            value?.Task;
    }

}
