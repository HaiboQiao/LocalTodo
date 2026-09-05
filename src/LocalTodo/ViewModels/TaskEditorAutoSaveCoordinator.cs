using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using LocalTodo.Models;

namespace LocalTodo.ViewModels;

/// <summary>
/// 管理任务详情编辑器的防抖、在途保存和重试状态。
///
/// 具体如何构造 TaskEditDraft、如何写数据库以及如何显示状态，
/// 仍由所属页面决定；此类只负责所有编辑器共有的生命周期协议。
/// </summary>
public sealed class TaskEditorAutoSaveCoordinator
{
    private readonly DispatcherTimer
        _timer;

    private readonly Func<bool>
        _hasEditingTarget;

    private readonly Func<Task<bool>>
        _saveAsync;

    private readonly Action<Exception>
        _onUnhandledException;

    private Task<bool>?
        _activeSaveTask;

    public TaskEditorAutoSaveCoordinator(
        TimeSpan delay,
        Func<bool> hasEditingTarget,
        Func<Task<bool>> saveAsync,
        Action<Exception> onUnhandledException)
    {
        ArgumentNullException.ThrowIfNull(
            hasEditingTarget);

        ArgumentNullException.ThrowIfNull(
            saveAsync);

        ArgumentNullException.ThrowIfNull(
            onUnhandledException);

        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay));
        }

        _hasEditingTarget =
            hasEditingTarget;

        _saveAsync =
            saveAsync;

        _onUnhandledException =
            onUnhandledException;

        _timer =
            new DispatcherTimer
            {
                Interval =
                    delay
            };

        _timer.Tick +=
            OnTimerTick;
    }

    public bool HasPendingSave { get; internal set; }

    public bool HasConflict { get; internal set; }

    public TaskEditFields DirtyFields { get; internal set; }

    public void Schedule(
        TaskEditFields changedFields)
    {
        if (changedFields ==
            TaskEditFields.None)
        {
            return;
        }

        HasPendingSave =
            true;

        DirtyFields |=
            changedFields;

        HasConflict =
            false;

        _timer.Stop();
        _timer.Start();
    }

    public TaskEditFields TakeDirtyFields()
    {
        TaskEditFields fields =
            DirtyFields;

        DirtyFields =
            TaskEditFields.None;

        return fields;
    }

    public void KeepPending()
    {
        HasPendingSave =
            true;
    }

    public void MarkConflict(
        TaskEditFields unsavedFields)
    {
        DirtyFields |=
            unsavedFields;

        HasConflict =
            true;
    }

    public void MarkRetryRequired(
        TaskEditFields unsavedFields)
    {
        DirtyFields |=
            unsavedFields;

        HasPendingSave =
            true;
    }

    public void MarkSaved()
    {
        HasConflict =
            false;
    }

    public void Reset()
    {
        _timer.Stop();

        HasPendingSave =
            false;

        HasConflict =
            false;

        DirtyFields =
            TaskEditFields.None;
    }

    public void StopDebounce()
    {
        _timer.Stop();
    }

    public async Task<bool> FlushAsync()
    {
        _timer.Stop();

        while (true)
        {
            Task<bool>? activeSaveTask =
                _activeSaveTask;

            if (activeSaveTask is not null)
            {
                if (!await activeSaveTask)
                {
                    return false;
                }

                continue;
            }

            if (HasConflict &&
                !HasPendingSave)
            {
                return false;
            }

            if (!HasPendingSave ||
                !_hasEditingTarget())
            {
                return true;
            }

            HasPendingSave =
                false;

            Task<bool> saveTask =
                _saveAsync();

            _activeSaveTask =
                saveTask;

            bool succeeded;

            try
            {
                succeeded =
                    await saveTask;
            }
            finally
            {
                if (ReferenceEquals(
                        _activeSaveTask,
                        saveTask))
                {
                    _activeSaveTask =
                        null;
                }
            }

            if (!succeeded)
            {
                return false;
            }
        }
    }

    private async void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        try
        {
            await FlushAsync();
        }
        catch (Exception exception)
        {
            _onUnhandledException(
                exception);
        }
    }
}
