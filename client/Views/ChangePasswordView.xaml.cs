using System.Windows;
using System.Windows.Controls;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

public partial class ChangePasswordView : UserControl
{
    public ChangePasswordView()
    {
        InitializeComponent();
    }

    private void Passwords_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChangePasswordViewModel vm) return;
        vm.OldPassword = oldBox.Password;
        vm.NewPassword = newBox.Password;
        vm.Confirm = confirmBox.Password;
    }
}
