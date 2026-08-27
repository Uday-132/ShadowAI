using System;
using System.Windows;
using System.Windows.Interop;

namespace OverlayApp.Helpers
{
    /// <summary>
    /// Utility helper to change and manage WPF window properties using Win32 styles.
    /// </summary>
    public static class WindowHelper
    {
        /// <summary>
        /// Toggles mouse click-through behavior on a WPF window.
        /// When clickThrough is true, the WS_EX_TRANSPARENT style is added, making the window mouse-interactive-transparent.
        /// </summary>
        public static void SetClickThrough(Window window, bool clickThrough)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            
            if (clickThrough)
            {
                // Apply WS_EX_TRANSPARENT so the OS ignores mouse events for this window
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, extendedStyle | Win32.WS_EX_TRANSPARENT);
            }
            else
            {
                // Remove WS_EX_TRANSPARENT so the window captures mouse events normally
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, extendedStyle & ~Win32.WS_EX_TRANSPARENT);
            }
        }

        /// <summary>
        /// Applies the WS_EX_TOOLWINDOW style to the window handle to hide it from the Windows Alt-Tab switcher.
        /// </summary>
        public static void HideFromAltTab(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, extendedStyle | Win32.WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// Toggles WS_EX_NOACTIVATE on the window.
        /// When enabled, the window never steals focus (stealth mode for exams).
        /// When disabled, the window can receive keyboard focus (for typing follow-up questions).
        /// </summary>
        public static void SetNoActivate(Window window, bool enable = true)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            if (enable)
            {
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, extendedStyle | Win32.WS_EX_NOACTIVATE);
            }
            else
            {
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, extendedStyle & ~Win32.WS_EX_NOACTIVATE);
            }
        }

        /// <summary>
        /// Disables Windows 11 Snap Layouts completely for the specified WPF window.
        /// Strips WS_MAXIMIZEBOX and WS_MINIMIZEBOX from the window's GWL_STYLE so Windows DWM
        /// never displays Snap Layout flyouts, snap grid menus, or edge snap gesture previews.
        /// </summary>
        public static void DisableSnapLayouts(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int style = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_STYLE, style & ~Win32.WS_MAXIMIZEBOX & ~Win32.WS_MINIMIZEBOX);
        }
    }
}
