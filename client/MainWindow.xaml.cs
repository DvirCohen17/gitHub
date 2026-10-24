using System.Windows;
using System.Windows.Controls;
using GitHubDemo.Core;

namespace GitHubDemo;

// The one window of the app (pages change inside it). Remembers its size and position.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplySavedBounds();
        Closing += (_, _) => SaveBounds();
    }

    private void ApplySavedBounds()
    {
        var s = Services.Settings;
        Rect area = SystemParameters.WorkArea;
        if (!double.IsNaN(s.WindowWidth) && s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight
            && s.WindowLeft > SystemParameters.VirtualScreenLeft - 50 && s.WindowTop > SystemParameters.VirtualScreenTop - 50
            && s.WindowLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && s.WindowTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }
        else
        {
            // first start: a comfortable size that fits the screen (also at 150% scaling)
            Width = Math.Min(1320, area.Width * 0.9);
            Height = Math.Min(840, area.Height * 0.9);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveBounds()
    {
        var s = Services.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        s.Save();
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }
}
