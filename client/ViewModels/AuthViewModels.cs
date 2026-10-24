using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubDemo.Core;

namespace GitHubDemo.ViewModels;

// Server address, connecting, and the version check.
public sealed partial class ConnectViewModel : PageViewModel
{
    private readonly bool _autoConnect;

    public ConnectViewModel(bool autoConnect = false)
    {
        _autoConnect = autoConnect;
        _host = Services.Settings.ServerHost.Length > 0 ? Services.Settings.ServerHost : "127.0.0.1";
        _port = Services.Settings.ServerPort.ToString();
    }

    public override bool ShowsSidebar => false;
    public override string Title => "Connect";

    [ObservableProperty] private string _host;
    [ObservableProperty] private string _port;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _updateMessage = "";
    [ObservableProperty] private bool _updateRequired;
    [ObservableProperty] private bool _showForm = true;

    public override async Task LoadAsync()
    {
        if (_autoConnect)
        {
            ShowForm = false;
            await ConnectAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        Error = "";
        string host = Host.Trim();
        if (host.Length == 0)
        {
            Error = "Enter the server's address.";
            ShowForm = true;
            return;
        }
        if (!int.TryParse(Port.Trim(), out int port) || port is < 1 or > 65535)
        {
            Error = "The port is a number from 1 to 65535.";
            ShowForm = true;
            return;
        }
        IsConnecting = true;
        try
        {
            var hello = await Services.Session.ConnectAsync(host, port);
            Services.Settings.ServerHost = host;
            Services.Settings.ServerPort = port;
            Services.Settings.Save();
            if (hello.UpdateRequired)
            {
                UpdateRequired = true;
                UpdateMessage = $"This server needs gitHub Demo {hello.RequiredVersion} (you have {Session.ClientVersion}).";
                ShowForm = false;
                return;
            }
            await Services.Main.AfterConnectedAsync();
        }
        catch (Exception ex)
        {
            Error = ex is ServerException ? ex.Message : $"Can't connect to {host}:{port}. Check the address and that the server is running.";
            ShowForm = true;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        await RunAsync(() => Updater.DownloadAndInstallAsync(Services.Connection.Host, Services.Connection.Port,
            p => UpdateMessage = $"Downloading the update... {p:P0}"));
    }
}

public sealed partial class LoginViewModel : PageViewModel
{
    public LoginViewModel()
    {
        _user = Services.Settings.RememberedUser;
        _remember = Services.Settings.RememberedUser.Length > 0;
    }

    public override bool ShowsSidebar => false;
    public override string Title => "Sign in";

    [ObservableProperty] private string _user;
    [ObservableProperty] private bool _remember;
    public string Password { get; set; } = "";   // set by the view (PasswordBox can't bind)
    public string ServerLabel => $"{Services.Connection.Host}:{Services.Connection.Port}";

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (User.Trim().Length == 0 || Password.Length == 0)
        {
            Error = "Enter your user name (or email) and password.";
            return;
        }
        string hash = Secrets.PasswordHash(Password);
        if (await RunAsync(() => Services.Session.LoginAsync(User.Trim(), hash)))
        {
            Services.Settings.RememberedUser = Remember ? User.Trim() : "";
            Services.Settings.RememberedSecret = Remember ? Secrets.Protect(hash) : "";
            Services.Settings.Save();
            await Services.Main.AfterSignInAsync();
        }
    }

    [RelayCommand] private void CreateAccount() => Services.Main.Navigate(new SignupViewModel());
    [RelayCommand] private void ChangePassword() => Services.Main.Navigate(new ChangePasswordViewModel(User));

    [RelayCommand]
    private void ChangeServer()
    {
        Services.Connection.Close();
        Services.Main.Navigate(new ConnectViewModel());
    }
}

public sealed partial class SignupViewModel : PageViewModel
{
    public override bool ShowsSidebar => false;
    public override string Title => "Create account";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _nameError = "";
    [ObservableProperty] private string _emailError = "";
    [ObservableProperty] private string _passwordError = "";
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";

    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9._-]{3,20}$");
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    [RelayCommand]
    private async Task CreateAsync()
    {
        NameError = NamePattern.IsMatch(Name.Trim()) ? "" : "3-20 characters: letters, digits, '.', '_' or '-'.";
        EmailError = EmailPattern.IsMatch(Email.Trim()) ? "" : "Enter a valid email address.";
        PasswordError = Password.Length < 6 ? "Use at least 6 characters." : Password != Confirm ? "The passwords don't match." : "";
        if (NameError.Length + EmailError.Length + PasswordError.Length > 0) return;
        if (await RunAsync(() => Services.Session.SignupAsync(Name.Trim(), Email.Trim(), Secrets.PasswordHash(Password))))
        {
            Services.Settings.RememberedUser = Name.Trim();
            Services.Settings.Save();
            await Services.Main.AfterSignInAsync();
        }
    }

    [RelayCommand] private void Back() => Services.Main.Navigate(new LoginViewModel());
}

public sealed partial class ChangePasswordViewModel : PageViewModel
{
    public ChangePasswordViewModel(string user)
    {
        _user = user;
    }

    public override bool ShowsSidebar => false;
    public override string Title => "Change password";

    [ObservableProperty] private string _user;
    [ObservableProperty] private bool _done;
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string Confirm { get; set; } = "";

    [RelayCommand]
    private async Task ChangeAsync()
    {
        if (User.Trim().Length == 0 || OldPassword.Length == 0) { Error = "Enter your user name and current password."; return; }
        if (NewPassword.Length < 6) { Error = "Use at least 6 characters for the new password."; return; }
        if (NewPassword != Confirm) { Error = "The new passwords don't match."; return; }
        if (await RunAsync(() => Services.Connection.RequestAsync("auth.changePassword", new JsonObject
            {
                ["user"] = User.Trim(),
                ["oldPassword"] = Secrets.PasswordHash(OldPassword),
                ["newPassword"] = Secrets.PasswordHash(NewPassword),
            })))
        {
            if (string.Equals(Services.Settings.RememberedUser, User.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Services.Settings.RememberedSecret = "";
                Services.Settings.Save();
            }
            Done = true;
        }
    }

    [RelayCommand] private void Back() => Services.Main.Navigate(new LoginViewModel());
}
