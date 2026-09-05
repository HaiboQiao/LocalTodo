using LocalTodo.Models;
using LocalTodo.Services;
using LocalTodo.ViewModels;

namespace LocalTodo.Tests;

public sealed class ContinuousTaskTests
{
    [Fact]
    public void FutureContinuousTaskAppearsInTodayGroup()
    {
        TaskItem task = new()
        {
            Title = "持续任务",
            Status = TodoStatus.Pending,
            IsContinuous = true,
            DueAt = TaskRules.CreateLocalDueAt(
                DateTime.Today.AddDays(5),
                null)
        };

        TaskListEntry entry = TaskListEntry.Create(
            task,
            DateTime.Today,
            0);

        Assert.Equal(1, entry.GroupOrder);
        Assert.StartsWith("今天", entry.GroupHeader);
        Assert.StartsWith("~ ", task.DueDateText);
    }

    [Fact]
    public void ContinuousFlagIsClearedWithoutDueDate()
    {
        TaskEditResult result = TaskRules.Normalize(
            new TaskEditDraft(
                "无日期任务",
                string.Empty,
                null,
                null,
                false,
                0,
                TaskRepeatType.None,
                true,
                false,
                QuadrantType.NotImportantNotUrgent));

        Assert.False(result.IsContinuous);
    }
}
