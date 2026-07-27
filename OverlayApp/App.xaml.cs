using System;
using System.Threading;
using System.Windows;

namespace OverlayApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "ShadowAI_OverlayApp_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                MessageBox.Show("Shadow AI Overlay is already running in the background! Check your desktop screen or Task Manager.", 
                                "Shadow AI", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Register global unhandled exception handlers so startup errors pop up explicitly instead of silent crash
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Unhandled Exception:\n{args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}",
                                "Shadow AI Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    MessageBox.Show($"Fatal Error:\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                                    "Shadow AI Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch { }
            base.OnExit(e);
        }
    }
}
