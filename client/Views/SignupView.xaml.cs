using System.Windows;
using System.Windows.Controls;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

public partial class SignupView : UserControl
{
    public SignupView()
    {
        InitializeComponent();
    }

    private void Passwords_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SignupViewModel vm) return;
        vm.Password = passwordBox.Password;
        vm.Confirm = confirmBox.Password;
    }
}
