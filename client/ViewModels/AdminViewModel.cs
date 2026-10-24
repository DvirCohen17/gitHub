using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

public sealed partial class AdminUser : ObservableObject
{
    public required UserRef User { get; init; }
    public string Email { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? LastSeen { get; init; }
    public long Projects { get; init; }
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _banned;
    [ObservableProperty] private string _banReason = "";
    [ObservableProperty] private bool _online;
    public string Joined => CreatedAt.ToString("d MMM yyyy");
    public string Seen => Online ? "Online now" : LastSeen is { } s && s.Year > 2000 ? "Seen " + Formatting.Ago(s) : "Never signed in";
    public bool IsMe => User.IsMe;
}

public sealed class AdminProject
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public UserRef? Owner { get; init; }
    public long Members { get; init; }
    public long Files { get; init; }
    public string Visibility { get; init; } = "";
    public string Language { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
    public string Updated => Formatting.Ago(UpdatedAt);
}

// Server administration (server admins only): statistics, accounts, projects.
public sealed partial class AdminViewModel : PageViewModel
{
    private readonly DispatcherTimer _refresh;

    public AdminViewModel()
    {
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refresh.Tick += async (_, _) => await LoadStatsAsync();
    }

    public override string Title => "Server admin";
    public override string NavKey => "admin";

    [ObservableProperty] private string _tab = "users";
    [ObservableProperty] private string _userQuery = "";
    [ObservableProperty] private string _projectQuery = "";
    [ObservableProperty] private string _uptime = "";
    [ObservableProperty] private long _onlineUsers;
    [ObservableProperty] private long _sessions;
    [ObservableProperty] private long _users;
    [ObservableProperty] private long _bannedUsers;
    [ObservableProperty] private long _projects;
    [ObservableProperty] private long _files;
    [ObservableProperty] private long _openFiles;
    [ObservableProperty] private long _requestsPerMinute;
    [ObservableProperty] private long _runs;
    [ObservableProperty] private bool _runEnabled;

    public ObservableCollection<AdminUser> UserList { get; } = new();
    public ObservableCollection<AdminProject> ProjectList { get; } = new();

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            await LoadStatsAsync();
            await LoadUsersAsync();
            await LoadProjectsAsync();
        });
        _refresh.Start();
    }

    public override void OnLeave()
    {
        _refresh.Stop();
        base.OnLeave();
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var s = await Services.Connection.RequestAsync("admin.stats");
            var up = TimeSpan.FromMilliseconds(s.Long("uptime"));
            Uptime = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : up.TotalHours >= 1 ? $"{(int)up.TotalHours}h {up.Minutes}m" : $"{up.Minutes}m";
            OnlineUsers = s.Long("onlineUsers");
            Sessions = s.Long("sessions");
            Users = s.Long("users");
            BannedUsers = s.Long("bannedUsers");
            Projects = s.Long("projects");
            Files = s.Long("files");
            OpenFiles = s.Long("openFiles");
            RequestsPerMinute = s.Long("requestsPerMinute");
            Runs = s.Long("runs");
            RunEnabled = s.Bool("runEnabled");
        }
        catch (Exception)
        {
            // keep the last numbers
        }
    }

    [RelayCommand]
    private async Task LoadUsersAsync()
    {
        var reply = await Services.Connection.RequestAsync("admin.users", new JsonObject { ["query"] = UserQuery.Trim() });
        UserList.Clear();
        foreach (var u in reply.Objects("users"))
        {
            UserList.Add(new AdminUser
            {
                User = Core.Users.From(u), Email = u.Str("email"), CreatedAt = u.Time("createdAt"),
                LastSeen = u.Long("lastSeen") > 0 ? u.Time("lastSeen") : null, Projects = u.Long("projects"),
                IsAdmin = u.Bool("isAdmin"), Banned = u.Bool("banned"), BanReason = u.Str("banReason"), Online = u.Bool("online"),
            });
        }
    }

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        var reply = await Services.Connection.RequestAsync("admin.projects", new JsonObject { ["query"] = ProjectQuery.Trim() });
        ProjectList.Clear();
        foreach (var p in reply.Objects("projects"))
        {
            ProjectList.Add(new AdminProject
            {
                Id = p.Long("id"), Name = p.Str("name"), Owner = p.Obj("owner") is { } o ? Core.Users.From(o) : null,
                Members = p.Long("members"), Files = p.Long("files"), Visibility = p.Str("visibility"),
                Language = CodeStyles.ProjectLanguageLabel(p.Str("language")), UpdatedAt = p.Time("updatedAt"),
            });
        }
    }

    [RelayCommand]
    private async Task BanAsync(AdminUser? user)
    {
        if (user == null) return;
        if (user.Banned)
        {
            if (await TryAsync(() => Services.Connection.RequestAsync("admin.ban", new JsonObject { ["userId"] = user.User.Id, ["banned"] = false })))
            {
                user.Banned = false;
                Services.Notifier.Success($"{user.User.Name} can sign in again.");
            }
            return;
        }
        string? reason = await Services.Dialogs.PromptAsync($"Suspend {user.User.Name}", "Reason (shown to them)", "", "Suspend",
            message: "They're signed out at once and can't sign in until you lift the suspension.");
        if (reason == null) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("admin.ban", new JsonObject { ["userId"] = user.User.Id, ["banned"] = true, ["reason"] = reason })))
        {
            user.Banned = true;
            user.BanReason = reason;
            user.Online = false;
            Services.Notifier.Success($"{user.User.Name} is suspended.");
        }
    }

    [RelayCommand]
    private async Task KickAsync(AdminUser? user)
    {
        if (user == null) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("admin.kick", new JsonObject { ["userId"] = user.User.Id })))
        {
            user.Online = false;
            Services.Notifier.Success($"{user.User.Name} was disconnected.");
        }
    }

    [RelayCommand]
    private async Task ToggleAdminAsync(AdminUser? user)
    {
        if (user == null) return;
        bool make = !user.IsAdmin;
        if (!await Services.Dialogs.ConfirmAsync(make ? "Make admin" : "Remove admin",
                make ? $"Give {user.User.Name} full control of this server?" : $"Take server-admin rights from {user.User.Name}?",
                make ? "Make admin" : "Remove", danger: !make)) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("admin.setAdmin", new JsonObject { ["userId"] = user.User.Id, ["isAdmin"] = make })))
            user.IsAdmin = make;
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(AdminProject? project)
    {
        if (project == null) return;
        if (!await Services.Dialogs.ConfirmAsync("Delete project",
                $"Delete {project.Name} and everything in it? Its {project.Members} member(s) are told it was removed by an administrator.", "Delete", danger: true)) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("admin.deleteProject", new JsonObject { ["id"] = project.Id })))
        {
            ProjectList.Remove(project);
            Services.Notifier.Success($"{project.Name} was deleted.");
        }
    }

    [RelayCommand] private void OpenUser(AdminUser? user) { if (user != null) Services.Main.OpenProfile(user.User.Name); }
    [RelayCommand] private void OpenProject(AdminProject? project) { if (project != null) Services.Main.OpenProject(project.Id); }
}
