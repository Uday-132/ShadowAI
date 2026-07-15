using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Exclude the overlay from screenshots, recording tools, and screen sharing
                Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
                
                // Hide from the Windows Alt-Tab switcher menu
                WindowHelper.HideFromAltTab(this);
            }
            
            // Pass this Window instance to the view-model services to complete initialization
            ViewModel.InitializeServices(this);

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
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Call VM clean up to stop performance timers and release hotkeys
            ViewModel.Cleanup();
            base.OnClosing(e);
        }

        /// <summary>
        /// Allows moving the window when left-clicking and dragging the header bar (if not locked).
        /// </summary>
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !ViewModel.IsLocked)
            {
                this.DragMove();
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
    }
}
