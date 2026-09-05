namespace LocalTodo.Models;

public enum TaskEditSaveStatus
{
    Saved = 0,
    Merged = 1,
    Conflict = 2,
    TargetUnavailable = 3
}

/// <summary>
/// 普通详情编辑的乐观并发保存结果。
/// </summary>
public sealed record TaskEditSaveResult(
    TaskEditSaveStatus Status,
    TaskEditBaseline? Current,
    TaskEditFields ConflictingFields)
{
    public bool IsSaved =>
        Status is
            TaskEditSaveStatus.Saved or
            TaskEditSaveStatus.Merged;

    public bool WasMerged =>
        Status ==
            TaskEditSaveStatus.Merged;
}
