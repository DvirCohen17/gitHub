using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

// Someone's profile: picture, bio, presence, friendship, projects you can see.
public sealed partial class ProfileViewModel : PageViewModel
{
    private readonly string _name;

    public ProfileViewModel(string name)
    {
        _name = name;
    }

    public override string Title => _name;
    public override string NavKey => IsSelf ? "profile" : "";

    [ObservableProperty] private UserRef? _user;
    [ObservableProperty] private string _bio = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _memberSince = "";
    [ObservableProperty] private long _friendCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelf), nameof(PrimaryAction), nameof(HasPrimaryAction), nameof(CanRemove), nameof(RelationText))]
    private string _relation = "";

    public ObservableCollection<ProjectCard> Projects { get; } = new();

    public bool IsSelf => Relation == "self";
    public string PrimaryAction => Relation switch { "none" => "Add friend", "requestReceived" => "Accept request", "requestSent" => "Cancel request", _ => "" };
    public bool HasPrimaryAction => PrimaryAction.Length > 0;
    public bool CanRemove => Relation == "friend";
    public string RelationText => Relation switch
    {
        "friend" => "Friends",
        "requestSent" => "Friend request sent",
        "requestReceived" => "Wants to be your friend",
        _ => "",
    };

    protected override void Subscribe()
    {
        On("ev.friends", e => { _ = LoadAsync(); });
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var reply = await Services.Connection.RequestAsync("user.profile", new JsonObject { ["name"] = _name });
        User = Users.From(reply.Obj("user"));
        if (reply.Obj("presence") is { } presence) Users.ApplyPresence(User, presence);
        Bio = reply.Str("bio");
        Email = reply.Str("email");
        Relation = reply.Str("relation");
        FriendCount = reply.Long("friends");
        MemberSince = "Member since " + reply.Time("createdAt").ToString("MMMM yyyy");
        Projects.Clear();
        foreach (var p in reply.Objects("projects"))
        {
            Projects.Add(new ProjectCard
            {
                Id = p.Long("id"), Name = p.Str("name"), Language = CodeStyles.ProjectLanguageLabel(p.Str("language")),
                Role = p.Str("role"), Visibility = p.Str("visibility"),
            });
        }
    });

    [RelayCommand]
    private async Task PrimaryAsync()
    {
        if (User == null) return;
        await TryAsync(async () =>
        {
            switch (Relation)
            {
                case "none":
                    await Services.Connection.RequestAsync("friends.request", new JsonObject { ["name"] = User.Name });
                    break;
                case "requestReceived":
                    await Services.Connection.RequestAsync("friends.respond", new JsonObject { ["userId"] = User.Id, ["accept"] = true });
                    break;
                case "requestSent":
                    await Services.Connection.RequestAsync("friends.remove", new JsonObject { ["userId"] = User.Id });
                    break;
            }
            await LoadAsync();
        });
    }

    [RelayCommand]
    private async Task RemoveFriendAsync()
    {
        if (User == null) return;
        if (!await Services.Dialogs.ConfirmAsync("Remove friend", $"Remove {User.Name} from your friends?", "Remove", danger: true)) return;
        await TryAsync(async () =>
        {
            await Services.Connection.RequestAsync("friends.remove", new JsonObject { ["userId"] = User.Id });
            await LoadAsync();
        });
    }

    [RelayCommand] private void OpenProject(ProjectCard? card) { if (card != null) Services.Main.OpenProject(card.Id); }
    [RelayCommand] private void EditProfile() => Services.Main.Navigate(new SettingsViewModel("profile"));
}

public sealed partial class InboxItem : ObservableObject
{
    public long Id { get; init; }
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
    public UserRef? From { get; init; }
    public long ProjectId { get; init; }
    public string ProjectName { get; init; } = "";
    public DateTime Time { get; init; }
    [ObservableProperty] private bool _read;
    [ObservableProperty] private bool _answered;

    public string When => Formatting.Ago(Time);
    public bool NeedsAnswer => Kind is "friendRequest" or "projectInvite" or "joinRequest" && !Answered;
    public bool CanOpenProject => ProjectId > 0 && Kind is "mention" or "task" or "info" && !NeedsAnswer;
    public string Glyph => Kind switch
    {
        "friendRequest" => "",
        "projectInvite" => "",
        "joinRequest" => "",
        "mention" => "",
        "task" => "",
        _ => "",
    };

    partial void OnAnsweredChanged(bool value) => OnPropertyChanged(nameof(NeedsAnswer));

