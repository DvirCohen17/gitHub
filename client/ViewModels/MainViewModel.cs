using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

// The main window: the current page, the sidebar, dialogs and notifications.
// Every screen is a page in this one window, so moving around never opens or
// closes windows; only "New / Edit project" is a separate dialog.
public sealed partial class MainViewModel : ObservableObject
{
    private string? _pendingLink; // a githubdemo:// link that arrived before sign-in

    public MainViewModel()
    {
        Session.Reconnected += async () =>
        {
            if (CurrentPage != null)
            {
                try { await CurrentPage.OnReconnectedAsync(); } catch { }
            }
        };
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Core.Session.Unread)) Services.Notifier.UpdateBadge();
            if (e.PropertyName is nameof(Core.Session.Me) or nameof(Core.Session.IsAdmin)) OnPropertyChanged(nameof(ShowSidebar));
        };
    }

    public Session Session => Services.Session;
    public Notifier Notifier => Services.Notifier;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSidebar), nameof(WindowTitle), nameof(NavKey))]
    private PageViewModel? _currentPage;

    [ObservableProperty] private DialogViewModel? _activeDialog;

    public bool ShowSidebar => CurrentPage?.ShowsSidebar == true && Session.Me != null;
    public string NavKey => CurrentPage?.NavKey ?? "";
    public string WindowTitle => string.IsNullOrEmpty(CurrentPage?.Title) ? "gitHub Demo" : $"{CurrentPage!.Title} - gitHub Demo";

    // ---- navigation -------------------------------------------------------------

    private bool _navigating;

    public async Task NavigateAsync(PageViewModel page)
    {
        if (_navigating) return;
        _navigating = true;
        try
        {
            if (CurrentPage != null)
            {
                if (!await CurrentPage.CanLeaveAsync()) return;
                CurrentPage.OnLeave();
            }
            CurrentPage = page;
        }
        finally
        {
            _navigating = false;
        }
        try
        {
            await page.EnterAsync();
        }
        catch (Exception ex)
        {
            page.Error = ViewModelBase.Describe(ex);
        }
    }

    public void Navigate(PageViewModel page) => _ = NavigateAsync(page);

    [RelayCommand] private void GoHome() => Navigate(new HomeViewModel());
    [RelayCommand] private void GoInbox() => Navigate(new InboxViewModel());
    [RelayCommand] private void GoExplore() => Navigate(new ExploreViewModel());
    [RelayCommand] private void GoSettings() => Navigate(new SettingsViewModel());
    [RelayCommand] private void GoAdmin() => Navigate(new AdminViewModel());
    [RelayCommand] private void GoMyProfile() { if (Session.Me != null) Navigate(new ProfileViewModel(Session.Me.Name)); }

    public void ShowInbox() => Navigate(new InboxViewModel());
    public void OpenProfile(string name) => Navigate(new ProfileViewModel(name));
    public void OpenProject(long id) => Navigate(new ProjectViewModel(id));
    public void OpenTasks(ProjectViewModel project) => Navigate(new TasksViewModel(project.ProjectId, project.Name, project.CanEdit));

    [RelayCommand]
    private async Task SignOut()
    {
        if (!await Services.Dialogs.ConfirmAsync("Sign out", "Sign out of gitHub Demo?", "Sign out")) return;
        Services.Settings.RememberedSecret = "";
        Services.Settings.Save();
        if (CurrentPage != null && !await CurrentPage.CanLeaveAsync()) return;
        CurrentPage?.OnLeave();
        CurrentPage = null;
        await Session.LogoutAsync();
        await NavigateAsync(new LoginViewModel());
    }

    // The server ended the session (kicked, banned, password changed...).
    public void SignedOut(string reason)
    {
        CurrentPage?.OnLeave();
        CurrentPage = null;
        Session.Clear();
        Services.Settings.RememberedSecret = "";
        Services.Settings.Save();
        Navigate(new LoginViewModel { Error = reason });
    }

    // ---- start-up -------------------------------------------------------------------

    public async Task StartAsync(string? link)
    {
        _pendingLink = link;
        var settings = Services.Settings;
        if (string.IsNullOrWhiteSpace(settings.ServerHost))
        {
            await NavigateAsync(new ConnectViewModel());
            return;
        }
        await NavigateAsync(new ConnectViewModel(autoConnect: true));
    }

    // After connecting: sign in with the remembered account, or show the sign-in page.
    public async Task AfterConnectedAsync()
    {
        var settings = Services.Settings;
        string secret = Secrets.Unprotect(settings.RememberedSecret);
        if (settings.RememberedUser.Length > 0 && secret.Length > 0)
        {
            try
            {
                await Session.LoginAsync(settings.RememberedUser, secret);
                await AfterSignInAsync();
                return;
            }
            catch (Exception ex) when (ex is ServerException)
            {
                await NavigateAsync(new LoginViewModel { Error = ex.Message });
                return;
            }
            catch (Exception)
            {
                // fall through to the sign-in page
            }
        }
        await NavigateAsync(new LoginViewModel());
    }

    public async Task AfterSignInAsync()
    {
        Services.Notifier.UpdateBadge();
        OnPropertyChanged(nameof(ShowSidebar));
        await NavigateAsync(new HomeViewModel());
        if (_pendingLink != null)
        {
            string link = _pendingLink;
            _pendingLink = null;
            await HandleLinkAsync(link);
        }
    }

    // githubdemo://join/CODE (invite link) or githubdemo://project/ID (public project).
    public async Task HandleLinkAsync(string link)
    {
        Notifier.BringToFront();
        if (Session.Me == null)
        {
            _pendingLink = link;
            Notifier.Info("Sign in to open the link.");
            return;
        }
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "githubdemo") return;
        string target = uri.Host.ToLowerInvariant();
        string value = uri.AbsolutePath.Trim('/');
        if (target == "project" && long.TryParse(value, out long id))
        {
            OpenProject(id);
        }
        else if (target == "join" && value.Length > 0)
        {
            await JoinWithCodeAsync(value);
        }
    }

    public async Task JoinWithCodeAsync(string code)
    {
        try
        {
            var reply = await Services.Connection.RequestAsync("project.join", new JsonObject { ["code"] = code });
            if (reply.Str("status") == "joined")
            {
                Notifier.Success($"You're in {reply.Str("name")}.");
                OpenProject(reply.Long("projectId"));
            }
            else
            {
                await Services.Dialogs.AlertAsync("Request sent",
                    $"You asked to join {reply.Str("name")}. You'll get a message in your inbox when an admin approves it.");
            }
        }
        catch (Exception ex)
        {
            Notifier.Error(ViewModelBase.Describe(ex));
        }
    }
}
