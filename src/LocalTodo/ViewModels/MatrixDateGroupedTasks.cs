using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using LocalTodo.Models;

namespace LocalTodo.ViewModels;

/// <summary>
/// 四象限中一个象限的日期分组显示集合。
///
/// 业务层继续使用原来的 TaskItem 集合；
/// 此类只把任务包装为 TaskListEntry，
/// 并按照“已过期、今天、具体日期、无截止日期”分组。
/// </summary>
public sealed class MatrixDateGroupedTasks
{
    private readonly ObservableCollection<TaskListEntry>
        _entries =
            [];

    public ICollectionView View { get; }

    public MatrixDateGroupedTasks()
    {
        View =
            CollectionViewSource.GetDefaultView(
                _entries);

        TaskDateGroupingService.ConfigureView(
            View,
            TaskDateGroupSortProfile
                .PreserveSourceOrder);
    }

    /// <summary>
    /// 使用当前象限的任务重新建立日期分组。
    /// </summary>
    public void Replace(
        IEnumerable<TaskItem> tasks,
        DateTime today)
    {
        ArgumentNullException.ThrowIfNull(
            tasks);

        TaskDateGroupingService.ReplaceEntries(
            _entries,
            tasks,
            today);

        View.Refresh();
    }

    /// <summary>
    /// 判断条目保存的分组是否已落后于任务当前过期状态。
    /// </summary>
    public bool RequiresRegroup()
    {
        return TaskDateGroupingService
            .RequiresRegroup(
                _entries);
    }
}
