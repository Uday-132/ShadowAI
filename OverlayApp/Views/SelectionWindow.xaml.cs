using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using OverlayApp.Helpers;

namespace OverlayApp.Views
{
    /// <summary>
    /// Interaction logic for SelectionWindow.xaml.
    /// Spans the entire desktop to capture screen crop dimensions.
    /// Excludes itself from screen sharing to hide selection borders.
    /// </summary>
    public partial class SelectionWindow : Window
    {
        private Point _startPoint;
        private bool _isSelecting;

        /// <summary>
        /// Fires when the user finishes dragging. Returns crop bounds in physical screen pixels.
        /// </summary>
        public Action<Int32Rect>? AreaSelected { get; set; }

        public SelectionWindow()
        {
            InitializeComponent();

            // Set coordinates to span the entire virtual monitor canvas
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Fetch HWND handle to activate capture exclusion
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Ensures screen share recipients cannot see the selection mask or selection borders
                Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
            }
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(SelectionCanvas);
            _isSelecting = true;

            // Place selection visual box
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            SelectionRectangle.Visibility = Visibility.Visible;

            SelectionCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            Point currentPoint = e.GetPosition(SelectionCanvas);

            // Bounding box calculations
            double x = Math.Min(_startPoint.X, currentPoint.X);
            double y = Math.Min(_startPoint.Y, currentPoint.Y);
            double w = Math.Abs(_startPoint.X - currentPoint.X);
            double h = Math.Abs(_startPoint.Y - currentPoint.Y);

            // Resize selection visual rectangle
            Canvas.SetLeft(SelectionRectangle, x);
            Canvas.SetTop(SelectionRectangle, y);
            SelectionRectangle.Width = w;
            SelectionRectangle.Height = h;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;

            _isSelecting = false;
            SelectionCanvas.ReleaseMouseCapture();
            SelectionRectangle.Visibility = Visibility.Collapsed;

            Point endPoint = e.GetPosition(SelectionCanvas);

            // Convert logical units (96 DPI) to physical screen pixels for screen grab
            double scaleX = 1.0;
            double scaleY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            // Offset the logical coordinates by the virtual screen boundaries
            double logicalX = Math.Min(_startPoint.X, endPoint.X) + Left;
            double logicalY = Math.Min(_startPoint.Y, endPoint.Y) + Top;
            double logicalW = Math.Abs(_startPoint.X - endPoint.X);
            double logicalH = Math.Abs(_startPoint.Y - endPoint.Y);

            int physicalX = (int)Math.Round(logicalX * scaleX);
            int physicalY = (int)Math.Round(logicalY * scaleY);
            int physicalW = (int)Math.Round(logicalW * scaleX);
            int physicalH = (int)Math.Round(logicalH * scaleY);

            // Hide overlay instantly to prevent visual artifacts
            this.Hide();

            if (physicalW > 3 && physicalH > 3)
            {
                AreaSelected?.Invoke(new Int32Rect(physicalX, physicalY, physicalW, physicalH));
            }

            this.Close();
        }
    }
}
