using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GitHubDemo.Core;

// A question shown on top of the current page (inside the main window, not a
// separate pop-up window).
public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string OkText { get; init; } = "OK";
    public string? CancelText { get; init; } = "Cancel";
    public bool IsDanger { get; init; }
    public bool HasInput { get; init; }
    public string InputLabel { get; init; } = "";

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _error = "";

    // Optional check of the typed text: return an error message, or null when it's fine.
    public Func<string, string?>? Validate { get; init; }

    public Task<bool> Result => _result.Task;

    [RelayCommand]
    private void Ok()
    {
        if (HasInput && Validate?.Invoke(Input.Trim()) is { } problem)
        {
            Error = problem;
            return;
        }
        _result.TrySetResult(true);
    }

    [RelayCommand]
    private void Cancel() => _result.TrySetResult(false);
}

public sealed class Dialogs
{
    private async Task<bool> ShowAsync(DialogViewModel dialog)
    {
        // One at a time; later ones wait their turn.
        while (Services.Main.ActiveDialog != null)
        {
            await Task.Delay(100);
        }
        Services.Main.ActiveDialog = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            Services.Main.ActiveDialog = null;
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string okText = "OK", bool danger = false) =>
        ShowAsync(new DialogViewModel { Title = title, Message = message, OkText = okText, IsDanger = danger });

    public Task AlertAsync(string title, string message) =>
        ShowAsync(new DialogViewModel { Title = title, Message = message, OkText = "OK", CancelText = null });

    // Returns the trimmed text, or null if cancelled.
    public async Task<string?> PromptAsync(string title, string label, string initial = "", string okText = "OK",
        Func<string, string?>? validate = null, string message = "")
    {
        var dialog = new DialogViewModel
        {
            Title = title, Message = message, OkText = okText, HasInput = true, InputLabel = label, Validate = validate, Input = initial,
        };
        return await ShowAsync(dialog) ? dialog.Input.Trim() : null;
    }
}
