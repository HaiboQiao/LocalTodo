using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LocalTodo.Helpers;

namespace LocalTodo.Services;

/// <summary>
/// 配置 LocalTodo 桌面小组件的 Win32 窗口特性。
///
/// 当前设计目标：
///
/// 1. 小组件保持独立顶层窗口；
/// 2. 不把主窗口设置为 Owner；
/// 3. 不把 Explorer / Desktop 设置为 Owner；
/// 4. 不使用 Topmost；
/// 5. 点击哪个窗口，就由 Windows 正常把哪个窗口
///    提到当前普通窗口层级的前面；
/// 6. 主窗口最小化或隐藏时，
///    不主动修改小组件的 Z-Order；
/// 7. 保留 WS_EX_TOOLWINDOW，
///    避免小组件作为普通应用窗口出现在
///    任务栏和 Alt+Tab 中。
/// </summary>
public sealed class DesktopWidgetHostService
{
    private const int
        GwlExStyle =
            -20;

    private const int
        GwlpHwndParent =
            -8;

    private const long
        WsExToolWindow =
            0x00000080L;

    private const long
        WsExAppWindow =
            0x00040000L;

    private const long
        WsExTopmost =
            0x00000008L;

    private const uint
        SwpNoSize =
            0x0001;

    private const uint
        SwpNoMove =
            0x0002;

    private const uint
        SwpNoZOrder =
            0x0004;

    private const uint
        SwpNoActivate =
            0x0010;

    private const uint
        SwpFrameChanged =
            0x0020;

    private const uint
        SwpNoOwnerZOrder =
            0x0200;

    private IntPtr
        _widgetHwnd;

    private IntPtr
        _originalOwnerHwnd;

    private IntPtr
        _originalExtendedStyle;

    private bool
        _originalStateCaptured;

    public bool IsAttached
    {
        get;
        private set;
    }

    /// <summary>
    /// 继续保留这个消息属性，
    /// 兼容当前 MatrixWindow 和
    /// DesktopTaskListWindow 中已经存在的
    /// Explorer 重启监听代码。
    ///
    /// 新版本已经不依赖 Explorer 作为 Owner，
    /// 即使重新调用 Attach，
    /// 也只会重新确认工具窗口样式。
    /// </summary>
    public uint TaskbarCreatedMessage
    {
        get;
    }

    public DesktopWidgetHostService()
    {
        TaskbarCreatedMessage =
            RegisterWindowMessage(
                "TaskbarCreated");
    }

    /// <summary>
    /// 将窗口配置成独立桌面工具窗口。
    ///
    /// 注意：
    ///
    /// 此方法不再把 Explorer Desktop
    /// 设置成窗口 Owner。
    /// </summary>
    public bool Attach(
        Window window)
    {
        ArgumentNullException
            .ThrowIfNull(
                window);

        try
        {
            WindowInteropHelper helper =
                new(window);

            IntPtr widgetHwnd =
                helper.EnsureHandle();

            /*
             * 第一次处理当前 HWND 时，
             * 保存 WPF 原始状态。
             *
             * 正式退出程序时可以恢复。
             */
            if (_widgetHwnd !=
                    widgetHwnd ||
                !_originalStateCaptured)
            {
                _widgetHwnd =
                    widgetHwnd;

                _originalOwnerHwnd =
                    GetWindowLongPtrCompat(
                        widgetHwnd,
                        GwlpHwndParent);

                _originalExtendedStyle =
                    GetWindowLongPtrCompat(
                        widgetHwnd,
                        GwlExStyle);

                _originalStateCaptured =
                    true;
            }

            /*
             * =====================================
             * 关键修改 1：
             *
             * 明确清除 Win32 Owner。
             *
             * 不再设置：
             *
             * Explorer
             * Progman
             * WorkerW
             * MainWindow
             *
             * 这样小组件就是一个真正独立的
             * 顶层窗口。
             * =====================================
             */

            SetWindowLongPtrChecked(
                widgetHwnd,
                GwlpHwndParent,
                IntPtr.Zero);

            /*
             * =====================================
             * 关键修改 2：
             *
             * 保留 TOOLWINDOW。
             *
             * 它只负责工具窗口外观/任务切换行为，
             * 不负责强制 Z-Order。
             * =====================================
             */

            long extendedStyle =
                GetWindowLongPtrCompat(
                    widgetHwnd,
                    GwlExStyle)
                .ToInt64();

            extendedStyle |=
                WsExToolWindow;

            extendedStyle &=
                ~WsExAppWindow;

            extendedStyle &=
                ~WsExTopmost;

            SetWindowLongPtrChecked(
                widgetHwnd,
                GwlExStyle,
                new IntPtr(
                    extendedStyle));

            /*
             * 通知 Windows 刷新窗口样式。
             *
             * 特别注意 SWP_NOZORDER：
             *
             * 这里绝对不主动调整窗口前后顺序。
             */
            SetWindowPosChecked(
                widgetHwnd,
                IntPtr.Zero,
                SwpNoMove |
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate |
                SwpFrameChanged);

            IsAttached =
                true;

            AppLog.Information(
                "桌面小组件已配置为独立工具窗口。" +
                "未设置 Explorer Owner，" +
                "窗口 Z-Order 交由 Windows 正常管理。");

            return true;
        }
        catch (Exception exception)
        {
            IsAttached =
                false;

            AppLog.Error(
                "配置桌面小组件独立窗口模式失败。",
                exception);

            return false;
        }
    }

