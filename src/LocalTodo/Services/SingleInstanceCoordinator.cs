using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LocalTodo.Helpers;

namespace LocalTodo.Services;

/// <summary>
/// 保证同一 Windows 登录会话中只运行一个 LocalTodo 实例。
/// 第二次启动只向现有实例发送“显示主窗口”信号。
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName =
        @"Local\HaiboQiao.LocalTodo.SingleInstance.v1";

    private const string ActivationEventName =
        @"Local\HaiboQiao.LocalTodo.Activate.v1";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _stopSource = new();
    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(
            initiallyOwned: true,
            MutexName,
            out bool createdNew);

        IsPrimaryInstance = createdNew;

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
    }

    public bool IsPrimaryInstance { get; }

    public void SignalPrimaryInstance()
    {
        if (IsPrimaryInstance || _disposed)
        {
            return;
        }

        _activationEvent.Set();
    }

    public void StartActivationListener(
        Func<Task> activateAsync)
    {
        ArgumentNullException.ThrowIfNull(activateAsync);

        if (!IsPrimaryInstance ||
            _disposed ||
            _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(
            () => ListenForActivationAsync(activateAsync));
    }

    private async Task ListenForActivationAsync(
        Func<Task> activateAsync)
    {
        WaitHandle[] handles =
        [
            _activationEvent,
            _stopSource.Token.WaitHandle
        ];

        while (!_stopSource.IsCancellationRequested)
        {
            int signaledHandle = WaitHandle.WaitAny(handles);

            if (signaledHandle != 0 ||
                _stopSource.IsCancellationRequested)
            {
                return;
            }

            try
            {
                Application? application = Application.Current;

                if (application is null ||
                    application.Dispatcher.HasShutdownStarted)
                {
                    continue;
                }

                await application.Dispatcher
                    .InvokeAsync(activateAsync)
                    .Task
                    .Unwrap();
            }
            catch (Exception exception)
            {
                AppLog.Error(
                    "响应第二次启动的窗口激活请求失败。",
                    exception);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopSource.Cancel();
        _activationEvent.Set();

        try
        {
            _listenerTask?.Wait(
                TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // 应用正在退出，监听任务的取消异常无需继续传播。
        }

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 互斥锁已由系统释放时无需再次处理。
            }
        }

        _activationEvent.Dispose();
        _mutex.Dispose();
        _stopSource.Dispose();
    }
}
