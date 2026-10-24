using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubDemo.Core;

// Global access to the app's services (one window, one connection, one user).
public static class Services
{
    public static AppSettings Settings { get; set; } = null!;
    public static Connection Connection { get; set; } = null!;
    public static Session Session { get; set; } = null!;
    public static ViewModels.MainViewModel Main { get; set; } = null!;
    public static Dialogs Dialogs { get; set; } = null!;
    public static Notifier Notifier { get; set; } = null!;
}

// The signed-in user and the connection's lifetime: hello/version check,
// sign-in, automatic reconnect, presence (online/away) and routing server
// events to whoever subscribed.
public sealed partial class Session : ObservableObject
{
    public const string ClientVersion = "2.0.0";

    private readonly Connection _conn;
    private readonly Dictionary<string, List<Action<JsonObject>>> _handlers = new();
    private readonly DispatcherTimer _pingTimer;
    private readonly DispatcherTimer _idleTimer;
    private string _passwordHash = "";
    private bool _reconnecting;
    private bool _away;

    public Session(Connection conn)
    {
        _conn = conn;
        _conn.EventReceived += OnEvent;
        _conn.Lost += OnLost;
        _pingTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, async (_, _) =>
        {
            if (!_conn.IsConnected) return;
            try { await _conn.RequestAsync("ping"); } catch { /* a dead connection is reported by Lost */ }
        }, Dispatcher.CurrentDispatcher);
        _idleTimer = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => CheckIdle(), Dispatcher.CurrentDispatcher);
    }

    [ObservableProperty] private UserRef? _me;
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _bio = "";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private long _unread;
    [ObservableProperty] private bool _isOnline = true;
    [ObservableProperty] private string _connectionMessage = "";
    [ObservableProperty] private string _serverVersion = "";

    public bool IsSignedIn => Me != null;

    // Raised after the connection came back and the user is signed in again:
    // pages reload what they show.
    public event Action? Reconnected;

    // ---- events ------------------------------------------------------------

    // Subscribes to a server event type ("ev.chat"); dispose the result to stop.
    public IDisposable On(string type, Action<JsonObject> handler)
    {
        if (!_handlers.TryGetValue(type, out var list)) _handlers[type] = list = new List<Action<JsonObject>>();
        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }

    private void OnEvent(JsonObject message)
    {
        string type = message.Str("t");
        switch (type)
        {
            case "ev.presence":
                Users.ApplyPresence(message.Long("userId"), message.Obj("presence") ?? new JsonObject());
                break;
            case "ev.user":
                Users.From(message.Obj("user"));
                break;
            case "ev.inbox":
                Unread = message.Long("unread");
                Services.Notifier.InboxItem(message.Obj("item") ?? new JsonObject());
                break;
            case "ev.kicked":
                _passwordHash = "";
                Services.Main.SignedOut(message.Str("reason", "You were signed out."));
                break;
        }
        if (_handlers.TryGetValue(type, out var list))
        {
            foreach (var handler in list.ToList())
            {
                try { handler(message); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"{type} handler failed: {ex}"); }
            }
        }
    }

    // ---- connecting & signing in ---------------------------------------------

    public sealed record Hello(bool UpdateRequired, string RequiredVersion, string Package);

    public async Task<Hello> ConnectAsync(string host, int port)
    {
        await _conn.ConnectAsync(host, port, TimeSpan.FromSeconds(6));
        var reply = await _conn.RequestAsync("hello", new JsonObject { ["clientVersion"] = ClientVersion });
        ServerVersion = reply.Str("serverVersion");
        IsOnline = true;
        ConnectionMessage = "";
        _pingTimer.Start();
        return new Hello(reply.Bool("updateRequired"), reply.Str("clientVersion"), reply.Str("package"));
    }

    public Task<JsonObject> LoginAsync(string user, string passwordHash) =>
        SignInAsync("auth.login", new JsonObject { ["user"] = user, ["password"] = passwordHash }, passwordHash);

    public Task<JsonObject> SignupAsync(string name, string email, string passwordHash) =>
        SignInAsync("auth.signup", new JsonObject { ["name"] = name, ["email"] = email, ["password"] = passwordHash }, passwordHash);

    private async Task<JsonObject> SignInAsync(string type, JsonObject fields, string passwordHash)
    {
        var reply = await _conn.RequestAsync(type, fields);
        _passwordHash = passwordHash;
        Users.Clear();
        Me = Users.From(reply.Obj("user"));
        Me.Status = "online";
        Email = reply.Str("email");
        Bio = reply.Str("bio");
        IsAdmin = reply.Bool("isAdmin");
        Unread = reply.Long("unread");
        _away = false;
        _idleTimer.Start();
        OnPropertyChanged(nameof(IsSignedIn));
        return reply;
    }

    public async Task LogoutAsync()
    {
        try { await _conn.RequestAsync("auth.logout", timeoutMs: 3000); } catch { }
        Clear();
    }

    public void Clear()
    {
        _passwordHash = "";
        _idleTimer.Stop();
        Me = null;
        IsAdmin = false;
        Unread = 0;
        OnPropertyChanged(nameof(IsSignedIn));
    }

    // ---- reconnecting --------------------------------------------------------

    private async void OnLost()
    {
        IsOnline = false;
        _pingTimer.Stop();
        if (_reconnecting) return;
        _reconnecting = true;
        try
        {
            int delay = 1;
            for (int attempt = 1; ; attempt++)
            {
                ConnectionMessage = attempt == 1 ? "Connection lost. Reconnecting..." : $"Can't reach the server. Retrying in {delay} s...";
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 1 : delay));
                try
                {
                    ConnectionMessage = "Reconnecting...";
                    await ConnectAsync(_conn.Host, _conn.Port);
                    if (Me != null && _passwordHash.Length > 0)
                    {
                        try
                        {
                            await SignInAsync("auth.login", new JsonObject { ["user"] = Me.Name, ["password"] = _passwordHash }, _passwordHash);
                        }
                        catch (ServerException ex)
                        {
                            Services.Main.SignedOut(ex.Message);
                            return;
                        }
                    }
                    IsOnline = true;
                    ConnectionMessage = "";
                    Reconnected?.Invoke();
                    Services.Notifier.Info("Reconnected to the server.");
                    return;
                }
                catch (Exception)
                {
                    delay = Math.Min(delay * 2, 15);
                }
            }
        }
        finally
        {
            _reconnecting = false;
        }
    }

    // ---- away after 5 minutes without input -----------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private void CheckIdle()
    {
        if (Me == null || !_conn.IsConnected) return;
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return;
        uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime); // both wrap after 49 days
        double idleSeconds = idleMs / 1000.0;
        bool away = idleSeconds > 300;
        if (away == _away) return;
        _away = away;
        Me.Status = away ? "away" : "online";
        _conn.Send("presence.set", new JsonObject { ["status"] = away ? "away" : "online" });
    }
}
