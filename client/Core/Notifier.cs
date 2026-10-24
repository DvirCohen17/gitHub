using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp.Notifications;

namespace GitHubDemo.Core;

public sealed partial class ToastItem : ObservableObject
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Kind { get; init; } = "info"; // info, success, error, message
    public UserRef? User { get; init; }
    public Action? Open { get; init; }
    public bool HasAction => Open != null;

    [RelayCommand]
    private void Activate()
    {
        Services.Notifier.Dismiss(this);
        Open?.Invoke();
    }

    [RelayCommand]
    private void Close() => Services.Notifier.Dismiss(this);
}

// Notifications: small cards in the corner of the window, a Windows notification
// when the app isn't in front, and a count badge on the taskbar button.
public sealed class Notifier
{
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    private readonly bool _systemToasts = Environment.GetEnvironmentVariable("GITHUBDEMO_NO_SYSTEM_TOASTS") != "1";

    public Notifier()
    {
        if (_systemToasts)
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated += args =>
                {
                    var arguments = ToastArguments.Parse(args.Argument);
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        BringToFront();
                        if (arguments.TryGetValue("project", out string project) && long.TryParse(project, out long id))
                            Services.Main.OpenProject(id);
                        else if (arguments.Contains("inbox"))
                            Services.Main.ShowInbox();
                    });
                };
            }
            catch (Exception)
            {
                _systemToasts = false;
            }
        }
    }

    public void Info(string message) => Show(new ToastItem { Title = message, Kind = "info" });
    public void Success(string message) => Show(new ToastItem { Title = message, Kind = "success" });
    public void Error(string message) => Show(new ToastItem { Title = message, Kind = "error" }, 7);

    public void Show(ToastItem toast, int seconds = 5)
    {
        Toasts.Add(toast);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(toast);
        };
        timer.Start();
    }

    public void Dismiss(ToastItem toast) => Toasts.Remove(toast);

    // A new inbox item (friend request, invite, mention, task...).
    public void InboxItem(JsonObject item)
    {
        UpdateBadge();
        if (!Services.Settings.Notifications) return;
        var from = item.Obj("from") is { } f ? Users.From(f) : null;
        string kind = item.Str("kind");
        string text = item.Str("text");
        long projectId = item.Long("projectId");
        string title = from != null ? $"{from.Name} {text}" : text;
        Action open = kind == "mention" && projectId > 0 ? () => Services.Main.OpenProject(projectId) : Services.Main.ShowInbox;
        Show(new ToastItem { Title = title, Kind = "message", User = from, Open = open }, 7);
        SystemToast(from?.Name ?? "gitHub Demo", text, kind == "mention" && projectId > 0 ? ("project", projectId.ToString()) : ("inbox", "1"));
    }

    // A chat message in an open project, shown when the user isn't looking.
    public void ChatMessage(string projectName, long projectId, UserRef from, string text)
    {
        if (!Services.Settings.Notifications || !Services.Settings.NotifyChat) return;
        if (Application.Current.MainWindow?.IsActive == true) return;
        SystemToast($"{from.Name} in {projectName}", text.Length > 200 ? text[..200] + "..." : text, ("project", projectId.ToString()));
        Flash();
    }

    private void SystemToast(string title, string body, (string Key, string Value) argument)
    {
        if (!_systemToasts || Application.Current.MainWindow?.IsActive == true) return;
        try
        {
            new ToastContentBuilder()
                .AddArgument(argument.Key, argument.Value)
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch (Exception)
        {
            // notifications unavailable (e.g. turned off by policy)
        }
        Flash();
    }

    // ---- taskbar ----

    public void UpdateBadge()
    {
        var window = Application.Current.MainWindow;
        if (window == null) return;
        window.TaskbarItemInfo ??= new TaskbarItemInfo();
        long count = Services.Session.Unread;
        window.TaskbarItemInfo.Overlay = count > 0 ? BadgeImage(count) : null;
        window.TaskbarItemInfo.Description = count > 0 ? $"{count} unread" : "";
    }

    private static ImageSource BadgeImage(long count)
    {
        string text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), new Pen(Brushes.White, 1.5), new Point(16, 16), 15, 15);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                text.Length > 2 ? 12 : 17, Brushes.White, 1.0);
            dc.DrawText(formatted, new Point(16 - formatted.Width / 2, 16 - formatted.Height / 2));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    private static void Flash()
    {
        var window = Application.Current.MainWindow;
        if (window == null || window.IsActive) return;
        var info = new FlashInfo
        {
            cbSize = (uint)Marshal.SizeOf<FlashInfo>(),
            hwnd = new WindowInteropHelper(window).Handle,
            dwFlags = 2 | 12, // taskbar button, until the window comes to the front
            uCount = 3,
        };
        FlashWindowEx(ref info);
    }

    public static void BringToFront()
    {
        var window = Application.Current.MainWindow;
        if (window == null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
