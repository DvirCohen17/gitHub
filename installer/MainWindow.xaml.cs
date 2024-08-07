using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using IWshRuntimeLibrary; // Ensure this using directive is present

namespace InstallerApp
{
    public partial class MainWindow : Window
    {
        private const string ConfigFilePath = @"C:\githubDemo\data\config.txt";

        private System.Windows.Threading.DispatcherTimer timer;
        private string serverAddress;
        private int port;

        public MainWindow()
        {
            InitializeComponent();
            LoadServerConfiguration();
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            installButton.IsEnabled = false;
            statusTextBlock.Text = "Status: Connecting to server...";

            // Show the loading animation
            StartLoadingAnimation();

            try
            {
                await InstallClientAsync();
                statusTextBlock.Text = "Status: Installation complete!";

                CreateShortcut();

                // Optionally, launch the client application
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "C:\\githubDemo\\data\\client_side.exe",
                    WorkingDirectory = "C:\\githubDemo\\data", // Set the working directory
                    UseShellExecute = false
                };
                Process.Start(processStartInfo);
                this.Close();
            }
            catch (Exception ex)
            {
                statusTextBlock.Text = "Status: Installation failed.";
                MessageBox.Show($"Error: {ex.Message}", "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StopLoadingAnimation();
            }
        }

        private void StartLoadingAnimation()
        {
            loadingGrid.Visibility = Visibility.Visible;
            Storyboard loadingAnimation = (Storyboard)FindResource("LoadingAnimation");
            loadingAnimation.Begin();
        }

        private void StopLoadingAnimation()
        {
            loadingGrid.Visibility = Visibility.Collapsed;
            Storyboard loadingAnimation = (Storyboard)FindResource("LoadingAnimation");
            loadingAnimation.Stop();
        }

        private async Task InstallClientAsync()
        {
            string zipFilePath = "C:\\githubDemo\\client_files.zip";
            string extractPath = "C:\\githubDemo\\data";
            string installerPath = "C:\\githubDemo\\installer";

            if (!Directory.Exists(extractPath))
            {
                Directory.CreateDirectory(extractPath);
            }
            // Create the installer directory if it doesn't exist
            if (!Directory.Exists(installerPath))
            {
                Directory.CreateDirectory(installerPath);
            }
            using (Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
            {
                if (socket == null)
                {
                    throw new ExternalException("Socket failed to initialize");
                }

                await Task.Run(() => socket.Connect(serverAddress, port));

                byte[] data = Encoding.UTF8.GetBytes("500");
                socket.Send(data);

                using (NetworkStream networkStream = new NetworkStream(socket))
                {
                    // Receive the ZIP file
                    await ReceiveFile(networkStream, zipFilePath);
                }
            }

            // Extract the ZIP file
            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, true);
            }

            ZipFile.ExtractToDirectory(zipFilePath, extractPath);

            // Delete the ZIP file after extraction
            if (System.IO.File.Exists(zipFilePath))
            {
                System.IO.File.Delete(zipFilePath);
            }

            // Copy installer files from the current directory to the installer directory
            CopyInstallerFiles(installerPath);
            // Update config file with new IP and port
            UpdateConfigFile(serverAddress, port);
        }

        private async Task ReceiveFile(NetworkStream networkStream, string destinationPath)
        {
            long totalBytesRead = 0;
            long totalBytes = 0; // Set this to the total size of the file if known or handle dynamically

            using (FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;
                }
            }
        }

        private void LoadServerConfiguration()
        {
            if (System.IO.File.Exists(ConfigFilePath))
            {
                var configData = System.IO.File.ReadAllText(ConfigFilePath);
                var parts = configData.Split(',');
                if (parts.Length == 2)
                {
                    serverAddress = parts[0];
                    port = int.Parse(parts[1]);
                }
            }
            else
            {
                // Prompt user for IP and Port if the config file does not exist
                var inputDialog = new InputDialog();
                if (inputDialog.ShowDialog() == true)
                {
                    serverAddress = inputDialog.IPAddress;
                    port = inputDialog.Port;

                }
                else
                {
                    MessageBox.Show("Configuration is required to proceed.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                }
            }

            // Show the IP and Port on the screen
            statusTextBlock.Text = $"Server Address: {serverAddress}, Port: {port}";
        }

        private void UpdateConfigFile(string newIP, int newPort)
        {
            System.IO.File.WriteAllText(ConfigFilePath, $"{newIP},{newPort}");
        }

        private void CreateShortcut()
        {
            try
            {
                var shell = new WshShell();
                string shortcutAddress = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "giThubDemo.lnk");
                var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);

                shortcut.Description = "New shortcut for GitHub Demo";
                shortcut.TargetPath = @"C:\githubDemo\data\client_side.exe";

                // Set the icon for the shortcut
                string iconFilePath = @"C:\githubDemo\data\icon.ico"; // Path to the icon file
                shortcut.IconLocation = $"{iconFilePath},0"; // Set the icon location (0 for the first icon in the file)

                shortcut.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating shortcut: {ex.Message}", "Shortcut Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyInstallerFiles(string destinationPath)
        {
            try
            {
                // Get the path of the directory where the application is running
                string sourcePath = AppDomain.CurrentDomain.BaseDirectory;

                // Copy all files from the source directory to the destination directory
                foreach (var file in Directory.GetFiles(sourcePath))
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(destinationPath, fileName);
                    System.IO.File.Copy(file, destFile, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying installer files: {ex.Message}", "File Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}