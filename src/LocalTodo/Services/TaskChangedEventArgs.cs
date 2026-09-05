using System;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 一次已提交任务变化的刷新上下文。
/// 订阅页面可以据此忽略自己的写入、增量更新或选择完整重载。
/// </summary>
public sealed class TaskChangedEventArgs :
    EventArgs
{
    public string TaskId
    { get; }

    public TaskChangeType ChangeType
    { get; }

    public TaskEditFields ChangedFields
    { get; }

    public long Revision
    { get; }

    public TaskChangeSource ChangeSource
    { get; }

    public bool RequiresRegroup
    { get; }

    public TaskChangedEventArgs(
        string taskId,
        TaskChangeType changeType,
        TaskEditFields changedFields,
        long revision,
        TaskChangeSource changeSource,
        bool requiresRegroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            taskId);

        TaskId =
            taskId;

        ChangeType =
            changeType;

        ChangedFields =
            changedFields;

        Revision =
            revision;

        ChangeSource =
            changeSource;

        RequiresRegroup =
            requiresRegroup;
    }
}

public enum TaskChangeType
{
    Created,
    Updated,
    CompletionChanged,
    Deleted,
    Restored,
    PermanentlyDeleted,
    ReminderDelivered
}

/// <summary>
/// 标识任务写入由哪个界面或后台服务发起。
/// </summary>
public enum TaskChangeSource
{
    Unknown,
    AllTasks,
    CompletedTasks,
    Calendar,
    MainMatrix,
    DesktopMatrix,
    DesktopTaskList,
    Trash,
    Reminder,
    System
}
