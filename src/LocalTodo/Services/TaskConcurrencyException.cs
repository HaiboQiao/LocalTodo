using System;

namespace LocalTodo.Services;

/// <summary>
/// 完成、删除等专用命令使用过期任务版本时抛出。
/// </summary>
public sealed class TaskConcurrencyException :
    InvalidOperationException
{
    public TaskConcurrencyException(
        string taskId)
        : base(
            $"任务已在其他窗口发生变化，请刷新后重试：{taskId}")
    {
        TaskId =
            taskId;
    }

    public string TaskId
    { get; }
}
