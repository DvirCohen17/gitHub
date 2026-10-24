using System.Windows;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

// "New project" / "Edit project": the one screen that is a separate dialog window.
public partial class ProjectDialog : Window
{
    private ProjectDialog(ProjectDialogViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Owner = this;
        vm.Close = result =>
        {
            DialogResult = result;
            Close();
        };
        Owner = Application.Current.MainWindow;
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    // Returns the new project's id, or 0 if cancelled.
    public static Task<long> CreateAsync()
    {
        var vm = new ProjectDialogViewModel();
        bool ok = new ProjectDialog(vm).ShowDialog() == true;
        return Task.FromResult(ok ? vm.CreatedProjectId : 0);
    }

    public static Task<bool> EditAsync(long projectId, bool readOnly, bool startOnSharing = false)
    {
        var vm = new ProjectDialogViewModel(projectId, readOnly);
        if (startOnSharing) vm.Section = "sharing";
        return Task.FromResult(new ProjectDialog(vm).ShowDialog() == true);
    }
}
