using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LocalTodo.Helpers;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// LocalTodo 本地任务提醒服务。
///
/// 程序运行期间定期检查已经到达截止时间、
/// 尚未完成并且尚未提醒的任务。
/// </summary>
public sealed class ReminderService :
    IDisposable
{
    private readonly TaskService
        _taskService;

    private readonly TrayIconService
        _trayIconService;

    private readonly IClock
        _clock;

    private readonly DispatcherTimer
        _timer;

    private readonly CancellationTokenSource
        _stopCancellationSource =
            new();

    private bool
        _isChecking;

    private bool
        _isStarted;

    public ReminderService(
        TaskService taskService,
        TrayIconService trayIconService,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(
            taskService);

        ArgumentNullException.ThrowIfNull(
            trayIconService);

        _taskService =
            taskService;

        _trayIconService =
            trayIconService;

        _clock =
            clock ??
            SystemClock.Instance;

        _timer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(
                        20)
            };

        _timer.Tick +=
            OnTimerTick;
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted =
            true;

        _timer.Start();

        /*
         * 程序刚启动时立即检查一次，
         * 避免必须等 20 秒。
         */
        BackgroundTaskObserver.Observe(
            CheckRemindersAsync(
                _stopCancellationSource.Token),
            "启动时检查任务提醒失败。");
    }

    public void Stop()
    {
        _timer.Stop();

        _stopCancellationSource.Cancel();

        _isStarted =
            false;
    }

    public void Dispose()
    {
        Stop();

        _timer.Tick -=
            OnTimerTick;

        _stopCancellationSource.Dispose();
    }

    private async void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        try
        {
            await CheckRemindersAsync(
                _stopCancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            // 正式退出时主动终止正在进行的提醒检查。
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "检查任务提醒失败。",
                exception);
        }
    }

    private async Task
        CheckRemindersAsync(
            CancellationToken cancellationToken)
    {
        if (_isChecking)
        {
            return;
        }

        _isChecking =
            true;

        try
        {
            IReadOnlyList<TaskItem> tasks =
                await _taskService
                    .GetDueRemindersAsync(
                        _clock.UtcNow,
                        cancellationToken);

            foreach (TaskItem task
                     in tasks)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                /*
                 * 先展示提醒。
                 */
                _trayIconService
                    .ShowTaskReminder(
                        task);

                DateTimeOffset deliveredAt =
                    _clock.UtcNow;

                /*
                 * 然后记录本期已经提醒过。
                 */
                await _taskService
                    .MarkReminderDeliveredAsync(
                        task,
                        deliveredAt,
                        cancellationToken,
                        TaskChangeSource.Reminder);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            // 应用正在退出，不记录为提醒检查失败。
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "任务提醒后台检查失败。",
                exception);
        }
        finally
        {
            _isChecking =
                false;
        }
    }
}
