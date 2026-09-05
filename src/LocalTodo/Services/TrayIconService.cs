using System;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LocalTodo.Services;

/// <summary>
/// 管理 Windows 通知区域中的 LocalTodo 图标。
/// </summary>
public sealed class TrayIconService :
    IDisposable
{
    private readonly ApplicationWindowService
        _applicationWindowService;

    private Forms.NotifyIcon?
        _notifyIcon;

    private Forms.ContextMenuStrip?
        _contextMenu;

    private Drawing.Icon?
        _trayIcon;

    private bool
        _isStarted;

    public TrayIconService(
        ApplicationWindowService applicationWindowService)
    {
        _applicationWindowService =
            applicationWindowService;
    }

    /// <summary>
    /// 创建并显示系统托盘图标。
    /// </summary>
    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted =
            true;

        Forms.ToolStripMenuItem
            openMainWindowItem =
                new("打开主窗口");

        Forms.ToolStripMenuItem
            openDesktopTaskListWindowItem =
                new("打开桌面任务列表");

        Forms.ToolStripMenuItem
            openMatrixWindowItem =
                new("打开桌面四象限");

        Forms.ToolStripMenuItem
            exitItem =
                new("退出 LocalTodo");

        Forms.ToolStripSeparator separator =
            new();

        openMainWindowItem.Click +=
            async (_, _) =>
                await ExecuteSafelyAsync(
                    _applicationWindowService
                        .ShowMainWindowAsync,
                    "从托盘打开主窗口失败。");

        openDesktopTaskListWindowItem.Click +=
            async (_, _) =>
                await ExecuteSafelyAsync(
                    _applicationWindowService
                        .ShowDesktopTaskListWindowAsync,
                    "从托盘打开桌面任务列表失败。");

        openMatrixWindowItem.Click +=
            async (_, _) =>
                await ExecuteSafelyAsync(
                    _applicationWindowService
                        .ShowMatrixWindowAsync,
                    "从托盘打开四象限窗口失败。");

        exitItem.Click +=
            async (_, _) =>
                await ExecuteSafelyAsync(
                    _applicationWindowService
                        .ExitApplicationAsync,
                    "从托盘退出程序失败。");

        _contextMenu =
            new Forms.ContextMenuStrip();

        _contextMenu.Items.AddRange(
            new Forms.ToolStripItem[]
            {
                openMainWindowItem,
                openDesktopTaskListWindowItem,
                openMatrixWindowItem,
                separator,
                exitItem
            });

        _trayIcon =
            LoadTrayIcon();

        _notifyIcon =
            new Forms.NotifyIcon
            {
                Icon =
                    _trayIcon,

                Text =
                    "LocalTodo",

                ContextMenuStrip =
                    _contextMenu,

                Visible =
                    true
            };

        _notifyIcon.DoubleClick +=
            async (_, _) =>
                await ExecuteSafelyAsync(
                    _applicationWindowService
                        .ShowMainWindowAsync,
                    "双击托盘图标打开主窗口失败。");

        AppLog.Information(
            "系统托盘图标已启动。");
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// 正式退出时先移除所有托盘入口，阻止产生新的窗口操作。
    /// </summary>
    public void Stop()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible =
                false;

            _notifyIcon.Dispose();

            _notifyIcon =
                null;
        }

        _contextMenu?.Dispose();
        _contextMenu =
            null;

        _trayIcon?.Dispose();
        _trayIcon =
            null;

        _isStarted =
            false;
    }

    /// <summary>
    /// 显示任务截止提醒。
    /// </summary>
    public void ShowTaskReminder(
        TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        if (_notifyIcon is null ||
            !_isStarted)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle =
            "LocalTodo 任务提醒";

        DateTime? localDueAt =
            task.DueAt.HasValue
                ? LocalDueDateTime.GetWallClock(
                    task.DueAt.Value)
                : null;

        _notifyIcon.BalloonTipText =
            localDueAt.HasValue
                ? $"“{task.Title}”提醒时间已到。\n" +
                  $"截止：{localDueAt.Value:M月d日 HH:mm}"
                : $"“{task.Title}”提醒时间已到。";

        _notifyIcon.BalloonTipIcon =
            Forms.ToolTipIcon.Info;

        _notifyIcon.ShowBalloonTip(
            8000);
    }

    /// <summary>
    /// 从 WPF 嵌入资源中加载 LocalTodo 托盘图标。
    ///
    /// 如果图标资源加载失败，使用系统默认应用图标兜底，
    /// 避免因为图标文件问题导致整个程序无法启动。
    /// </summary>
    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            Uri iconUri =
                new(
                    "pack://application:,,,/" +
                    "Resources/Icons/LocalTodo.ico",
                    UriKind.Absolute);

            var resourceInfo =
                System.Windows.Application
                    .GetResourceStream(
                        iconUri);

            if (resourceInfo is null)
            {
                throw new InvalidOperationException(
                    "未找到嵌入的 LocalTodo.ico 图标资源。");
            }

            using var iconStream =
                resourceInfo.Stream;

            using Drawing.Icon sourceIcon =
                new(
                    iconStream);

            /*
             * 克隆后再释放资源流，
             * 避免托盘图标继续依赖已经关闭的流。
             */
            return (Drawing.Icon)
                sourceIcon.Clone();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "加载 LocalTodo 系统托盘图标失败，" +
                "已回退到系统默认图标。",
                exception);

            return (Drawing.Icon)
                Drawing.SystemIcons.Application
                    .Clone();
        }
    }

    private static async Task
        ExecuteSafelyAsync(
            Func<Task> action,
            string errorMessage)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                errorMessage,
                exception);
        }
    }
}
