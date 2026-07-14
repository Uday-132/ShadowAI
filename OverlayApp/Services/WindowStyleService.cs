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
    }
}
