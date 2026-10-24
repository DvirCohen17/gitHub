using client_side.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace client_side
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {

        private Communicator communicator;
        bool disconnect = true; // if window closed by the user disconnect

        public LoginWindow()
        {
            InitializeComponent();
            MainFrame.Attach(this); // same size and place as the previous screen
            Style = (Style)FindResource(typeof(Window));

            Communicator? connected = ConnectToServer();
            if (connected == null)
            {
                Application.Current.Shutdown();
                Environment.Exit(0);
                return;
            }
            communicator = connected;
            Closing += login_CloseFile; // Hook up the closing event handler
            communicator.ApplyTheme(this);
            Loaded += (_, _) => txtUsername.Focus();
            LoadSavedCredentials();
        }

        // Connects to the configured server. If there is no config, or the server
        // can't be reached, asks the user for the address instead of just exiting.
        private static Communicator? ConnectToServer()
        {
            ServerConfig? config = ServerConfig.Load();
            string reason = "No server is configured yet. Enter the address of the server to connect to.";
            if (config != null)
            {
                try
                {
                    Communicator communicator = new Communicator(config.Host, config.Port);
                    communicator.Initialize();
                    return communicator;
                }
                catch (Exception ex)
                {
                    reason = $"Couldn't connect to {config}: {ex.Message}";
                }
            }

            // Don't let the app shut down when this dialog (the only visible window) closes.
            ShutdownMode previousMode = Application.Current.ShutdownMode;
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                ServerConnectWindow dialog = new ServerConnectWindow(config, reason);
                return dialog.ShowDialog() == true ? dialog.ConnectedCommunicator : null;
            }
            finally
            {
                Application.Current.ShutdownMode = previousMode;
            }
        }

        // "Remember me" restores the saved password hash. It is used as-is unless the
        // user types a new password (hashing the saved hash again would never match).
        private string? savedPasswordHash;

        private void LoadSavedCredentials()
        {
            string savedUsername = Properties.Settings.Default.Username;
            string savedHashedPassword = Communicator.UnprotectSecret(Properties.Settings.Default.HashedPassword);

            if (!string.IsNullOrEmpty(savedUsername) && !string.IsNullOrEmpty(savedHashedPassword))
            {
                txtUsername.Text = savedUsername;
                pwdPassword.Password = "********"; // placeholder dots; the real hash never goes in the box
                savedPasswordHash = savedHashedPassword; // after the line above: PasswordChanged clears it
                chkRememberMe.IsChecked = true;
                AttemptLogin(savedUsername, savedHashedPassword);
            }
        }

        private void pwdPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            savedPasswordHash = null;
        }

        private void AttemptLogin(string username, string hashedPassword)
        {
            try
            {
                string code = ((int)MessageCodes.MC_LOGIN_REQUEST).ToString();
                communicator.SendData($"{code}{username.Length:D5}{username}{hashedPassword.Length:D5}{hashedPassword}");

                string update = communicator.ReceiveData();
                string rep = update.Substring(0, 3);

                if (rep == ((int)MessageCodes.MC_LOGIN_RESP).ToString())
                {
                    string lengthString = update.Substring(3, 5);
                    int Namelength = int.Parse(lengthString);
                    string userName = update.Substring(8, Namelength);

                    string Id = update.Substring(8 + Namelength);
                    communicator.UserName = userName;
                    communicator.UserId = int.Parse(Id);
                    disconnect = false;
                    communicator.UseResponseTimeout(false); // from here on, windows listen in the background

                    if (chkRememberMe.IsChecked == true)
                    { 
                        communicator.settings.UserName = username;
                        communicator.settings.Pass = Communicator.ProtectSecret(hashedPassword);

                        communicator.SaveSettings(communicator.settingsFilePath, communicator.settings);
                        
                    }
                    else
                    {
                        communicator.ClearCredentials();
                    }

                    HomePage filesWindow = new HomePage(communicator);
                    filesWindow.Show();
                    Close();
                }
                else if (rep == ((int)MessageCodes.MC_ERROR_RESP).ToString())
                {
                    lblErrMsg.Text = update.Substring(3);
                }
            }
            catch (Exception ex)
            {
                lblErrMsg.Text = ex.Message;
            }
        }

        private async void login_CloseFile(object sender, EventArgs e)
        {
            if (disconnect)
            {
                try
                {
                    string chatMessageCode = ((int)MessageCodes.MC_DISCONNECT).ToString();

                    string fullMessage = $"{chatMessageCode}";

                    communicator.SendData(fullMessage);

                    // Close the window on the UI thread
                    await Dispatcher.InvokeAsync(() => Close());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during closing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public LoginWindow(Communicator _communicator)
        {
            InitializeComponent();
            MainFrame.Attach(this); // same size and place as the previous screen
            Style = (Style)FindResource(typeof(Window));
            communicator = _communicator;
            communicator.UseResponseTimeout(true);
            Closing += login_CloseFile; // Hook up the closing event handler
            communicator.ApplyTheme(this);
            Loaded += (_, _) => txtUsername.Focus();
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = txtUsername.Text.Trim();
                if (name.Length == 0 || pwdPassword.Password.Length == 0)
                {
                    lblErrMsg.Text = "Enter your username (or email) and password.";
                    return;
                }

                string pass = savedPasswordHash ?? communicator.HashPassword(pwdPassword.Password);
                AttemptLogin(name, pass);
            }
            catch(Exception ex)
            {
                lblErrMsg.Text = ex.Message;
            }
        }

        private void hprlnkSignup(object sender, RoutedEventArgs e)
        {
            disconnect = false;
            SignupWindow signupWindow = new SignupWindow(communicator);
            signupWindow.Show();
            Close();
        }

        private void hprlnkForgotPassword(object sender, RoutedEventArgs e)
        {
            disconnect = false;
            ForgotPasswordWindow forgotPassWindow = new ForgotPasswordWindow(communicator);
            forgotPassWindow.Show();
            Close();
        }

    }
}
