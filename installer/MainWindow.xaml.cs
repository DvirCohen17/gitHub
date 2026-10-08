using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IWshRuntimeLibrary;

namespace InstallerApp
{
    public partial class MainWindow : Window
    {
        private const string InstallRoot = @"C:\githubDemo";
        private static readonly string DataPath = Path.Combine(InstallRoot, "data");
        private static readonly string InstallerPath = Path.Combine(InstallRoot, "installer");
        private static readonly string ConfigFilePath = Path.Combine(DataPath, "config.txt");
        private static readonly string ClientExePath = Path.Combine(DataPath, "client_side.exe");

        private const string DownloadRequest = "500";   // MC_RECEIVE_FILES_REQUEST
        private const string ErrorResponse = "200";     // MC_ERROR_RESP
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

        private CancellationTokenSource? cancellation;

        public MainWindow()
        {
            InitializeComponent();
            LoadServerConfiguration();

            // The client launches us with --update when the server has a newer version.
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--update", StringComparison.OrdinalIgnoreCase)))
            {
                statusTextBlock.Text = "A new version is available. Updating...";
                Loaded += (_, _) => InstallButton_Click(this, new RoutedEventArgs());
            }
        }

        private void LoadServerConfiguration()
        {
            string host = "127.0.0.1";
            string port = "12345";
            try
            {
                if (System.IO.File.Exists(ConfigFilePath))
                {
                    string[] parts = System.IO.File.ReadAllText(ConfigFilePath).Trim().Split(',');
                    if (parts.Length == 2)
                    {
                        host = parts[0].Trim();
                        port = parts[1].Trim();
                    }
                }
            }
            catch (IOException) { }
            hostTextBox.Text = host;
            portTextBox.Text = port;
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            string host = hostTextBox.Text.Trim();
            if (host.Length == 0 || !int.TryParse(portTextBox.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                statusTextBlock.Text = "Enter a server address and a port between 1 and 65535.";
                return;
            }

            SetBusy(true);
            cancellation = new CancellationTokenSource();
            try
            {
                await InstallClientAsync(host, port, cancellation.Token);
                CreateShortcut();
                statusTextBlock.Text = "Installation complete. Starting gitHub Demo...";

                Process.Start(new ProcessStartInfo
                {
                    FileName = ClientExePath,
                    WorkingDirectory = DataPath,
                    UseShellExecute = false
                });
                Close();
            }
            catch (OperationCanceledException)
            {
                statusTextBlock.Text = "Installation cancelled. Your existing installation was not changed.";
            }
            catch (Exception ex)
            {
                statusTextBlock.Text = $"Installation failed: {ex.Message}\nYour existing installation was not changed.";
            }
            finally
            {
                SetBusy(false);
                cancellation = null;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancellation?.Cancel();
        }

        private void SetBusy(bool busy)
        {
            installButton.IsEnabled = !busy;
            serverPanel.IsEnabled = !busy;
            cancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            progressBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            if (busy)
            {
                progressBar.IsIndeterminate = true;
            }
        }

        private void ReportStatus(string text, double? percent = null)
        {
            Dispatcher.Invoke(() =>
            {
                statusTextBlock.Text = text;
                if (percent.HasValue)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = percent.Value;
                }
            });
        }

        // Download -> extract into a staging folder -> swap with the current install.
        // Nothing under C:\githubDemo\data is touched until the new files are complete.
        private async Task InstallClientAsync(string host, int port, CancellationToken token)
        {
            Directory.CreateDirectory(InstallRoot);
            string zipPath = Path.Combine(Path.GetTempPath(), $"githubDemo-{Guid.NewGuid():N}.zip");
            string stagingPath = DataPath + ".new";
            string backupPath = DataPath + ".old";

            try
            {
                ReportStatus($"Connecting to {host}:{port}...");
                await DownloadPackageAsync(host, port, zipPath, token);

                ReportStatus("Extracting files...");
                if (Directory.Exists(stagingPath))
                {
                    Directory.Delete(stagingPath, true);
                }
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingPath), token);
                if (!System.IO.File.Exists(Path.Combine(stagingPath, "client_side.exe")))
                {
                    throw new InvalidDataException("the downloaded package doesn't contain client_side.exe.");
                }
                token.ThrowIfCancellationRequested();

                ReportStatus("Waiting for gitHub Demo to close...");
                await WaitForClientToExitAsync(token);

                ReportStatus("Installing...");
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }
                if (Directory.Exists(DataPath))
                {
                    Directory.Move(DataPath, backupPath);
                }
                try
                {
                    Directory.Move(stagingPath, DataPath);
                }
                catch
                {
                    if (Directory.Exists(backupPath) && !Directory.Exists(DataPath))
                    {
                        Directory.Move(backupPath, DataPath); // put the old install back
                    }
                    throw;
                }

