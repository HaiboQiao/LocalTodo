namespace LocalTodo.Models;

/// <summary>
/// 编辑器提交尚未持久化内容的结果。
/// </summary>
public sealed record FlushResult(
    FlushStatus Status,
    string Message)
{
    public bool Succeeded =>
        Status == FlushStatus.Succeeded;

    public static FlushResult Success()
    {
        return new FlushResult(
            FlushStatus.Succeeded,
            string.Empty);
    }

    public static FlushResult Blocked(
        string message)
    {
        return new FlushResult(
            FlushStatus.Blocked,
            message);
    }

    public static FlushResult Failed(
        string message)
    {
        return new FlushResult(
            FlushStatus.Failed,
            message);
    }

    public static FlushResult TimedOut(
        string message)
    {
        return new FlushResult(
            FlushStatus.TimedOut,
            message);
    }
}

public enum FlushStatus
{
    Succeeded = 0,
    Blocked = 1,
    Failed = 2,
    TimedOut = 3
}
