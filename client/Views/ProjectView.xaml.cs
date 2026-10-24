using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitHubDemo.ViewModels;

namespace GitHubDemo.Views;

public partial class ProjectView : UserControl
{
    private ProjectViewModel? _vm;

    public ProjectView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as ProjectViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as ProjectViewModel);
        tree.PreviewKeyDown += Tree_KeyDown;
    }

    private void Attach(ProjectViewModel? vm)
    {
        if (_vm == vm) return;
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
            _vm.OutputAppended -= console.Append;
            _vm.OutputCleared -= console.Clear;
            _vm.ChatScrollToEnd -= ScrollChatToEnd;
        }
        _vm = vm;
        if (vm == null) return;
        vm.PropertyChanged += OnViewModelChanged;
        vm.OutputAppended += console.Append;
        vm.OutputCleared += console.Clear;
        vm.ChatScrollToEnd += ScrollChatToEnd;
        UpdateSplit();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectViewModel.IsSplit)) UpdateSplit();
        if (e.PropertyName == nameof(ProjectViewModel.ActiveTab) && _vm?.ActiveTab != null)
            Dispatcher.BeginInvoke(() => mainEditor.FocusEditor(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void UpdateSplit()
    {
        bool split = _vm?.IsSplit == true;
        splitterColumn.Width = split ? new GridLength(1) : new GridLength(0);
        splitColumn.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void ScrollChatToEnd() => Dispatcher.BeginInvoke(() => chatScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

    // ---- header ----

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = moreButton.ContextMenu;
        menu.PlacementTarget = moreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
    }

    // ---- file tree ----

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNode { IsFolder: false } node && _vm != null && _vm.ActiveTab?.FileId != node.FileId)
            _vm.OpenFile(node.FileId);
    }

    // Right-click selects the item under the mouse, so the menu acts on it.
    private void Tree_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null)
        {
            item.Focus();
            e.Handled = false;
        }
    }

    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null || !_vm.CanEdit || tree.SelectedItem is not TreeNode node) return;
        if (e.Key == Key.F2)
        {
            _vm.RenameCommand.Execute(node);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _vm.DeleteCommand.Execute(node);
            e.Handled = true;
        }
    }

    private static TreeNode? NodeOf(object sender) => (sender as FrameworkElement)?.DataContext as TreeNode;

    private void OpenInSplit_Click(object sender, RoutedEventArgs e) => _vm?.OpenInSplitCommand.Execute(NodeOf(sender));
    private void NewFileHere_Click(object sender, RoutedEventArgs e) { if (_vm?.CanEdit == true) _vm.NewFileCommand.Execute(NodeOf(sender)); }
    private void NewFolderHere_Click(object sender, RoutedEventArgs e) { if (_vm?.CanEdit == true) _vm.NewFolderCommand.Execute(NodeOf(sender)); }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (_vm?.CanEdit == true) _vm.RenameCommand.Execute(NodeOf(sender)); }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (_vm?.CanEdit == true) _vm.DeleteCommand.Execute(NodeOf(sender)); }

    // ---- tabs ----

    private static EditorTab? TabOf(object sender) => (sender as FrameworkElement)?.DataContext as EditorTab;

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) != null) return; // the close button
        _vm?.ActivateTabCommand.Execute(TabOf(sender));
    }

    private void TabClose_Click(object sender, RoutedEventArgs e) => _vm?.CloseTabCommand.Execute(TabOf(sender));
    private void TabSplit_Click(object sender, RoutedEventArgs e) => _vm?.ShowInSplitCommand.Execute(TabOf(sender));

    // ---- chat ----

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if ((e.Key == Key.Tab || e.Key == Key.Return) && _vm.ShowMentions && _vm.MentionSuggestions.Count > 0)
        {
            _vm.InsertMentionCommand.Execute(_vm.MentionSuggestions[0]);
            chatInput.CaretIndex = chatInput.Text.Length;
            e.Handled = true;
            return;
        }
        // Enter sends; Shift+Enter is a new line (and so is Enter inside an open ``` block).
        if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            int fences = System.Text.RegularExpressions.Regex.Matches(chatInput.Text, "```").Count;
            if (fences % 2 == 1) return;
            _vm.SendChatCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static ChatMessageItem? MessageOf(object sender) => (sender as FrameworkElement)?.DataContext as ChatMessageItem;

    private void CopyMessage_Click(object sender, RoutedEventArgs e) => _vm?.CopyMessageCommand.Execute(MessageOf(sender));
    private void EditMessage_Click(object sender, RoutedEventArgs e) => _vm?.EditMessageCommand.Execute(MessageOf(sender));
    private void DeleteMessage_Click(object sender, RoutedEventArgs e) => _vm?.DeleteMessageCommand.Execute(MessageOf(sender));

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
