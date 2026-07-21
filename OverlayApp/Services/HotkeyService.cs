using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OverlayApp.Helpers;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service that registers hotkeys using three redundant methods for absolute resilience
    /// against lockdown browsers like Pearson OnVUE:
    /// 1. RegisterHotKey (Win32 API) - Primary standard hotkey registration.
    /// 2. WH_KEYBOARD_LL Hook - Low-level hook to catch input before standard focus filtering.
    /// 3. Direct Key State Polling (GetAsyncKeyState) - Continuously polls keyboard hardware 
    ///    state in a background thread. Bypasses User Interface Privilege Isolation (UIPI) 
    ///    and hooks blocked by high-privilege lockdown windows.
    /// </summary>
    public class HotkeyService
    {
        public const int HOTKEY_SCAN_ID = 4221;
        public const int HOTKEY_COPY_ID = 4222;
        public const int HOTKEY_CLEAR_ID = 4223;

        private IntPtr _windowHandle = IntPtr.Zero;
        private HwndSource? _hwndSource;

        // Low-level keyboard hook handle and delegate reference
        private IntPtr _llKeyboardHook = IntPtr.Zero;
        private Win32.LowLevelKeyboardProc? _llKeyboardProc;
        private Dispatcher? _dispatcher;

        // Background thread for hardware key polling (direct hardware access fallback)
        private System.Threading.Thread? _pollingThread;
        private bool _isPolling;

        /// <summary>
        /// Fires when any registered hotkey combination is pressed, passing the hotkey ID.
        /// </summary>
        public event Action<int>? HotkeyPressed;

        /// <summary>
        /// Registers the hotkeys using the provided Window handle.
        /// </summary>
        public void Register(Window window)
        {
            _windowHandle = new WindowInteropHelper(window).Handle;
            if (_windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window must have a valid HWND handle to register hotkeys.");
            }

            _dispatcher = window.Dispatcher;

            // Method 1: Register standard Win32 Hotkeys
            try
            {
                Win32.RegisterHotKey(_windowHandle, HOTKEY_SCAN_ID, Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_S);
                Win32.RegisterHotKey(_windowHandle, HOTKEY_COPY_ID, Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_X);
                Win32.RegisterHotKey(_windowHandle, HOTKEY_CLEAR_ID, Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_Z);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RegisterHotKey failed: {ex.Message}");
            }

            // Attach message hook to listen for WM_HOTKEY events
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);

            // Method 2: Install low-level keyboard hook
            try
            {
                InstallLowLevelHook();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InstallLowLevelHook failed: {ex.Message}");
            }

            // Method 3: Start background key polling (resilient to UIPI / high integrity level windows)
            StartPollingThread();
        }

        /// <summary>
        /// Unregisters the hotkeys and detaches message hooks to clean up resources.
        /// </summary>
        public void Unregister()
        {
            // Stop polling thread
            StopPollingThread();

            // Unregister Win32 Hotkeys
            if (_windowHandle != IntPtr.Zero)
            {
                Win32.UnregisterHotKey(_windowHandle, HOTKEY_SCAN_ID);
                Win32.UnregisterHotKey(_windowHandle, HOTKEY_COPY_ID);
                Win32.UnregisterHotKey(_windowHandle, HOTKEY_CLEAR_ID);
                _hwndSource?.RemoveHook(HwndHook);
                _hwndSource = null;
                _windowHandle = IntPtr.Zero;
            }

            // Uninstall low-level hook
            UninstallLowLevelHook();
        }

        /// <summary>
        /// Listens to Win32 messages dispatched to the window.
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                int pressedId = wParam.ToInt32();
                if (pressedId >= HOTKEY_SCAN_ID && pressedId <= HOTKEY_CLEAR_ID)
                {
                    HotkeyPressed?.Invoke(pressedId);
                    handled = true; // Mark message as handled
                }
            }
            return IntPtr.Zero;
        }

        #region Low-Level Keyboard Hook

        private void InstallLowLevelHook()
        {
            _llKeyboardProc = LowLevelKeyboardCallback;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _llKeyboardHook = Win32.SetWindowsHookEx(
                Win32.WH_KEYBOARD_LL,
                _llKeyboardProc,
                Win32.GetModuleHandle(curModule.ModuleName),
                0
            );
        }

        private void UninstallLowLevelHook()
        {
            if (_llKeyboardHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_llKeyboardHook);
                _llKeyboardHook = IntPtr.Zero;
            }
            _llKeyboardProc = null;
        }

        private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam.ToInt32() == Win32.WM_KEYDOWN || wParam.ToInt32() == Win32.WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);

                bool ctrlHeld = (Win32.GetAsyncKeyState(0x11) & 0x8000) != 0;  // VK_CONTROL
                bool shiftHeld = (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT

                if (ctrlHeld && shiftHeld)
                {
                    int hotkeyId = -1;
                    switch (vkCode)
                    {
                        case (int)Win32.VK_S: hotkeyId = HOTKEY_SCAN_ID; break;
                        case (int)Win32.VK_X: hotkeyId = HOTKEY_COPY_ID; break;
                        case (int)Win32.VK_Z: hotkeyId = HOTKEY_CLEAR_ID; break;
                    }

                    if (hotkeyId != -1)
                    {
                        _dispatcher?.BeginInvoke(new Action(() =>
                        {
                            HotkeyPressed?.Invoke(hotkeyId);
                        }));
                    }
                }
            }

            return Win32.CallNextHookEx(_llKeyboardHook, nCode, wParam, lParam);
        }

        #endregion

        #region Hardware Key Polling Loop (Direct Hardware Access Fallback)

        private void StartPollingThread()
        {
            _isPolling = true;
            _pollingThread = new System.Threading.Thread(PollKeysLoop)
            {
                IsBackground = true,
                Name = "HotkeyPollingThread",
                Priority = System.Threading.ThreadPriority.AboveNormal
            };
            _pollingThread.Start();
        }

        private void StopPollingThread()
        {
            _isPolling = false;
            if (_pollingThread != null)
            {
                if (!_pollingThread.Join(500))
                {
                    try { _pollingThread.Interrupt(); } catch { }
                }
                _pollingThread = null;
            }
        }

        private void PollKeysLoop()
        {
            // Track the previous state of keys to detect state transitions (prevent key spam)
            bool prevS = false;
            bool prevX = false;
            bool prevZ = false;

            while (_isPolling)
            {
                try
                {
                    // Check physical status of Ctrl + Shift keys globally
                    bool ctrlPressed = (Win32.GetAsyncKeyState(0x11) & 0x8000) != 0;  // VK_CONTROL = 0x11
                    bool shiftPressed = (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT = 0x10

                    if (ctrlPressed && shiftPressed)
                    {
                        // Check hotkey trigger keys
                        bool currS = (Win32.GetAsyncKeyState(0x53) & 0x8000) != 0; // VK_S = 0x53
                        bool currX = (Win32.GetAsyncKeyState(0x58) & 0x8000) != 0; // VK_X = 0x58
                        bool currZ = (Win32.GetAsyncKeyState(0x5A) & 0x8000) != 0; // VK_Z = 0x5A

                        int triggeredId = -1;

                        // Identify transition to pressed
                        if (currS && !prevS) triggeredId = HOTKEY_SCAN_ID;
                        else if (currX && !prevX) triggeredId = HOTKEY_COPY_ID;
                        else if (currZ && !prevZ) triggeredId = HOTKEY_CLEAR_ID;

                        if (triggeredId != -1)
                        {
                            _dispatcher?.BeginInvoke(new Action(() =>
                            {
                                HotkeyPressed?.Invoke(triggeredId);
                            }));
                        }

                        prevS = currS;
                        prevX = currX;
                        prevZ = currZ;
                    }
                    else
                    {
                        // Reset key states when modifier keys are released
                        prevS = false;
                        prevX = false;
                        prevZ = false;
                    }
                }
                catch
                {
                    // Silence errors to keep loop running
                }

                // Poll every 40ms to maintain precise response without consuming CPU
                System.Threading.Thread.Sleep(40);
            }
        }

        #endregion
    }
}
