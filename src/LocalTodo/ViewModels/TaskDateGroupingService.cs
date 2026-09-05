using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using LocalTodo.Models;

namespace LocalTodo.ViewModels;

/// <summary>
/// 统一建立“已过期、今天、未来日期、无截止日期”分组。
///
/// TaskList、桌面任务列表和四象限都使用同一套条目创建、
/// 分组刷新判断和 CollectionView 配置，避免页面之间再次漂移。
/// </summary>
public static class TaskDateGroupingService
{
    public static void ConfigureView(
        ICollectionView view,
        TaskDateGroupSortProfile sortProfile)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.GroupDescriptions.Add(
            new PropertyGroupDescription(
                nameof(TaskListEntry.GroupHeader)));

        AddSort(
            view,
            nameof(TaskListEntry.GroupOrder),
            ListSortDirection.Ascending);

        AddSort(
            view,
            nameof(TaskListEntry.GroupDate),
            ListSortDirection.Ascending);

        if (sortProfile ==
            TaskDateGroupSortProfile.Detailed)
        {
            AddSort(
                view,
                nameof(TaskListEntry.SortDueAt),
                ListSortDirection.Ascending);

            AddSort(
                view,
                nameof(TaskListEntry.SortIsImportant),
                ListSortDirection.Descending);

            AddSort(
                view,
                nameof(TaskListEntry.SortPriority),
                ListSortDirection.Descending);

            AddSort(
                view,
                nameof(TaskListEntry.SortCreatedAt),
                ListSortDirection.Descending);
        }

        AddSort(
            view,
            nameof(TaskListEntry.Sequence),
            ListSortDirection.Ascending);
    }

    public static IReadOnlyList<TaskListEntry>
        CreateEntries(
            IEnumerable<TaskItem> tasks,
            DateTime today)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return tasks
            .Select(
                (task, sequence) =>
                    TaskListEntry.Create(
                        task,
                        today,
                        sequence))
            .ToList();
    }

    public static void ReplaceEntries(
        ObservableCollection<TaskListEntry> target,
        IEnumerable<TaskItem> tasks,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(target);

        IReadOnlyList<TaskListEntry> entries =
            CreateEntries(
                tasks,
                today);

        target.Clear();

        foreach (TaskListEntry entry in entries)
        {
            target.Add(entry);
        }
    }

    public static bool RequiresRegroup(
        IEnumerable<TaskListEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Any(
            entry =>
                entry.Task.IsOverdue !=
                (entry.GroupOrder == 0));
    }

    private static void AddSort(
        ICollectionView view,
        string propertyName,
        ListSortDirection direction)
    {
        view.SortDescriptions.Add(
            new SortDescription(
                propertyName,
                direction));
    }
}

public enum TaskDateGroupSortProfile
{
    PreserveSourceOrder = 0,
    Detailed = 1
}
