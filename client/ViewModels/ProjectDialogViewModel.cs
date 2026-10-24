using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

public sealed partial class InviteChoice : ObservableObject
{
    public required UserRef User { get; init; }
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _asAdmin;
}

public sealed partial class MemberEdit : ObservableObject
{
    public required UserRef User { get; init; }
    public required string OriginalRole { get; init; }
    [ObservableProperty] private string _role = "member";
    [ObservableProperty] private bool _remove;
    public bool IsOwner => OriginalRole == "owner";
    public bool IsMe => User.IsMe;
    public bool CanChange { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = new[] { "member", "admin" };
}

public sealed class PendingInvite
{
    public required UserRef User { get; init; }
    public string Role { get; init; } = "";
    public bool Cancel { get; set; }
}

// Create a project, or edit one: name, description, visibility, members and
// roles, invites, and the invite code.
public sealed partial class ProjectDialogViewModel : ViewModelBase
{
    public ProjectDialogViewModel(long projectId = 0, bool readOnly = false)
    {
        ProjectId = projectId;
        IsReadOnly = readOnly;
        _languageKey = "cpp";
    }

    public long ProjectId { get; private set; }
    public bool IsCreate => ProjectId == 0;
    public bool IsReadOnly { get; }
    public string DialogTitle => IsCreate ? "New project" : IsReadOnly ? "Project details" : "Edit project";
    public string SaveText => IsCreate ? "Create project" : "Save changes";
    public IReadOnlyList<(string Key, string Label, string Extension)> LanguageOptions => CodeStyles.ProjectLanguages;
    public IEnumerable<KeyValuePair<string, string>> Languages => CodeStyles.ProjectLanguages.Select(l => new KeyValuePair<string, string>(l.Key, l.Label));

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _languageKey;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isPublic;
    [ObservableProperty] private string _myRole = "owner";
    [ObservableProperty] private string _inviteCode = "";
    [ObservableProperty] private bool _autoApprove;
    [ObservableProperty] private string _section = "general";

    public ObservableCollection<InviteChoice> Friends { get; } = new();
    public ObservableCollection<MemberEdit> Members { get; } = new();
    public ObservableCollection<PendingInvite> PendingInvites { get; } = new();

    public bool HasInviteCode => InviteCode.Length > 0;
    public string InviteLink => $"githubdemo://join/{InviteCode}";
    public bool IsOwner => MyRole == "owner";
    public bool HasFriendsToInvite => Friends.Count > 0;

    partial void OnInviteCodeChanged(string value)
    {
        OnPropertyChanged(nameof(HasInviteCode));
        OnPropertyChanged(nameof(InviteLink));
    }

    // Set by the dialog window: closes it with a result.
    public Action<bool>? Close { get; set; }
    public Window? Owner { get; set; }
    public long CreatedProjectId { get; private set; }

    private bool _loading;

    public async Task LoadAsync()
    {
        _loading = true;
        await RunAsync(async () =>
        {
            var friends = await Services.Connection.RequestAsync("friends.list");
            var memberIds = new HashSet<long>();
            var invitedIds = new HashSet<long>();
            if (!IsCreate)
            {
                var reply = await Services.Connection.RequestAsync("project.get", new JsonObject { ["id"] = ProjectId });
                var p = reply.Obj("project")!;
                Name = p.Str("name");
                LanguageKey = p.Str("language");
                Description = p.Str("description");
                IsPublic = p.Str("visibility") == "public";
                MyRole = p.Str("myRole");
                InviteCode = p.Str("inviteCode");
                AutoApprove = p.Bool("inviteAutoApprove");
                foreach (var m in p.Objects("members"))
                {
                    var user = Users.From(m.Obj("user"));
                    string role = m.Str("role");
                    memberIds.Add(user.Id);
                    bool canChange = !IsReadOnly && role != "owner" && !user.IsMe && (MyRole == "owner" || role == "member");
                    Members.Add(new MemberEdit
                    {
                        User = user, OriginalRole = role, Role = role, CanChange = canChange,
                        Roles = MyRole == "owner" ? new[] { "member", "admin", "owner" } : new[] { "member", "admin" },
                    });
                }
                foreach (var i in p.Objects("invites"))
                {
                    var user = Users.From(i.Obj("user"));
                    invitedIds.Add(user.Id);
                    PendingInvites.Add(new PendingInvite { User = user, Role = i.Str("role") });
                }
            }
            foreach (var f in friends.Objects("friends"))
            {
                var user = Users.From(f);
                if (!memberIds.Contains(user.Id) && !invitedIds.Contains(user.Id)) Friends.Add(new InviteChoice { User = user });
            }
            OnPropertyChanged(nameof(HasFriendsToInvite));
        });
        _loading = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsReadOnly)
        {
            Close?.Invoke(false);
            return;
        }
        string name = Name.Trim();
        if (name.Length is 0 or > 50)
        {
            Error = "Give the project a name (up to 50 characters).";
            Section = "general";
            return;
        }
        var invites = new JsonArray();
        foreach (var f in Friends.Where(f => f.IsChecked))
            invites.Add(new JsonObject { ["name"] = f.User.Name, ["role"] = f.AsAdmin ? "admin" : "member" });

