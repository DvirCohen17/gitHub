using System.Windows;
using System.Windows.Controls;

namespace GitHubDemo.Controls;

// Placeholder text for text and password boxes: Hint.Text="Search people".
// The styles in Design.xaml show it while the box is empty.
public static class Hint
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Hint), new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string value) => o.SetValue(TextProperty, value);

    // PasswordBox has no bindable Password, so emptiness is tracked here.
    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.RegisterAttached(
        "IsEmpty", typeof(bool), typeof(Hint), new PropertyMetadata(true));

    public static bool GetIsEmpty(DependencyObject o) => (bool)o.GetValue(IsEmptyProperty);
    public static void SetIsEmpty(DependencyObject o, bool value) => o.SetValue(IsEmptyProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box)
        {
            box.PasswordChanged -= OnPasswordChanged;
            box.PasswordChanged += OnPasswordChanged;
            SetIsEmpty(box, box.Password.Length == 0);
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetIsEmpty(box, box.Password.Length == 0);
    }
}

// Moves keyboard focus to an element when it becomes visible (inline editors, dialogs).
public static class FocusOnShow
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusOnShow), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
            {
                element.Dispatcher.BeginInvoke(() =>
                {
                    element.Focus();
                    if (element is TextBox box) box.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        };
        if (element.IsVisible) element.Loaded += (_, _) => element.Focus();
    }
}

// Marks the selected sidebar entry (see the NavButton style).
public static class Nav
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(Nav), new PropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject o) => (bool)o.GetValue(IsSelectedProperty);
    public static void SetIsSelected(DependencyObject o, bool value) => o.SetValue(IsSelectedProperty, value);
}
