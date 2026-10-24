using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

public sealed class ProjectCard
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Language { get; init; } = "";
    public string Description { get; init; } = "";
    public string Role { get; init; } = "";
    public string Visibility { get; init; } = "";
    public long Members { get; init; }
    public DateTime UpdatedAt { get; init; }
    public UserRef? Owner { get; init; }
    public string Updated => "Updated " + Formatting.Ago(UpdatedAt);
    public string MembersText => Members == 1 ? "1 member" : $"{Members} members";
    public bool IsPublic => Visibility == "public";
    public string RoleLabel => Role switch { "owner" => "Owner", "admin" => "Admin", "member" => "Member", _ => "Viewer" };

    public static ProjectCard From(JsonObject p) => new()
    {
        Id = p.Long("id"),
        Name = p.Str("name"),
        Language = CodeStyles.ProjectLanguageLabel(p.Str("language")),
        Description = p.Str("description"),
        Role = p.Str("role"),
        Visibility = p.Str("visibility"),
        Members = p.Long("members"),
        UpdatedAt = p.Time("updatedAt"),
        Owner = p.Obj("owner") is { } o ? Users.From(o) : null,
    };
}

public sealed partial class InviteItem : ObservableObject
{
    public long ProjectId { get; init; }
    public string ProjectName { get; init; } = "";
    public string Role { get; init; } = "";
    public UserRef? From { get; init; }
    public Func<InviteItem, bool, Task>? Respond { get; init; }

    [RelayCommand] private Task Accept() => Respond!(this, true);
    [RelayCommand] private Task Decline() => Respond!(this, false);
}

public sealed partial class PersonItem : ObservableObject
{
    public required UserRef User { get; init; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText), nameof(HasAction))]
    private string _relation = "none";
    public Func<PersonItem, Task>? Act { get; init; }

    public string ActionText => Relation switch
    {
        "none" => "Add friend",
        "requestReceived" => "Accept",
        "requestSent" => "Requested",
        "friend" => "Friends",
        _ => "",
    };
    public bool HasAction => Relation is "none" or "requestReceived";

    [RelayCommand] private void Open() => Services.Main.OpenProfile(User.Name);
    [RelayCommand] private Task Action() => Act != null && HasAction ? Act(this) : Task.CompletedTask;
}

public sealed class ActivityItem
{
    public long Id { get; init; }
    public UserRef? User { get; init; }
    public long ProjectId { get; init; }
    public string ProjectName { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
    public DateTime Time { get; init; }
    public string When => Formatting.Ago(Time);
    public string Glyph => Kind switch
    {
        "project.created" => "",
        "member.joined" => "",
        "member.left" => "",
        "file.created" or "files.imported" => "",
        "file.deleted" or "folder.deleted" => "",
        "file.renamed" or "folder.renamed" => "",
        "task.created" => "",
        "task.done" => "",
        "version.created" or "version.restored" => "",
        _ => "",
    };

    public static ActivityItem From(JsonObject a) => new()
    {
        Id = a.Long("id"),
        User = a.Obj("user") is { } u ? Users.From(u) : null,
        ProjectId = a.Long("projectId"),
        ProjectName = a.Str("projectName"),
        Kind = a.Str("kind"),
        Text = a.Str("text"),
        Time = a.Time("createdAt"),
    };
}

public sealed partial class HomeViewModel : PageViewModel
{
    private readonly DispatcherTimer _searchTimer;
    private List<ProjectCard> _allProjects = new();

    public HomeViewModel()
    {
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await SearchAsync();
        };
    }

    public override string Title => "Home";
    public override string NavKey => "home";
    public Session Session => Services.Session;

    public ObservableCollection<ProjectCard> Projects { get; } = new();
    public ObservableCollection<InviteItem> Invites { get; } = new();
    public ObservableCollection<UserRef> Friends { get; } = new();
    public ObservableCollection<PersonItem> FriendRequests { get; } = new();
    public ObservableCollection<PersonItem> SearchResults { get; } = new();
    public ObservableCollection<ActivityItem> Activity { get; } = new();

