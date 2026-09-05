using System;
using System.Windows.Threading;

namespace LocalTodo.Services;

/// <summary>
/// 为所有需要时间敏感显示的页面提供唯一的一分钟时钟事件。
/// 页面只在分组或过期状态真正变化时重建本地视图。
/// </summary>
public sealed class TaskTimeRefreshService :
    IDisposable
{
    private readonly DispatcherTimer
        _timer;

    private readonly IClock
        _clock;

    private readonly ILocalTimeService
        _localTimeService;

    private bool
        _isDisposed;

    public event EventHandler<TaskTimeRefreshEventArgs>?
        RefreshRequested;

    public DateTime Today =>
        _localTimeService
            .ToLocalDateTime(
                _clock.UtcNow)
            .Date;

    public TaskTimeRefreshService(
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
    {
        _clock =
            clock ??
            SystemClock.Instance;

        _localTimeService =
            localTimeService ??
            LocalTimeService.System;

        _timer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMinutes(1)
            };

        _timer.Tick +=
            OnTimerTick;

        _timer.Start();
    }

    private void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        Publish(
            _clock.UtcNow);
    }

    private void Publish(
        DateTimeOffset now)
    {
        RefreshRequested?.Invoke(
            this,
            new TaskTimeRefreshEventArgs(
                now,
                _localTimeService
                    .ToLocalDateTime(
                        now)
                    .Date));
    }

    internal void RequestRefreshForTesting(
        DateTimeOffset now)
    {
        Publish(
            now);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed =
            true;

        _timer.Stop();

        _timer.Tick -=
            OnTimerTick;
    }
}

public sealed class TaskTimeRefreshEventArgs(
    DateTimeOffset now,
    DateTime today) :
    EventArgs
{
    public DateTimeOffset Now
    { get; } = now;

    public DateTime Today
    { get; } = today.Date;
}