    public static InboxItem Parse(JsonObject i) => new()
    {
        Id = i.Long("id"),
        Kind = i.Str("kind"),
        Text = i.Str("text"),
        From = i.Obj("from") is { } f ? Users.From(f) : null,
        ProjectId = i.Long("projectId"),
        ProjectName = i.Str("projectName"),
        Time = i.Time("createdAt"),
        Read = i.Bool("read"),
    };
}

public sealed partial class InboxViewModel : PageViewModel
{
    public override string Title => "Inbox";
    public override string NavKey => "inbox";

    public ObservableCollection<InboxItem> Items { get; } = new();
    [ObservableProperty] private bool _showUnreadOnly;
    [ObservableProperty] private bool _loaded;
    public bool IsEmpty => Loaded && !Visible.Any();
    public IEnumerable<InboxItem> Visible => ShowUnreadOnly ? Items.Where(i => !i.Read) : Items;

    partial void OnShowUnreadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(IsEmpty));
    }

    protected override void Subscribe()
    {
        On("ev.inbox", e =>
        {
            if (e.Obj("item") is { } item)
            {
                Items.Insert(0, InboxItem.Parse(item));
                OnPropertyChanged(nameof(Visible));
                OnPropertyChanged(nameof(IsEmpty));
            }
        });
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("inbox.list");
            Items.Clear();
            foreach (var i in reply.Objects("items")) Items.Add(InboxItem.Parse(i));
            Services.Session.Unread = reply.Long("unread");
        });
        Loaded = true;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task MarkReadAsync(InboxItem item)
    {
        if (item.Read) return;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("inbox.markRead", new JsonObject { ["id"] = item.Id });
            item.Read = true;
            Services.Session.Unread = reply.Long("unread");
        });
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("inbox.markRead", new JsonObject { ["all"] = true });
            foreach (var i in Items) i.Read = true;
            Services.Session.Unread = reply.Long("unread");
            OnPropertyChanged(nameof(Visible));
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(InboxItem? item)
    {
        if (item == null) return;
        await TryAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("inbox.delete", new JsonObject { ["id"] = item.Id });
            Items.Remove(item);
            Services.Session.Unread = reply.Long("unread");
            OnPropertyChanged(nameof(Visible));
            OnPropertyChanged(nameof(IsEmpty));
        });
    }

    [RelayCommand]
    private async Task AcceptAsync(InboxItem? item) => await AnswerAsync(item, true);

    [RelayCommand]
    private async Task DeclineAsync(InboxItem? item) => await AnswerAsync(item, false);

    private async Task AnswerAsync(InboxItem? item, bool accept)
    {
        if (item == null || item.From == null && item.Kind != "projectInvite") return;
        bool ok = await TryAsync(async () =>
        {
            switch (item.Kind)
            {
                case "friendRequest":
                    await Services.Connection.RequestAsync("friends.respond", new JsonObject { ["userId"] = item.From!.Id, ["accept"] = accept });
                    break;
                case "projectInvite":
                    await Services.Connection.RequestAsync("project.respondInvite", new JsonObject { ["id"] = item.ProjectId, ["accept"] = accept });
                    break;
                case "joinRequest":
                    await Services.Connection.RequestAsync("project.respondJoin", new JsonObject
                    {
                        ["projectId"] = item.ProjectId, ["userId"] = item.From!.Id, ["accept"] = accept,
                    });
                    break;
            }
        });
        if (ok)
        {
            item.Answered = true;
            Services.Notifier.Success(accept ? "Accepted." : "Declined.");
            await LoadAsync(); // the server removes answered requests
        }
        else
        {
            await LoadAsync(); // e.g. the invite was withdrawn meanwhile
        }
    }

    [RelayCommand]
    private async Task OpenAsync(InboxItem? item)
    {
        if (item == null) return;
        await MarkReadAsync(item);
        if (item.CanOpenProject) Services.Main.OpenProject(item.ProjectId);
        else if (item.From != null && item.Kind == "info" && item.ProjectId == 0) Services.Main.OpenProfile(item.From.Name);
    }

    [RelayCommand] private Task Read(InboxItem? item) => item != null ? MarkReadAsync(item) : Task.CompletedTask;
}

// Public projects anyone can watch.
public sealed partial class ExploreViewModel : PageViewModel
{
    public override string Title => "Explore";
    public override string NavKey => "explore";

    public ObservableCollection<ProjectCard> Projects { get; } = new();
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _loaded;
    public bool IsEmpty => Loaded && Projects.Count == 0;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("project.public", new JsonObject { ["query"] = Query.Trim() });
            Projects.Clear();
            foreach (var p in reply.Objects("projects")) Projects.Add(ProjectCard.From(p));
        });
        Loaded = true;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand] private Task Search() => LoadAsync();
    [RelayCommand] private void Open(ProjectCard? card) { if (card != null) Services.Main.OpenProject(card.Id); }
}
