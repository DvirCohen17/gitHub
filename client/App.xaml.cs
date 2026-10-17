using System;
using System.Windows;

namespace client_side
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            base.OnStartup(e);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
            {
                return;
            }
            if (!MainFrame.IsMainScreen(window))
            {
                FitToWorkArea(window); // main screens are placed by MainFrame before they're shown
            }
        }

        // Dialogs: never larger than the work area (at 150% scaling a large dialog
        // can be taller than a 1080p screen); centred again if it had to shrink.
        private static void FitToWorkArea(Window window)
        {
            if (window.WindowState != WindowState.Normal)
            {
                return;
            }
            Rect area = SystemParameters.WorkArea;
            bool resized = false;
            if (window.ActualWidth > area.Width * 0.95)
            {
                window.Width = area.Width * 0.95;
                resized = true;
            }
            if (window.ActualHeight > area.Height * 0.95)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Height = area.Height * 0.95;
                resized = true;
            }
            if (resized)
            {
                window.Left = area.Left + (area.Width - window.Width) / 2;
                window.Top = area.Top + (area.Height - window.Height) / 2;
            }
        }
    }

    // The app's main screens (sign-in, home, project, to-do...) are separate
    // windows, but they all share one size and position: each new screen opens
    // exactly where the previous one was (maximised too), so moving between them
    // looks like one window changing pages instead of windows closing and opening.
    public static class MainFrame
    {
        private static Rect? bounds;
        private static WindowState state = WindowState.Normal;

        public static bool IsMainScreen(Window window) =>
            window is LoginWindow or SignupWindow or ForgotPasswordWindow or ServerConnectWindow
                   or HomePage or ProjectDirectory or ToDoListWindow or MessagesWindow or settingsWindow;

        // Call from a main screen's constructor (before Show) so it appears in place.
        public static void Attach(Window window)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.ResizeMode = ResizeMode.CanResize;
            window.MinWidth = 820;
            window.MinHeight = 520;

            if (bounds == null)
            {
                Rect area = SystemParameters.WorkArea;
                double width = Math.Min(1240, area.Width * 0.92);
                double height = Math.Min(780, area.Height * 0.92);
                bounds = new Rect(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
            }
            Rect b = bounds.Value;
            window.Left = b.Left;
            window.Top = b.Top;
            window.Width = b.Width;
            window.Height = b.Height;
            window.WindowState = state;

            window.LocationChanged += (_, _) => Remember(window);
            window.SizeChanged += (_, _) => Remember(window);
            window.StateChanged += (_, _) =>
            {
                if (window.WindowState != WindowState.Minimized)
                {
                    state = window.WindowState;
                }
            };
        }

        private static void Remember(Window window)
        {
            if (window.WindowState == WindowState.Normal && window.IsLoaded && window.ActualWidth > 0)
            {
                bounds = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            }
        }
    }
}