    /// <summary>
    /// 将非激活显示的小组件放在当前前台窗口之后。
    /// 用于开机恢复，既不抢焦点，也不会形成“前台窗口在下面、
    /// 非激活小组件却浮在上面”的反常层级。
    /// </summary>
    public void PlaceBehindForegroundWithoutActivation(
        Window window)
    {
        ArgumentNullException.ThrowIfNull(
            window);

        if (!window.IsVisible)
        {
            return;
        }

        try
        {
            WindowInteropHelper helper =
                new(window);

            IntPtr hwnd =
                helper.Handle;

            if (hwnd ==
                IntPtr.Zero)
            {
                return;
            }

            IntPtr foregroundHwnd =
                GetForegroundWindow();

            if (foregroundHwnd == IntPtr.Zero ||
                foregroundHwnd == hwnd)
            {
                return;
            }

            long foregroundExtendedStyle =
                GetWindowLongPtrCompat(
                    foregroundHwnd,
                    GwlExStyle)
                .ToInt64();

            // 非 Topmost 小组件天然位于 Topmost 应用之后，
            // 不用 topmost HWND 作为 insert-after，避免跨越窗口层级带。
            if ((foregroundExtendedStyle & WsExTopmost) != 0)
            {
                return;
            }

            SetWindowPosChecked(
                hwnd,
                foregroundHwnd,
                SwpNoMove |
                SwpNoSize |
                SwpNoActivate |
                SwpNoOwnerZOrder);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "设置桌面小组件后台恢复层级失败。",
                exception);
        }
    }

    /// <summary>
    /// 正式退出程序时，
    /// 恢复窗口创建时的 Win32 状态。
    /// </summary>
    public void Detach(
        Window window)
    {
        ArgumentNullException
            .ThrowIfNull(
                window);

        if (_widgetHwnd ==
                IntPtr.Zero ||
            !_originalStateCaptured)
        {
            IsAttached =
                false;

            return;
        }

        try
        {
            /*
             * 恢复原始 Owner。
             */
            SetWindowLongPtrChecked(
                _widgetHwnd,
                GwlpHwndParent,
                _originalOwnerHwnd);

            /*
             * 恢复原始扩展样式。
             */
            SetWindowLongPtrChecked(
                _widgetHwnd,
                GwlExStyle,
                _originalExtendedStyle);

            SetWindowPosChecked(
                _widgetHwnd,
                IntPtr.Zero,
                SwpNoMove |
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate |
                SwpFrameChanged);
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "恢复桌面小组件原始窗口状态失败。",
                exception);
        }
        finally
        {
            IsAttached =
                false;

            _widgetHwnd =
                IntPtr.Zero;

            _originalOwnerHwnd =
                IntPtr.Zero;

            _originalExtendedStyle =
                IntPtr.Zero;

            _originalStateCaptured =
                false;
        }
    }

    private static void
        SetWindowLongPtrChecked(
            IntPtr hwnd,
            int index,
            IntPtr newValue)
    {
        Marshal.SetLastPInvokeError(
            0);

        IntPtr previousValue =
            SetWindowLongPtrCompat(
                hwnd,
                index,
                newValue);

        int errorCode =
            Marshal.GetLastPInvokeError();

        /*
         * SetWindowLongPtr 返回 0
         * 并不一定代表失败，
         * 因为原值本身也可能就是 0。
         */
        if (previousValue ==
                IntPtr.Zero &&
            errorCode !=
                0)
        {
            throw new Win32Exception(
                errorCode);
        }
    }

    private static void
        SetWindowPosChecked(
            IntPtr hwnd,
            IntPtr insertAfter,
            uint flags)
    {
        bool succeeded =
            SetWindowPos(
                hwnd,
                insertAfter,
                0,
                0,
                0,
                0,
                flags);

        if (!succeeded)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }
    }

    private static IntPtr
        GetWindowLongPtrCompat(
            IntPtr hwnd,
            int index)
    {
        return IntPtr.Size ==
                8
            ? GetWindowLongPtr64(
                hwnd,
                index)
            : new IntPtr(
                GetWindowLong32(
                    hwnd,
                    index));
    }

    private static IntPtr
        SetWindowLongPtrCompat(
            IntPtr hwnd,
            int index,
            IntPtr newValue)
    {
        return IntPtr.Size ==
                8
            ? SetWindowLongPtr64(
                hwnd,
                index,
                newValue)
            : new IntPtr(
                SetWindowLong32(
                    hwnd,
                    index,
                    newValue.ToInt32()));
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint =
            "RegisterWindowMessageW",
        SetLastError = true)]
    private static extern uint
        RegisterWindowMessage(
            string messageName);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr
        GetForegroundWindow();

    [DllImport(
        "user32.dll",
        EntryPoint =
            "GetWindowLongW",
        SetLastError = true)]
    private static extern int
        GetWindowLong32(
            IntPtr hwnd,
            int index);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr
        GetWindowLongPtr64(
            IntPtr hwnd,
            int index);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "SetWindowLongW",
        SetLastError = true)]
    private static extern int
        SetWindowLong32(
            IntPtr hwnd,
            int index,
            int newValue);

    [DllImport(
        "user32.dll",
        EntryPoint =
            "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr
        SetWindowLongPtr64(
            IntPtr hwnd,
            int index,
            IntPtr newValue);
}
