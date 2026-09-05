using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace LocalTodo.Helpers;

/// <summary>
/// 修复 WPF Popup 在整个应用失去前台状态以后，
/// Popup 内输入控件无法重新获得真实键盘焦点的问题。
///
/// WPF Popup 使用独立 HWND。
/// 因此单纯调用 Keyboard.Focus / Control.Focus
/// 并不总能恢复 Popup 子窗口的 Win32 焦点。
/// </summary>
public static class PopupFocusHelper
{
    /// <summary>
    /// 恢复指定 Popup 的前台状态、原生 HWND 焦点，
    /// 并在当前鼠标消息处理完成后把 WPF 键盘焦点
    /// 交给用户实际点击的控件。
    ///
    /// 应在用户点击 Popup 内部时调用。
    /// </summary>
    public static void RestoreFocusForPointerInput(
        Popup popup,
        DependencyObject? originalSource)
    {
        ArgumentNullException.ThrowIfNull(
            popup);

        if (!popup.IsOpen)
        {
            return;
        }

        /*
         * Popup.Child 实际显示在一个独立的
         * Win32 HwndSource 中。
         */
        if (popup.Child
            is not Visual popupChild)
        {
            return;
        }

        PresentationSource?
            presentationSource =
                PresentationSource
                    .FromVisual(
                        popupChild);

        if (presentationSource
            is not HwndSource hwndSource)
        {
            return;
        }

        IntPtr popupHandle =
            hwndSource.Handle;

        if (popupHandle ==
            IntPtr.Zero)
        {
            return;
        }

        IntPtr ownerHandle =
            GetOwnerHandle(
                popup);

        UIElement? focusTarget =
            FindFocusableAncestor(
                originalSource);

        /*
         * 关键：
         *
         * SetFocus 只能解决当前前台线程内部的
         * 键盘焦点切换。
         *
         * 如果用户已经点击了其他程序，
         * LocalTodo 不再是前台进程，
         * 此时只调用 SetFocus 会静默失败。
         *
         * 当前方法只响应用户对 Popup 的真实点击，
         * 因而先请求把 Popup 恢复为前台窗口，
         * 再设置它的原生键盘焦点。
         *
         * 如果 Popup HWND 因窗口样式不能直接成为前台，
         * 则先激活它的宿主窗口后再次设置 Popup 焦点。
         */
        _ =
            RestoreNativeFocus(
                popupHandle,
                ownerHandle,
                SetForegroundWindow,
                SetFocus);

        if (focusTarget is null)
        {
            return;
        }

        /*
         * PreviewMouseDown 发生时，
         * 当前鼠标输入仍在 WPF 路由中。
         *
         * 等本次路由结束以后再恢复控件焦点，
         * 避免 TextBox 模板或其他控件的默认 MouseDown
         * 在后续阶段覆盖刚设置的焦点。
         */
        _ = popup.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(
                () =>
                {
                    if (!popup.IsOpen ||
                        !focusTarget.IsVisible ||
                        !focusTarget.IsEnabled ||
                        !focusTarget.Focusable)
                    {
                        return;
                    }

                    _ =
                        RestoreNativeFocus(
                            popupHandle,
                            ownerHandle,
                            SetForegroundWindow,
                            SetFocus);

                    _ =
                        focusTarget.Focus();

                    _ =
                        Keyboard.Focus(
                            focusTarget);
                }));
    }

    /// <summary>
    /// 恢复 Win32 前台窗口和键盘焦点。
    ///
    /// 将 Win32 调用作为委托传入，
    /// 使关键调用顺序可以在不创建真实窗口的情况下测试。
    /// </summary>
    internal static bool RestoreNativeFocus(
        IntPtr popupHandle,
        IntPtr ownerHandle,
        Func<IntPtr, bool> setForegroundWindow,
        Func<IntPtr, IntPtr> setFocus)
    {
        ArgumentNullException.ThrowIfNull(
            setForegroundWindow);

        ArgumentNullException.ThrowIfNull(
            setFocus);

        if (popupHandle ==
            IntPtr.Zero)
        {
            return false;
        }

        bool foregroundRestored =
            setForegroundWindow(
                popupHandle);

        if (!foregroundRestored &&
            ownerHandle !=
                IntPtr.Zero)
        {
            foregroundRestored =
                setForegroundWindow(
                    ownerHandle);
        }

        _ =
            setFocus(
                popupHandle);

        return foregroundRestored;
    }

    /// <summary>
    /// 从鼠标事件的原始命中元素向上查找
    /// 第一个可以获得键盘焦点的 WPF 控件。
    /// </summary>
    internal static UIElement? FindFocusableAncestor(
        DependencyObject? originalSource)
    {
        DependencyObject? current =
            originalSource;

        while (current is not null)
        {
            if (current
                is UIElement
                {
                    Focusable: true,
                    IsEnabled: true
                } focusableElement)
            {
                return focusableElement;
            }

            DependencyObject? parent =
                current switch
                {
                    Visual or Visual3D =>
                        VisualTreeHelper
                            .GetParent(
                                current),

                    _ =>
                        null
                };

            current =
                parent ??
                LogicalTreeHelper
                    .GetParent(
                        current);
        }

        return null;
    }

    private static IntPtr GetOwnerHandle(
        Popup popup)
    {
        if (popup.PlacementTarget
                is not DependencyObject
                    placementTarget)
        {
            return IntPtr.Zero;
        }

        Window? ownerWindow =
            Window.GetWindow(
                placementTarget);

        return ownerWindow is null
            ? IntPtr.Zero
            : new WindowInteropHelper(
                    ownerWindow)
                .Handle;
    }

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        SetForegroundWindow(
            IntPtr hWnd);

    [DllImport(
        "user32.dll")]
    private static extern IntPtr
        SetFocus(
            IntPtr hWnd);
}
