using System.Collections.Generic;

namespace LocalTodo.Models;

/// <summary>
/// 四象限页面的一次完整查询结果。
/// </summary>
public sealed class QuadrantSnapshot
{
    public IReadOnlyList<TaskItem>
        ImportantAndUrgentTasks
    { get; init; } =
        [];

    public IReadOnlyList<TaskItem>
        ImportantNotUrgentTasks
    { get; init; } =
        [];

    public IReadOnlyList<TaskItem>
        UrgentNotImportantTasks
    { get; init; } =
        [];

    public IReadOnlyList<TaskItem>
        NotImportantNotUrgentTasks
    { get; init; } =
        [];

    public int UrgencyThresholdDays { get; init; }
}
