using System.Windows;
using System.Windows.Controls;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // start where there's something to type
            if (string.IsNullOrEmpty(userBox.Text)) userBox.Focus();
            else passwordBox.Focus();
        };
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = passwordBox.Password;
    }
}
