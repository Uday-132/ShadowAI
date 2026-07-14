using System;
using System.Windows;
using System.Windows.Interop;
using OverlayApp.Helpers;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service that registers a system-wide global hotkey (Ctrl + Shift + C) to toggle
    /// overlay interactivity. Hooks into the native Windows message queue.
    /// </summary>
    public class HotkeyService
    {
        private const int HOTKEY_ID = 4220; // Unique identifier for the registered hotkey
        private IntPtr _windowHandle = IntPtr.Zero;
        private HwndSource? _hwndSource;

        /// <summary>
        /// Fires when the registered hotkey combination is pressed.
        /// </summary>
        public event Action? HotkeyPressed;

        /// <summary>
        /// Registers the hotkey using the provided Window handle.
        /// </summary>
        public void Register(Window window)
        {
            _windowHandle = new WindowInteropHelper(window).Handle;
            if (_windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window must have a valid HWND handle to register hotkeys.");
            }

            // Register Ctrl + Shift + C
            // fsModifiers: MOD_CONTROL (0x0002) | MOD_SHIFT (0x0004)
            // vk: VK_C (0x43)
            bool registered = Win32.RegisterHotKey(
                _windowHandle, 
                HOTKEY_ID, 
                Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, 
                Win32.VK_C
            );

            if (!registered)
            {
                // In a production app, we would log this, try alternative binds, or alert the user
                System.Diagnostics.Debug.WriteLine("Failed to register global hotkey Ctrl+Shift+C.");
            }

            // Attach message hook to listen for WM_HOTKEY events
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);
        }

        /// <summary>
        /// Unregisters the hotkey and detaches message hooks to clean up resources.
        /// </summary>
        public void Unregister()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                Win32.UnregisterHotKey(_windowHandle, HOTKEY_ID);
                _hwndSource?.RemoveHook(HwndHook);
                _hwndSource = null;
                _windowHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Listens to Win32 messages dispatched to the window.
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke();
                handled = true; // Mark message as handled
            }
            return IntPtr.Zero;
        }
    }
}
