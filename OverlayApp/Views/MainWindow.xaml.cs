using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OverlayApp.Helpers;
using OverlayApp.Services;
using OverlayApp.ViewModels;

namespace OverlayApp.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel => (MainViewModel)DataContext;

        /// <summary>
        /// Timer that periodically re-asserts the window's topmost Z-order position.
        /// Lockdown browsers like Pearson OnVUE continuously force themselves to the top
        /// and push other windows behind. This timer fights back every second.
        /// </summary>
        private DispatcherTimer? _topmostTimer;
        private IntPtr _hwnd = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();
            
            // Wire up MVVM architecture by initializing and injecting the backend services
            DataContext = new MainViewModel(
                new SystemMonitorService(),
                new HotkeyService(),
                new WindowStyleService()
            );
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Fetch raw HWND handle to apply capture exclusion styles
            _hwnd = new WindowInteropHelper(this).Handle;
            if (_hwnd != IntPtr.Zero)
            {
                // Exclude the overlay from screenshots, recording tools, and screen sharing
                Win32.SetWindowDisplayAffinity(_hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
                
                // Hide from the Windows Alt-Tab switcher menu
                WindowHelper.HideFromAltTab(this);

                // Prevent the overlay from stealing focus so exam browsers don't detect a tab switch
                WindowHelper.SetNoActivate(this);

                // Hook into the window message pump to intercept WM_MOUSEACTIVATE
                HwndSource source = HwndSource.FromHwnd(_hwnd);
                source?.AddHook(WndProc);
            }
            
            // Pass this Window instance to the view-model services to complete initialization
            ViewModel.InitializeServices(this);

            // Sync initial mouse hook state
            UpdateMouseHook();

            // Auto-scroll scan results to the top when new response text arrives
            ViewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.ScanResponseText))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var text = ViewModel.ScanResponseText;
                        if (text != null && !text.Contains("👉 Follow-up Question"))
                        {
                            TxtScanScrollViewer?.ScrollToTop();
                        }
                    }));
                }
                else if (args.PropertyName == nameof(MainViewModel.VoiceScanResponseText))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var text = ViewModel.VoiceScanResponseText;
                        if (text != null && !text.Contains("👉 Follow-up Question"))
                        {
                            // Only scroll to the top for the initial voice scan query, keep current scroll position for follow-ups
                            VoiceScanScrollViewer?.ScrollToTop();
                        }
                    }));
                }
                else if (args.PropertyName == nameof(MainViewModel.IsClickThrough))
                {
                    Dispatcher.BeginInvoke(new Action(() => UpdateMouseHook()));
                }
            };

            // Start the topmost re-assertion timer (fights lockdown browsers that push us behind)
            StartTopmostTimer();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Stop the topmost timer
            _topmostTimer?.Stop();

            // Safely uninstall mouse hook if active
            if (_mouseHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            // Call VM clean up to stop performance timers and release hotkeys
            ViewModel.Cleanup();
            base.OnClosing(e);
        }

        /// <summary>
        /// Gets or sets whether the window is in stealth mode (anti-focus-detection).
        /// When true, all focus-stealing messages from the OS are blocked.
        /// </summary>
        public bool IsStealthMode { get; set; } = true;

        /// <summary>
        /// Comprehensive anti-detection WndProc handler.
        /// Intercepts ALL Windows messages that could cause this overlay to steal focus/activation
        /// from the exam browser. Prevents these JavaScript detection events from firing:
        /// - visibilitychange / webkitvisibilitychange / mozvisibilitychange (page stays visible)
        /// - blur / focus / focusin / focusout (browser never loses focus)
        /// - pagehide (page never hides)
        /// - mouseenter / mouseleave (browser stays as foreground target)
        /// - requestAnimationFrame throttling (page stays in foreground, RAF runs at full speed)
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Always block Z-order manipulation from lockdown browsers (even outside stealth mode)
            if (msg == Win32.WM_WINDOWPOSCHANGING && Topmost)
            {
                // Prevent any application from changing our Z-order position
                // by adding SWP_NOZORDER flag to the incoming WINDOWPOS struct
                try
                {
                    var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
                    pos.flags |= Win32.SWP_NOZORDER;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                catch { /* Safety: never crash on marshaling errors */ }
            }

            if (!IsStealthMode)
            {
                // When not in stealth mode, allow normal activation and focus so the user can type!
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case Win32.WM_MOUSEACTIVATE:
                    // Mouse clicked on our window — tell Windows to process the click but do NOT activate us
                    handled = true;
                    return new IntPtr(Win32.MA_NOACTIVATE);

                case Win32.WM_ACTIVATE:
                    // Block any activation attempt. If wParam != WA_INACTIVE, the OS is trying to activate us — deny it.
                    if ((wParam.ToInt32() & 0xFFFF) != Win32.WA_INACTIVE)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;

                case Win32.WM_NCACTIVATE:
                    // Non-client area activation (title bar glow, border highlight).
                    // Return TRUE to allow the visual update but prevent actual activation.
                    // Since WindowStyle=None, there's no visible non-client area anyway.
                    handled = true;
                    return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Starts a DispatcherTimer that re-asserts topmost Z-order every 1.5 seconds.
        /// Lockdown browsers continuously call SetWindowPos on themselves to stay on top,
        /// which can push our overlay behind. This timer counteracts that by calling
        /// SetWindowPos(HWND_TOPMOST) with SWP_NOACTIVATE (so we don't steal focus).
        /// </summary>
        private void StartTopmostTimer()
        {
            _topmostTimer = new DispatcherTimer();
            _topmostTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _topmostTimer.Tick += (s, e) =>
            {
                if (_hwnd != IntPtr.Zero && Topmost)
                {
                    // Re-assert topmost position without activating the window
                    Win32.SetWindowPos(
                        _hwnd,
                        Win32.HWND_TOPMOST,
                        0, 0, 0, 0,
                        Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW
                    );
                }
            };
            _topmostTimer.Start();
        }

        /// <summary>
        /// Allows moving the window when left-clicking and dragging the header bar (if not locked).
        /// Uses Win32 ReleaseCapture + WM_NCLBUTTONDOWN instead of WPF DragMove() to avoid
        /// window activation (DragMove internally calls Activate(), which would trigger blur events).
        /// </summary>
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !ViewModel.IsLocked)
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    Win32.ReleaseCapture();
                    Win32.SendMessage(hwnd, (uint)Win32.WM_NCLBUTTONDOWN, (IntPtr)Win32.HTCAPTION, IntPtr.Zero);
                }
            }
        }

        /// <summary>
        /// Initiates a smooth native Win32 window resizing session using SendMessage.
        /// Avoids laggy custom WPF layout-updating loops.
        /// </summary>
        private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !ViewModel.IsLocked)
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    // Call user32 SendMessage with WM_SYSCOMMAND (0x0112) and SC_SIZE + 8 (0xF008 - bottom-right resize grip)
                    // This gives control back to the OS Window Manager to resize the frame naturally
                    Win32.SendMessage(hwnd, Win32.WM_SYSCOMMAND, (IntPtr)(Win32.SC_SIZE + 8), IntPtr.Zero);
                }
            }
        }

        #region Low-Level Mouse Hook for Click-Through Unlock

        private IntPtr _mouseHook = IntPtr.Zero;
        private Win32.LowLevelMouseProc? _mouseProc;

        private void UpdateMouseHook()
        {
            if (ViewModel == null) return;

            if (ViewModel.IsClickThrough)
            {
                if (_mouseHook == IntPtr.Zero)
                {
                    _mouseProc = LowLevelMouseCallback;
                    using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule!;
                    _mouseHook = Win32.SetWindowsHookEx(
                        Win32.WH_MOUSE_LL,
                        _mouseProc,
                        Win32.GetModuleHandle(curModule.ModuleName),
                        0
                    );
                }
            }
            else
            {
                if (_mouseHook != IntPtr.Zero)
                {
                    Win32.UnhookWindowsHookEx(_mouseHook);
                    _mouseHook = IntPtr.Zero;
                    _mouseProc = null;
                }
            }
        }

        private IntPtr LowLevelMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == Win32.WM_LBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                var mousePos = new Point(hookStruct.pt.x, hookStruct.pt.y);

                bool isOverPin = false;
                Dispatcher.Invoke(() =>
                {
                    isOverPin = IsMouseOverPinButton(mousePos);
                });

                if (isOverPin)
                {
                    // User clicked directly on the Pin button! Intercept click, turn off click-through
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ViewModel.IsClickThrough = false;
                    }));

                    return new IntPtr(1); // Consume the click event to prevent pass-through
                }
            }
            return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool IsMouseOverPinButton(Point mouseScreenPos)
        {
            if (PinButton == null || !PinButton.IsVisible) return false;
            try
            {
                Point relativePos = PinButton.PointFromScreen(mouseScreenPos);
                return relativePos.X >= 0 && relativePos.X <= PinButton.ActualWidth &&
                       relativePos.Y >= 0 && relativePos.Y <= PinButton.ActualHeight;
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }
}
