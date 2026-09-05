using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LocalTodo.Data;
using LocalTodo.Helpers;
using LocalTodo.Models;
using LocalTodo.Services;
using LocalTodo.ViewModels;
using LocalTodo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LocalTodo;

public partial class App :
    Application
{
    private ServiceProvider?
        _serviceProvider;

    private SingleInstanceCoordinator?
        _singleInstanceCoordinator;

    public App()
    {
        DispatcherUnhandledException +=
            OnDispatcherUnhandledException;
    }

    protected override async void OnStartup(
    StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceCoordinator =
            new SingleInstanceCoordinator();

        if (!_singleInstanceCoordinator
            .IsPrimaryInstance)
        {
            _singleInstanceCoordinator
                .SignalPrimaryInstance();

            Shutdown();
            return;
        }

        /*
         * Windows 开机启动命令：
         *
         * LocalTodo.exe --startup
         *
         * 普通手动启动没有 --startup。
         */
        bool isWindowsStartupLaunch =
            Array.Exists(
                e.Args,
                argument =>
                    string.Equals(
                        argument,
                        StartupService.StartupArgument,
                        StringComparison.OrdinalIgnoreCase));

        try
        {
            AppPaths.EnsureDirectories();

            AppLog.Information(
                isWindowsStartupLaunch
                    ? "LocalTodo 正在以 Windows 开机启动模式启动。"
                    : "LocalTodo 正在正常启动。");

            ServiceCollection services =
                new();

            ConfigureServices(
                services);

            _serviceProvider =
                services.BuildServiceProvider(
                    new ServiceProviderOptions
                    {
                        ValidateOnBuild =
                            true,

                        ValidateScopes =
                            true
                    });

            /*
             * 将旧版本：
             *
             * "LocalTodo.exe"
             *
             * 安全升级成：
             *
             * "LocalTodo.exe" --startup
             *
             * 用户没有启用开机启动时不会创建新启动项。
             */
            StartupService startupService =
                _serviceProvider
                    .GetRequiredService<
                        StartupService>();

            startupService
                .UpgradeLegacyRegistrationIfNeeded();

            DatabaseMaintenanceService
                databaseMaintenanceService =
                    _serviceProvider
                        .GetRequiredService<
                            DatabaseMaintenanceService>();

            DatabaseRestoreResult restoreResult =
                await databaseMaintenanceService
                    .ApplyPendingRestoreAsync();

            if (restoreResult.Applied)
            {
                AppLog.Information(
                    "已在数据库初始化前应用待恢复副本。" +
                    (string.IsNullOrWhiteSpace(
                        restoreResult.SafetyBackupFile)
                        ? string.Empty
                        : "恢复前安全备份：" +
                          restoreResult.SafetyBackupFile));
            }

            DatabaseInitializer databaseInitializer =
                _serviceProvider
                    .GetRequiredService<
                        DatabaseInitializer>();

            await databaseInitializer
                .InitializeAsync();

            MainWindowViewModel mainWindowViewModel =
                _serviceProvider
                    .GetRequiredService<
                        MainWindowViewModel>();

            await mainWindowViewModel
                .InitializeAsync();

            /*
             * 无论什么启动方式，
             * 托盘图标都必须先建立。
             */
            TrayIconService trayIconService =
                _serviceProvider
                    .GetRequiredService<
                        TrayIconService>();

            trayIconService.Start();

            /*
             * 后台提醒服务也继续正常工作。
             */
            ReminderService reminderService =
                _serviceProvider
                    .GetRequiredService<
                        ReminderService>();

            reminderService.Start();

            ApplicationWindowService
                applicationWindowService =
                    _serviceProvider
                        .GetRequiredService<
                            ApplicationWindowService>();

            _singleInstanceCoordinator
                .StartActivationListener(
                    applicationWindowService
                        .ShowMainWindowAsync);

            /*
             * 普通启动：
             * 正常显示主窗口。
             *
             * Windows 开机启动：
             * 主窗口保持隐藏，
             * LocalTodo 直接驻留托盘。
             */
            if (!isWindowsStartupLaunch)
            {
                await applicationWindowService
                    .ShowMainWindowAsync();
            }
            else
            {
                AppLog.Information(
                    "开机启动模式：主窗口保持隐藏。");
            }

            /*
             * 小组件恢复与主窗口是否显示无关。
             *
             * 用户之前打开哪个，
             * Windows 登录后就恢复哪个。
             */
            await applicationWindowService
                .RestoreDesktopWidgetsAsync();

            AppLog.Information(
                "LocalTodo 启动完成。");
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "LocalTodo 启动失败。",
                exception);

            string errorMessage =
                "程序启动失败。\n\n" +
                $"错误类型：" +
                $"{exception.GetType().FullName}\n\n" +
                $"错误信息：" +
                $"{exception.Message}\n\n" +
                $"日志目录：" +
                $"{AppPaths.LogDirectory}";

            MessageBox.Show(
                errorMessage,
                "LocalTodo 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    /// <summary>
    /// Windows 用户注销或系统关机时触发。
    ///
    /// 这里必须在 WPF 真正关闭各窗口之前，
    /// 告诉 ApplicationWindowService：
    /// 当前是整个 Windows 会话结束，
    /// 不是用户主动关闭某个桌面小组件。
    ///
    /// 否则 MatrixWindow / DesktopTaskListWindow
    /// 的 Closing 事件会调用 Hide...WindowAsync，
    /// 从而错误地把小组件启用状态保存为 false。
    /// </summary>
    protected override void OnSessionEnding(
        SessionEndingCancelEventArgs e)
    {
        try
        {
            if (_serviceProvider is not null)
            {
                ApplicationWindowService
                    applicationWindowService =
                        _serviceProvider
                            .GetRequiredService<
                                ApplicationWindowService>();

                applicationWindowService
                    .BeginSystemSessionEnding();

                /*
                 * Windows 会话结束事件本身是同步的。
                 * 使用有截止时间的 DispatcherFrame 继续处理 UI
                 * continuation，使编辑器有机会完成最后一次保存，
                 * 同时绝不无限阻止系统注销或关机。
                 */
                Task<FlushResult> flushTask =
                    applicationWindowService
                        .FlushForSystemSessionEndingAsync(
                            TimeSpan.FromMilliseconds(
                                1500));

                FlushResult flushResult =
                    WaitForSessionFlush(
                        flushTask,
                        TimeSpan.FromSeconds(2));

                if (!flushResult.Succeeded)
                {
                    AppLog.Information(
                        "Windows 会话结束时未能完成全部保存：" +
                        flushResult.Message);
                }

                AppLog.Information(
                    $"收到 Windows 会话结束通知：" +
                    $"{e.ReasonSessionEnding}");
            }
        }
        catch (Exception exception)
        {
            /*
             * 会话结束处理不能因为状态标记失败
             * 而阻止 Windows 正常注销或关机。
             *
             * 这里只记录错误，
             * 不设置 e.Cancel。
             */
            AppLog.Error(
                "处理 Windows 会话结束通知失败。",
                exception);
        }

        /*
         * 不取消 Windows 注销/关机。
         *
         * 继续执行 WPF 默认会话结束流程。
         */
        base.OnSessionEnding(e);
    }

    private static FlushResult WaitForSessionFlush(
        Task<FlushResult> flushTask,
        TimeSpan maximumWait)
    {
        if (flushTask.IsCompleted)
        {
            return flushTask
                .GetAwaiter()
                .GetResult();
        }

        DispatcherFrame frame =
            new();

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        DispatcherTimer timer =
            new(
                DispatcherPriority.Send)
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        20)
            };

        timer.Tick +=
            (_, _) =>
            {
                if (flushTask.IsCompleted ||
                    stopwatch.Elapsed >=
                        maximumWait)
                {
                    frame.Continue =
                        false;
                }
            };

        timer.Start();

        try
        {
            Dispatcher.PushFrame(
                frame);
        }
        finally
        {
            timer.Stop();
            stopwatch.Stop();
        }

        return flushTask.IsCompleted
            ? flushTask
                .GetAwaiter()
                .GetResult()
            : FlushResult.TimedOut(
                "超过 Windows 会话结束保存时限");
    }

    protected override async void OnExit(
        ExitEventArgs e)
    {
        try
        {
            if (_serviceProvider is not null)
            {
                await _serviceProvider
                    .DisposeAsync();

                _serviceProvider =
                    null;
            }

            AppLog.Information(
                "LocalTodo 已退出。");
        }
        finally
        {
            _singleInstanceCoordinator?
                .Dispose();

            _singleInstanceCoordinator =
                null;

            base.OnExit(e);
        }
    }

    private static void ConfigureServices(
        IServiceCollection services)
    {
        services.AddSingleton<IClock>(
            SystemClock.Instance);

        services.AddSingleton<ILocalTimeService>(
            LocalTimeService.System);

        // 数据访问

        services.AddSingleton<
            SqliteConnectionFactory>();

        services.AddSingleton<
            DatabaseInitializer>();

        services.AddSingleton<
            DatabaseMaintenanceService>();

        services.AddSingleton<
            TaskRepository>();

        services.AddSingleton<
            WeeklyPlanRepository>();

        services.AddSingleton<
            AchievementRepository>();

        services.AddSingleton<
            AchievementCategoryRepository>();

        services.AddSingleton<
            AppSettingRepository>();

        // 业务服务


        services.AddSingleton<
            TaskService>();

        services.AddSingleton<
            WeeklyPlanService>();

        services.AddSingleton<
            AchievementService>();

        services.AddSingleton<
            AchievementCategoryService>();

        services.AddSingleton<
            QuadrantService>();

        services.AddSingleton<
            WindowPlacementService>();

        services.AddSingleton<
            StartupService>();

        services.AddSingleton<
            DesktopWidgetStateService>();

        services.AddSingleton<
            PendingChangesCoordinator>();

        services.AddSingleton<
            TaskTimeRefreshService>();

        services.AddTransient<
            DesktopWidgetHostService>();

        services.AddSingleton<
            ApplicationWindowService>();

        services.AddSingleton<
            TrayIconService>();

        services.AddSingleton<
            ReminderService>();

        services.AddSingleton<
            DialogService>();

        services.AddSingleton<
            LunarCalendarService>();

        // ViewModel

        services.AddSingleton<
            MatrixTaskStore>();

        services.AddSingleton<
            MainMatrixSessionViewModel>();

        services.AddSingleton<
            DesktopMatrixSessionViewModel>();

        services.AddSingleton<
            CalendarViewModel>();

        services.AddSingleton<
            WeeklyPlanViewModel>();

        services.AddSingleton<
            AchievementViewModel>();

        services.AddSingleton<
            DesktopTaskListViewModel>();

        services.AddSingleton<
            MainWindowViewModel>();

        // Window

        services.AddSingleton<
            MainWindow>();

        services.AddSingleton<
            MatrixWindow>();

        services.AddSingleton<
            DesktopTaskListWindow>();
    }

    private void
        OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(
            "程序发生未处理异常。",
            e.Exception);

        MessageBox.Show(
            "程序发生异常并即将关闭，" +
            "请查看 Logs 文件夹。",
            "LocalTodo",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled =
            true;

        Shutdown(-1);
    }
}
