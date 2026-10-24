using CommunityToolkit.Mvvm.ComponentModel;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _error = "";

    // Runs a server call: shows the server's error message instead of crashing.
    protected async Task<bool> RunAsync(Func<Task> action, bool busy = true)
    {
        Error = "";
        if (busy) IsBusy = true;
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            Error = Describe(ex);
            return false;
        }
        finally
        {
            if (busy) IsBusy = false;
        }
    }

    // Same, but reports the problem as a notification (for actions in lists).
    protected static async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            Services.Notifier.Error(Describe(ex));
            return false;
        }
    }

    public static string Describe(Exception ex) => ex switch
    {
        ServerException s => s.Message,
        ConnectionLostException => "You're offline. Waiting for the server...",
        TimeoutException => "The server didn't answer in time.",
        _ => ex.Message,
    };
}

// A screen shown in the main window.
public abstract class PageViewModel : ViewModelBase
{
    private readonly List<IDisposable> _subscriptions = new();

    public virtual string Title => "";
    public virtual string NavKey => "";        // which sidebar entry is highlighted
    public virtual bool ShowsSidebar => true;  // false for the sign-in screens

    protected void On(string eventType, Action<System.Text.Json.Nodes.JsonObject> handler) =>
        _subscriptions.Add(Services.Session.On(eventType, handler));

    // Called when the page is shown: subscribes to server events, then loads.
    public async Task EnterAsync()
    {
        Subscribe();
        await LoadAsync();
    }

    protected virtual void Subscribe() { }

    public virtual Task LoadAsync() => Task.CompletedTask;

    public virtual void OnLeave()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
    }

    // The connection came back: reload whatever the page shows.
    public virtual Task OnReconnectedAsync() => LoadAsync();

    // Return false to stay (e.g. unsaved changes the user wants to keep).
    public virtual Task<bool> CanLeaveAsync() => Task.FromResult(true);
}
