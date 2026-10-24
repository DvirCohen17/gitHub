using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GitHubDemo.Core;

namespace GitHubDemo.Controls;

// A round profile picture: the user's picture, or their initial on a colour,
// optionally with an online/away dot.
public partial class Avatar : UserControl
{
    public static readonly DependencyProperty UserProperty = DependencyProperty.Register(
        nameof(User), typeof(UserRef), typeof(Avatar), new PropertyMetadata(null, (d, e) => ((Avatar)d).OnUserChanged(e)));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Avatar), new PropertyMetadata(32.0, (d, _) => ((Avatar)d).Refresh()));

    public static readonly DependencyProperty ShowPresenceProperty = DependencyProperty.Register(
        nameof(ShowPresence), typeof(bool), typeof(Avatar), new PropertyMetadata(false, (d, _) => ((Avatar)d).Refresh()));

    public Avatar()
    {
        InitializeComponent();
        // Only listen to the (long-lived) user object while on screen.
        Loaded += (_, _) =>
        {
            if (User != null)
            {
                User.PropertyChanged -= OnUserPropertyChanged;
                User.PropertyChanged += OnUserPropertyChanged;
            }
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (User != null) User.PropertyChanged -= OnUserPropertyChanged;
        };
        Refresh();
    }

    public UserRef? User
    {
        get => (UserRef?)GetValue(UserProperty);
        set => SetValue(UserProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool ShowPresence
    {
        get => (bool)GetValue(ShowPresenceProperty);
        set => SetValue(ShowPresenceProperty, value);
    }

    private void OnUserChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is UserRef old) old.PropertyChanged -= OnUserPropertyChanged;
        if (e.NewValue is UserRef user && IsLoaded) user.PropertyChanged += OnUserPropertyChanged;
        Refresh();
    }

    private void OnUserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UserRef.Picture) or nameof(UserRef.Status)) Refresh();
    }

    private void Refresh()
    {
        initials.FontSize = Math.Max(8, Size * 0.42);
        dot.Width = dot.Height = Math.Max(8, Size * 0.3);
        picture.Visibility = User?.Picture != null ? Visibility.Visible : Visibility.Collapsed;
        dot.Visibility = ShowPresence && User != null ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = User == null ? null : ShowPresence ? $"{User.Name} · {User.PresenceText}" : User.Name;
    }
}
