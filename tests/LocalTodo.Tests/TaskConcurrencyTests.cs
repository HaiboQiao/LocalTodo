using LocalTodo.Data;
using LocalTodo.Models;
using LocalTodo.Services;

namespace LocalTodo.Tests;

public sealed class TaskConcurrencyTests : IAsyncLifetime
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "LocalTodo.Tests",
        Guid.NewGuid().ToString("N"));

    private SqliteConnectionFactory _factory = null!;
    private TaskRepository _repository = null!;
    private TaskService _service = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        _factory = new SqliteConnectionFactory(
            Path.Combine(_temporaryDirectory, "test.db"),
            pooling: false);
        await new DatabaseInitializer(_factory).InitializeAsync();
        _repository = new TaskRepository(_factory);
        _service = new TaskService(_repository);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompletionUsesLatestDatabaseRevision()
    {
        TaskItem created = await CreateTaskAsync("完成并发测试");
        TaskItem stale = (await _repository.GetActiveTaskByIdAsync(created.Id))!;
        TaskItem editorCopy = (await _repository.GetActiveTaskByIdAsync(created.Id))!;

        await _service.UpdateTaskAsync(
            editorCopy,
            TaskEditDraft.FromTask(editorCopy) with
            {
                Description = "另一个窗口刚刚保存"
            });

        await _service.SetCompletionStateAsync(
            stale.Id,
            TodoStatus.Completed);

        TaskItem latest =
            (await _repository.GetActiveTaskByIdAsync(created.Id))!;
        Assert.Equal(TodoStatus.Completed, latest.Status);
        Assert.Equal("另一个窗口刚刚保存", latest.Description);
    }

    [Fact]
    public async Task DeleteUsesLatestDatabaseRevision()
    {
        TaskItem created = await CreateTaskAsync("删除并发测试");
        TaskItem stale = (await _repository.GetActiveTaskByIdAsync(created.Id))!;
        TaskItem editorCopy = (await _repository.GetActiveTaskByIdAsync(created.Id))!;

        await _service.UpdateTaskAsync(
            editorCopy,
            TaskEditDraft.FromTask(editorCopy) with
            {
                Description = "删除前的其他窗口修改"
            });

        await _service.DeleteTaskWithChoiceByIdAsync(
            stale.Id,
            TaskDeleteChoice.DeleteSingleTask);

        Assert.Null(await _repository.GetActiveTaskByIdAsync(created.Id));
        Assert.Contains(
            await _repository.GetDeletedTasksAsync(),
            task => task.Id == created.Id);
    }

    [Fact]
    public async Task DeletingCurrentRecurringOccurrenceStillCreatesNextOne()
    {
        TaskItem created = await _service.CreateTaskAsync(
            CreateDraft("循环删除测试") with
            {
                DueDate = DateTime.Today.AddDays(1),
                RepeatType = TaskRepeatType.Daily
            });

        TaskItem editorCopy = (await _repository.GetActiveTaskByIdAsync(created.Id))!;
        await _service.UpdateTaskAsync(
            editorCopy,
            TaskEditDraft.FromTask(editorCopy) with
            {
                Description = "并发更新后的循环任务"
            });

        TaskItem? next = await _service.DeleteTaskWithChoiceByIdAsync(
            created.Id,
            TaskDeleteChoice.DeleteCurrentOccurrence);

        Assert.NotNull(next);
        Assert.NotEqual(created.Id, next.Id);
        Assert.Equal(TaskRepeatType.Daily, next.RepeatType);
        Assert.Equal("并发更新后的循环任务", next.Description);
    }

    private Task<TaskItem> CreateTaskAsync(string title) =>
        _service.CreateTaskAsync(CreateDraft(title));

    private static TaskEditDraft CreateDraft(string title) =>
        new(
            title,
            string.Empty,
            null,
            null,
            false,
            0,
            TaskRepeatType.None,
            false,
            false,
            QuadrantType.NotImportantNotUrgent);
}
