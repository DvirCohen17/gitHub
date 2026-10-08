using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace client_side
{
    // Small "enter a value" dialog (rename file, etc.). Returns null when cancelled.
    public static class TextPrompt
    {
        public static string Show(Window owner, string title, string label, string initialValue)
        {
            var input = new TextBox { Text = initialValue, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
            var ok = new Button { Content = "OK", Width = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 28, IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = label });
            panel.Children.Add(input);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title,
                Content = panel,
                Owner = owner,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            dialog.Style = (Style)Application.Current.FindResource(typeof(Window));
            ok.Click += (_, _) => dialog.DialogResult = true;
            dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
            if (owner?.Background != null)
            {
                dialog.Background = owner.Background;
                dialog.Foreground = owner.Foreground;
            }
            return dialog.ShowDialog() == true ? input.Text : null;
        }
    }
}