                System.IO.File.WriteAllText(ConfigFilePath, $"{host},{port}");
                CopyInstallerFiles();
                TryDeleteDirectory(backupPath);
            }
            finally
            {
                TryDeleteFile(zipPath);
                TryDeleteDirectory(stagingPath);
            }
        }

        private async Task DownloadPackageAsync(string host, int port, string destinationPath, CancellationToken token)
        {
            using Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connectTimeout.CancelAfter(ConnectTimeout);
                try
                {
                    await socket.ConnectAsync(host, port, connectTimeout.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException($"the server at {host}:{port} did not answer.");
                }
                catch (SocketException ex)
                {
                    throw new IOException($"couldn't connect to {host}:{port} ({ex.Message}).");
                }
            }

            using NetworkStream stream = new NetworkStream(socket, ownsSocket: false);
            await SendFrameAsync(stream, DownloadRequest, token);

            // The server answers with one framed "500<size>" (or "200<error>"), then the raw zip.
            string header = await ReceiveFrameAsync(stream, token);
            if (header.StartsWith(ErrorResponse))
            {
                throw new IOException(header.Substring(ErrorResponse.Length));
            }
            if (!header.StartsWith(DownloadRequest) || !long.TryParse(header.Substring(DownloadRequest.Length), out long totalBytes) || totalBytes <= 0)
            {
                throw new InvalidDataException("unexpected reply from the server.");
            }

            using FileStream file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            byte[] buffer = new byte[64 * 1024];
            long received = 0;
            while (received < totalBytes)
            {
                int toRead = (int)Math.Min(buffer.Length, totalBytes - received);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), token);
                if (read == 0)
                {
                    throw new IOException("the connection closed before the download finished.");
                }
                await file.WriteAsync(buffer.AsMemory(0, read), token);
                received += read;
                ReportStatus($"Downloading... {received / 1048576.0:0.0} of {totalBytes / 1048576.0:0.0} MB",
                             received * 100.0 / totalBytes);
            }
        }

        // Same framing as the client and server: '#' + 8-digit length + payload.
        private static async Task SendFrameAsync(NetworkStream stream, string payload, CancellationToken token)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            byte[] frame = Encoding.ASCII.GetBytes("#" + body.Length.ToString("D8")).Concat(body).ToArray();
            await stream.WriteAsync(frame, token);
        }

        private static async Task<string> ReceiveFrameAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] header = await ReadExactlyAsync(stream, 9, token);
            if (header[0] != (byte)'#' || !int.TryParse(Encoding.ASCII.GetString(header, 1, 8), out int length) || length > 1024 * 1024)
            {
                throw new InvalidDataException("unexpected reply from the server (is it an older server version?).");
            }
            return Encoding.UTF8.GetString(await ReadExactlyAsync(stream, length, token));
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count, CancellationToken token)
        {
            byte[] buffer = new byte[count];
            int received = 0;
            while (received < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(received, count - received), token);
                if (read == 0)
                {
                    throw new IOException("the server closed the connection.");
                }
                received += read;
            }
            return buffer;
        }

        private static async Task WaitForClientToExitAsync(CancellationToken token)
        {
            for (int i = 0; i < 40; i++)
            {
                bool running = Process.GetProcessesByName("client_side").Any(p =>
                {
                    try { return string.Equals(p.MainModule?.FileName, ClientExePath, StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                });
                if (!running)
                {
                    return;
                }
                await Task.Delay(250, token);
            }
            throw new IOException("gitHub Demo is still running. Close it and try again.");
        }

        private void CreateShortcut()
        {
            try
            {
                var shell = new WshShell();
                string shortcutAddress = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "gitHub Demo.lnk");
                var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);

                shortcut.Description = "gitHub Demo";
                shortcut.TargetPath = ClientExePath;
                shortcut.WorkingDirectory = DataPath; // the client looks for config.txt here

                string iconFilePath = Path.Combine(DataPath, "icon.ico");
                shortcut.IconLocation = System.IO.File.Exists(iconFilePath) ? $"{iconFilePath},0" : $"{ClientExePath},0";

                shortcut.Save();
            }
            catch (Exception ex)
            {
                // The install itself succeeded; a missing shortcut isn't worth failing over.
                Debug.WriteLine($"Shortcut not created: {ex.Message}");
            }
        }

        // Keep a copy of the installer in C:\githubDemo\installer so the client can run it to update.
        private static void CopyInstallerFiles()
        {
            string sourcePath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\');
            if (string.Equals(sourcePath, Path.GetFullPath(InstallerPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return; // already running from the installed copy (update flow)
            }
            Directory.CreateDirectory(InstallerPath);
            foreach (string file in Directory.GetFiles(sourcePath))
            {
                System.IO.File.Copy(file, Path.Combine(InstallerPath, Path.GetFileName(file)), overwrite: true);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch (IOException) { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
