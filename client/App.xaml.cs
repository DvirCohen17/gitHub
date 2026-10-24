using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Threading;
using GitHubDemo.Core;
using GitHubDemo.ViewModels;

namespace GitHubDemo;

public partial class App : Application
{
    // One running copy per Windows user (and data folder); a second start (e.g.
    // clicking a githubdemo:// link) hands its link to the first and exits.
    private static readonly string InstanceName = "gitHubDemo-" + Environment.UserName + "-" +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppSettings.DataDir.ToLowerInvariant())))[..10];
    private Mutex? _instanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? link = e.Args.FirstOrDefault(a => a.StartsWith("githubdemo:", StringComparison.OrdinalIgnoreCase));

        _instanceMutex = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            ForwardToRunningCopy(link ?? "activate");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        Services.Settings = AppSettings.Load();
        ThemeManager.Apply(Services.Settings.Theme);
        Services.Connection = new Connection(Dispatcher);
        Services.Session = new Session(Services.Connection);
        Services.Dialogs = new Dialogs();
        Services.Notifier = new Notifier();
        Services.Main = new MainViewModel();

        var window = new MainWindow { DataContext = Services.Main };
        MainWindow = window;
        window.Show();
        ListenForOtherCopies();
        await Services.Main.StartAsync(link);
    }

    private static void ForwardToRunningCopy(string message)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(message);
        }
        catch (Exception)
        {
            // the other copy is closing; nothing to do
        }
    }

    private void ListenForOtherCopies()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe);
                    string? message = reader.ReadLine();
                    Dispatcher.BeginInvoke(async () =>
                    {
                        if (message != null && message.StartsWith("githubdemo:", StringComparison.OrdinalIgnoreCase))
                            await Services.Main.HandleLinkAsync(message);
                        else
                            Notifier.BringToFront();
                    });
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }
        }) { IsBackground = true, Name = "single instance" };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep running: report it instead of crashing the whole app.
        e.Handled = true;
        try
        {
            File.AppendAllText(Path.Combine(AppSettings.DataDir, "errors.log"), $"{DateTime.Now:u} {e.Exception}\n\n");
        }
        catch (Exception) { }
        Services.Notifier?.Error("Something went wrong: " + e.Exception.Message);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.Connection?.Close();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
