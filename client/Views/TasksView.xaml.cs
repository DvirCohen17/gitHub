using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

public partial class TasksView : UserControl
{
    public TasksView()
    {
        InitializeComponent();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is TasksViewModel vm && (sender as FrameworkElement)?.DataContext is TasksViewModel.FilterOption option)
            vm.SetFilterCommand.Execute(option.Key);
    }

    // Clicking a task card (not its checkbox) opens it in the editor panel.
    private void Task_Click(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d != null && d != sender; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ToggleButton) return;
        }
        if (DataContext is TasksViewModel vm && (sender as FrameworkElement)?.DataContext is TaskItem task)
            vm.EditTaskCommand.Execute(task);
    }
}