        if (IsCreate)
        {
            await RunAsync(async () =>
            {
                var reply = await Services.Connection.RequestAsync("project.create", new JsonObject
                {
                    ["name"] = name, ["language"] = LanguageKey, ["description"] = Description.Trim(),
                    ["visibility"] = IsPublic ? "public" : "private", ["invites"] = invites,
                });
                CreatedProjectId = reply.Obj("project").Long("id");
                Close?.Invoke(true);
            });
            return;
        }

        var newOwner = Members.FirstOrDefault(m => m.Role == "owner" && m.OriginalRole != "owner" && !m.Remove);
        // (this dialog is modal, so ask with a message box rather than the main window's overlay)
        if (newOwner != null && MessageBox.Show(Owner!, $"Make {newOwner.User.Name} the owner? You become an admin and can't undo this yourself.",
                "Hand over the project", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        var fields = new JsonObject
        {
            ["id"] = ProjectId, ["name"] = name, ["description"] = Description.Trim(),
            ["visibility"] = IsPublic ? "public" : "private", ["invites"] = invites,
        };
        var roles = new JsonArray();
        foreach (var m in Members.Where(m => m.CanChange && !m.Remove && m.Role != m.OriginalRole))
            roles.Add(new JsonObject { ["userId"] = m.User.Id, ["role"] = m.Role });
        if (roles.Count > 0) fields["roles"] = roles;
        var remove = new JsonArray();
        foreach (var m in Members.Where(m => m.CanChange && m.Remove)) remove.Add(m.User.Id);
        if (remove.Count > 0) fields["remove"] = remove;
        var cancel = new JsonArray();
        foreach (var i in PendingInvites.Where(i => i.Cancel)) cancel.Add(i.User.Id);
        if (cancel.Count > 0) fields["cancelInvites"] = cancel;

        await RunAsync(async () =>
        {
            await Services.Connection.RequestAsync("project.update", fields);
            Close?.Invoke(true);
        });
    }

    [RelayCommand] private void Cancel() => Close?.Invoke(false);
    [RelayCommand] private void ShowSection(string section) => Section = section;

    // ---- invite code ----

    [RelayCommand]
    private async Task CreateCodeAsync()
    {
        await RunAsync(async () =>
        {
            var reply = await Services.Connection.RequestAsync("project.inviteCode", new JsonObject { ["id"] = ProjectId, ["action"] = "create", ["autoApprove"] = AutoApprove });
            InviteCode = reply.Str("inviteCode");
        });
    }

    [RelayCommand]
    private async Task RevokeCodeAsync()
    {
        await RunAsync(async () =>
        {
            await Services.Connection.RequestAsync("project.inviteCode", new JsonObject { ["id"] = ProjectId, ["action"] = "revoke" });
            InviteCode = "";
        });
    }

    partial void OnAutoApproveChanged(bool value)
    {
        if (_loading || IsCreate || !HasInviteCode || IsReadOnly) return;
        _ = RunAsync(() => Services.Connection.RequestAsync("project.inviteCode", new JsonObject { ["id"] = ProjectId, ["action"] = "settings", ["autoApprove"] = value }));
    }

    [RelayCommand]
    private void CopyCode()
    {
        Clipboard.SetText(InviteCode);
        Services.Notifier.Success("Invite code copied.");
    }

    [RelayCommand]
    private void CopyLink()
    {
        Clipboard.SetText(InviteLink);
        Services.Notifier.Success("Invite link copied.");
    }
}
