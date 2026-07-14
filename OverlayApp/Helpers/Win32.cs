using System;
using System.Runtime.InteropServices;

namespace OverlayApp.Helpers
{
    /// <summary>
    /// Provides P/Invoke declarations and constants for Win32 API interactions.
    /// Includes comments explaining how Windows Desktop Window Manager (DWM) and window styles operate.
    /// </summary>
    public static class Win32
    {
        // Window styles index
        public const int GWL_EXSTYLE = -20;

        // Extended Window Styles:
        // WS_EX_TRANSPARENT makes the window click-through. Hit-testing will ignore the window and pass clicks below.
        // WS_EX_LAYERED enables the OS to support alpha blending and composite transparent windows smoothly.
        // WS_EX_TOPMOST makes the window stay above all standard windows.
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        // Window Display Affinity Flags:
        // WDA_EXCLUDEFROMCAPTURE hides the window completely from screen sharing, screen recording, and screenshots.
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        // Window Messages
        public const int WM_HOTKEY = 0x0312;
        public const int WM_SYSCOMMAND = 0x0112;
        
        // System command size action
        public const int SC_SIZE = 0xF000;

        // Hotkey Modifiers
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;

        // Virtual Key Codes
        public const uint VK_C = 0x43; // C key
        
        // DLL Imports with 32-bit and 64-bit compatibility wrappers
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>
        /// Retrieves information about the specified window. Safe for both 32-bit and 64-bit processes.
        /// </summary>
        public static int GetWindowLong(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
            {
                return (int)GetWindowLongPtr64(hWnd, nIndex).ToInt64();
            }
            return GetWindowLong32(hWnd, nIndex);
        }

        /// <summary>
        /// Changes an attribute of the specified window. Safe for both 32-bit and 64-bit processes.
        /// </summary>
        public static int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
        {
            if (IntPtr.Size == 8)
            {
                return (int)SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong)).ToInt64();
            }
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        // GDI screen capture interops
        public const int SRCCOPY = 0x00CC0020;

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }
}
