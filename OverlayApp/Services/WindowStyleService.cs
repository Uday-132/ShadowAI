using System;
using System.Windows;
using OverlayApp.Helpers;

namespace OverlayApp.Services
{
    /// <summary>
    /// Service that coordinates native window modification operations for the ViewModel,
    /// maintaining pure MVVM separation by abstracting the physical Window reference.
    /// </summary>
    public class WindowStyleService
    {
        private Window? _targetWindow;

        /// <summary>
        /// Registers the active Window instance to be managed by this service.
        /// </summary>
        public void Initialize(Window window)
        {
            _targetWindow = window;
        }

        /// <summary>
        /// Updates the click-through styling of the managed window.
        /// </summary>
        public void SetClickThrough(bool clickThrough)
        {
            if (_targetWindow == null) return;
            WindowHelper.SetClickThrough(_targetWindow, clickThrough);
        }

        /// <summary>
        /// Toggles the Always-on-Top setting of the window.
        /// </summary>
        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
            if (_targetWindow == null) return;
            _targetWindow.Topmost = alwaysOnTop;
        }

        /// <summary>
        /// Changes the transparency/opacity of the window.
        /// </summary>
        public void SetOpacity(double opacity)
        {
            if (_targetWindow == null) return;
            _targetWindow.Opacity = Math.Clamp(opacity, 0.1, 1.0);
        }

        /// <summary>
        /// Toggles stealth mode (WS_EX_NOACTIVATE + WndProc activation blocking).
        /// When stealth=true: overlay is invisible to browser focus detection.
        /// When stealth=false: overlay can receive keyboard input for typing follow-ups.
        /// </summary>
        public void SetStealthMode(bool stealth)
        {
            if (_targetWindow == null) return;
            
            // Toggle WS_EX_NOACTIVATE flag
            WindowHelper.SetNoActivate(_targetWindow, stealth);

            // Tell the WndProc whether to block activation messages
            if (_targetWindow is Views.MainWindow mainWindow)
            {
                mainWindow.IsStealthMode = stealth;
            }
        }

        /// <summary>
        /// Explicitly activates the window so it can receive keyboard input.
        /// Only called for modal overlays (Login, Groq Key, Dashboard) — NEVER during exams.
        /// </summary>
        public void ActivateWindow()
        {
            _targetWindow?.Activate();
        }
    }
}
