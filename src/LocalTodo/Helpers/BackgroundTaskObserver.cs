using System;
using System.Threading.Tasks;

namespace LocalTodo.Helpers;

/// <summary>
/// 观察确实不需要等待的后台任务，避免异常静默丢失。
/// </summary>
public static class BackgroundTaskObserver
{
    public static void Observe(
        Task task,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        _ = ObserveCoreAsync(
            task,
            errorMessage);
    }

    private static async Task ObserveCoreAsync(
        Task task,
        string errorMessage)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            AppLog.Error(
                errorMessage,
                exception);
        }
    }
}
