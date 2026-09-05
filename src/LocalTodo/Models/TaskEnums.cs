namespace LocalTodo.Models;

/// <summary>
/// 任务完成状态。
/// </summary>
public enum TodoStatus
{
    Pending = 0,
    Completed = 1
}

/// <summary>
/// 任务优先级。
/// </summary>
public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// 四象限归类方式。
/// </summary>
public enum QuadrantMode
{
    Automatic = 0,
    Manual = 1
}

/// <summary>
/// 四象限类型。
/// </summary>
public enum QuadrantType
{
    ImportantAndUrgent = 1,
    ImportantNotUrgent = 2,
    UrgentNotImportant = 3,
    NotImportantNotUrgent = 4
}

/// <summary>
/// 任务循环方式。
/// </summary>
public enum TaskRepeatType
{
    None = 0,

    Daily = 1,

    Weekly = 2,

    Monthly = 3,

    Yearly = 4,

    Weekdays = 5
}