    [ObservableProperty] private string _projectFilter = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _joinCode = "";
    [ObservableProperty] private bool _isEditingBio;
    [ObservableProperty] private string _bioDraft = "";
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _hasMoreActivity;

    public string Greeting
    {
        get
        {
            int hour = DateTime.Now.Hour;
            string part = hour < 5 ? "Good night" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            return $"{part}, {Services.Session.Me?.Name}";
        }
    }

    public bool HasProjects => Projects.Count > 0;
    public bool HasInvites => Invites.Count > 0;
    public bool HasFriends => Friends.Count > 0;
    public bool HasFriendRequests => FriendRequests.Count > 0;
    public bool HasActivity => Activity.Count > 0;
    public bool IsSearching => SearchText.Trim().Length > 0;
    public int OnlineFriends => Friends.Count(f => f.IsOnline);

    protected override void Subscribe()
    {
        On("ev.projects", e => { _ = LoadProjectsAsync(); });
        On("ev.friends", e => { _ = LoadFriendsAsync(); });
        On("ev.presence", _ => SortFriends());
        On("ev.activity", e =>
        {
            if (e.Obj("item") is { } item && Activity.All(a => a.Id != item.Long("id")))
            {
                Activity.Insert(0, ActivityItem.From(item));
                OnPropertyChanged(nameof(HasActivity));
            }
        });
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            await Task.WhenAll(LoadProjectsAsync(), LoadFriendsAsync(), LoadActivityAsync(false));
        });
        Loaded = true;
    }

    private async Task LoadProjectsAsync()
    {
        var reply = await Services.Connection.RequestAsync("project.list");
        _allProjects = reply.Objects("projects").Select(ProjectCard.From).ToList();
        ApplyProjectFilter();
        Invites.Clear();
        foreach (var i in reply.Objects("invites"))
        {
            Invites.Add(new InviteItem
            {
                ProjectId = i.Long("projectId"),
                ProjectName = i.Str("name"),
                Role = i.Str("role") == "admin" ? "an admin" : "a member",
                From = i.Obj("from") is { } f ? Users.From(f) : null,
                Respond = RespondInviteAsync,
            });
        }
        OnPropertyChanged(nameof(HasInvites));
    }

    partial void OnProjectFilterChanged(string value) => ApplyProjectFilter();

    private void ApplyProjectFilter()
    {
        string filter = ProjectFilter.Trim();
        Projects.Clear();
        foreach (var p in _allProjects.Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                                  || p.Language.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
            Projects.Add(p);
        OnPropertyChanged(nameof(HasProjects));
    }

    private async Task LoadFriendsAsync()
    {
        var reply = await Services.Connection.RequestAsync("friends.list");
        Friends.Clear();
        foreach (var f in reply.Objects("friends")) Friends.Add(Users.From(f));
        SortFriends();
        FriendRequests.Clear();
        foreach (var u in reply.Objects("incoming"))
            FriendRequests.Add(new PersonItem { User = Users.From(u), Relation = "requestReceived", Act = AcceptRequestAsync });
        OnPropertyChanged(nameof(HasFriends));
        OnPropertyChanged(nameof(HasFriendRequests));
    }

    private void SortFriends()
    {
        var sorted = Friends.OrderBy(f => f.Status == "online" ? 0 : f.Status == "away" ? 1 : 2).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (int k = 0; k < sorted.Count; k++)
        {
            int at = Friends.IndexOf(sorted[k]);
            if (at != k) Friends.Move(at, k);
        }
        OnPropertyChanged(nameof(OnlineFriends));
    }

    private async Task LoadActivityAsync(bool more)
    {
        var fields = new JsonObject();
        if (more && Activity.Count > 0) fields["before"] = Activity[^1].Id;
        var reply = await Services.Connection.RequestAsync("activity.feed", fields);
        if (!more) Activity.Clear();
        var items = reply.Objects("items").Select(ActivityItem.From).ToList();
        foreach (var item in items) Activity.Add(item);
        HasMoreActivity = items.Count >= 40;
        OnPropertyChanged(nameof(HasActivity));
    }

    [RelayCommand] private Task MoreActivity() => TryAsync(() => LoadActivityAsync(true));

    // ---- actions ----

    [RelayCommand] private void OpenProject(ProjectCard? card) { if (card != null) Services.Main.OpenProject(card.Id); }
    [RelayCommand] private void OpenActivity(ActivityItem? item) { if (item != null) Services.Main.OpenProject(item.ProjectId); }
    [RelayCommand] private void OpenFriend(UserRef? user) { if (user != null) Services.Main.OpenProfile(user.Name); }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        var created = await Views.ProjectDialog.CreateAsync();
        if (created > 0) Services.Main.OpenProject(created);
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        if (JoinCode.Trim().Length == 0) return;
        await Services.Main.JoinWithCodeAsync(JoinCode.Trim());
        JoinCode = "";
    }

    private async Task RespondInviteAsync(InviteItem invite, bool accept)
    {
        if (await TryAsync(() => Services.Connection.RequestAsync("project.respondInvite", new JsonObject { ["id"] = invite.ProjectId, ["accept"] = accept })))
        {
            Invites.Remove(invite);
            OnPropertyChanged(nameof(HasInvites));
            if (accept)
            {
                Services.Notifier.Success($"You joined {invite.ProjectName}.");
                await LoadProjectsAsync();
            }
        }
    }

    private async Task AcceptRequestAsync(PersonItem person)
    {
        if (await TryAsync(() => Services.Connection.RequestAsync("friends.respond", new JsonObject { ["userId"] = person.User.Id, ["accept"] = true })))
        {
            FriendRequests.Remove(person);
            person.Relation = "friend";
            await LoadFriendsAsync();
        }
    }

    [RelayCommand]
    private async Task DeclineRequestAsync(PersonItem? person)
    {
        if (person == null) return;
        if (await TryAsync(() => Services.Connection.RequestAsync("friends.respond", new JsonObject { ["userId"] = person.User.Id, ["accept"] = false })))
        {
            FriendRequests.Remove(person);
            OnPropertyChanged(nameof(HasFriendRequests));
        }
    }

    // ---- people search ----

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        _searchTimer.Stop();
        if (value.Trim().Length == 0) SearchResults.Clear();
        else _searchTimer.Start();
    }

    private async Task SearchAsync()
    {
        string query = SearchText.Trim();
        if (query.Length == 0) return;
        try
        {
            var reply = await Services.Connection.RequestAsync("user.search", new JsonObject { ["query"] = query });
            if (query != SearchText.Trim()) return; // typed on meanwhile
            SearchResults.Clear();
            foreach (var u in reply.Objects("users"))
            {
                if (u.Str("relation") == "self") continue;
                SearchResults.Add(new PersonItem { User = Users.From(u), Relation = u.Str("relation"), Act = AddFriendAsync });
            }
        }
        catch (Exception)
        {
            // keep the previous results
        }
    }

    private async Task AddFriendAsync(PersonItem person)
    {
        if (person.Relation == "requestReceived")
        {
            await AcceptRequestAsync(person);
            return;
        }
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("friends.request", new JsonObject { ["name"] = person.User.Name });
            person.Relation = reply.Str("state") == "friends" ? "friend" : "requestSent";
            Services.Notifier.Success(person.Relation == "friend" ? $"You and {person.User.Name} are now friends." : $"Friend request sent to {person.User.Name}.");
        });
    }

    // ---- bio ----

    [RelayCommand]
    private void EditBio()
    {
        BioDraft = Services.Session.Bio;
        IsEditingBio = true;
    }

    [RelayCommand] private void CancelBio() => IsEditingBio = false;

    [RelayCommand]
    private async Task SaveBioAsync()
    {
        if (await TryAsync(async () =>
            {
                var reply = await Services.Connection.RequestAsync("user.updateProfile", new JsonObject { ["bio"] = BioDraft });
                Services.Session.Bio = reply.Str("bio");
            }))
        {
            IsEditingBio = false;
        }
    }
}
