﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using client_side.Properties;
using System.Diagnostics;

namespace client_side
{
    enum MessageCodes
    {
        MC_INITIAL_REQUEST = 101, //requests
        MC_INSERT_REQUEST = 102,
        MC_DELETE_REQUEST = 103,
        MC_REPLACE_REQUEST = 104,
        MC_CREATE_FILE_REQUEST = 105,
        MC_GET_FILES_REQUEST = 106,
        MC_CLOSE_FILE_REQUEST = 108,
        MC_GET_CHAT_MESSAGES_REQUEST = 109,
        MC_GET_USERS_ON_FILE_REQUEST = 110,
        MC_POST_MSG_REQUEST = 111,
        MC_ENTER_FILE_REQUEST = 112,
        MC_EXIT_FILE_REQUEST = 113,
        MC_DELETE_FILE_REQUEST = 114,
        MC_GET_USERS_REQUEST = 115,
        MC_GET_USERS_PERMISSIONS_REQ_REQUEST = 116,
        MC_APPROVE_PERMISSION_REQUEST = 117,
        MC_REJECT_PERMISSION_REQUEST = 118,
        MC_PERMISSION_FILE_REQ_REQUEST = 119,
        MC_PROFILE_INFO_REQUEST = 120,
        MC_FRIENDS_LIST_REQUEST = 121,
        MC_PROJECTS_LIST_REQUEST = 122,
        MC_ADD_FRIEND_REQUEST = 123,
        MC_REMOVE_FRIEND_REQUEST = 124,
        MC_SEARCH_REQUEST = 125,
        MC_ENTER_PROJECT_REQUEST = 126,
        MC_EDIT_PROFILE_INFO_REQUEST = 127,
        MC_CREATE_PROJECT_REQUEST = 128,
        MC_MOVE_TO_CREATE_PROJ_WINDOW_REQUEST = 129,
        MC_GET_PROJECT_FILES_REQUEST = 130,
        MC_EXIT_PROJECT_REQUEST = 131,
        MC_DELETE_PROJECT_REQUEST = 132,
        MC_RENAME_FILE_REQUEST = 133,
        MC_GET_PROFILE_IMAGE_REQUEST = 134,
        MC_UPLOAD_PROFILE_IMAGE_REQUEST = 135,
        MC_APPROVE_FRIEND_REQ_REQUEST = 136,
        MC_REJECT_FRIEND_REQ_REQUEST = 137,
        MC_FRIEND_REQ_REQUEST = 138,
        MC_SEARCH_FRIENDS_REQUEST = 139,
        MC_BACK_TO_HOME_PAGE_REQUEST = 140,
        MC_USER_PROJECTS_LIST_REQUEST = 141,
        MC_LEAVE_PROJECT_REQUEST = 142,
        MC_ACCEPT_PROJECT_INVITE_REQUEST = 143,
        MC_DECLINE_PROJECT_INVITE_REQUEST = 144,
        MC_VIEW_PROJECT_INFO_REQUEST = 145,
        MC_EDIT_PROJECT_INFO_REQUEST = 146,
        MC_UPDATE_PROJECT_LIST_REQUEST = 147,
        MC_GET_PROJECT_INFO_REQUEST = 148,
        MC_MODIFY_PROJECT_INFO_REQUEST = 149,
        MC_GET_CODE_STYLES_REQUEST = 150,
        MC_SETTINGS_REQUEST = 151,
        MC_GET_MAIL_IMAGES_REQUEST = 152,
        MC_GET_MESSAGES_REQUEST = 153,
        MC_MOVE_TO_MESSAGES_REQUEST = 154,
        MC_GET_MSG_COUNT_REQUEST = 155,
        MC_SEND_MSG_REQUEST = 156,
        MC_MARK_AS_READ_REQUEST = 157,
        MC_ADD_TASK_REQUEST = 158,
        MC_MARK_TASK_AS_COMPLETED_REQUEST = 159,
        MC_MARK_TASK_AS_NOT_COMPLETED_REQUEST = 160,
        MC_DELETE_TASK_REQUEST = 161,
        MC_GET_CURRENT_PROJECT_ISSUES_REQUEST = 162,
        MC_GET_COMPLETED_PROJECT_ISSUES_REQUEST = 163,
        MC_GET_PROJECT_PATICIPANTS_REQUEST = 164,
        MC_MOVE_TO_ISSUE_DATA_WINDOW_REQUEST = 165,
        MC_GET_ISSUE_REQUEST = 166,
        MC_BACK_TO_PROJECT_PAGE_REQUEST = 167,
        MC_MOVE_TO_TO_DO_LIST_REQUEST = 168,
        MC_MOVE_TO_PROJECT_PAGE_REQUEST = 169,
        MC_BACK_TO_TO_DO_LIST_PAGE_REQUEST = 170,
        MC_MODIFY_ISSUE_REQUEST = 171,
        MC_MARK_ALL_AS_READ_REQUEST = 172,

        MC_ERROR_RESP = 200, //responses
        MC_INITIAL_RESP = 201,
        MC_INSERT_RESP = 202,
        MC_DELETE_RESP = 203,
        MC_REPLACE_RESP = 204,
        MC_CREATE_FILE_RESP = 205,
        MC_GET_FILES_RESP = 206,
        MC_ADD_FILE_RESP = 207,
        MC_CLOSE_FILE_RESP = 208,
        MC_GET_CHAT_MESSAGES_RESP = 209,
        MC_GET_USERS_ON_FILE_RESP = 210,
        MC_POST_MSG_RESP = 211,
        MC_ENTER_FILE_RESP = 212,
        MC_EXIT_FILE_RESP = 213,
        MC_DELETE_FILE_RESP = 214,
        MC_GET_USERS_RESP = 215,
        MC_GET_USERS_PERMISSIONS_REQ_RESP = 216,
        MC_APPROVE_PERMISSION_RESP = 217,
        MC_REJECT_PERMISSION_RESP = 218,
        MC_PERMISSION_FILE_REQ_RESP = 219,
        MC_PROFILE_INFO_RESP = 220,
        MC_FRIENDS_LIST_RESP = 221,
        MC_PROJECTS_LIST_RESP = 222,
        MC_ADD_FRIEND_RESP = 223,
        MC_REMOVE_FRIEND_RESP = 224,
        MC_SEARCH_RESP = 225,
        MC_ENTER_PROJECT_RESP = 226,
        MC_EDIT_PROFILE_INFO_RESP = 227,
        MC_CREATE_PROJECT_RESP = 228,
        MC_MOVE_TO_CREATE_PROJ_WINDOW_RESP = 229,
        MC_GET_PROJECT_FILES_RESP = 230,
        MC_EXIT_PROJECT_RESP = 231,
        MC_DELETE_PROJECT_RESP = 232,
        MC_RENAME_FILE_RESP = 233,
        MC_GET_PROFILE_IMAGE_RESP = 234,
        MC_UPLOAD_PROFILE_IMAGE_RESP = 235,
        MC_APPROVE_FRIEND_REQ_RESP = 236,
        MC_REJECT_FRIEND_REQ_RESP = 237,
        MC_FRIEND_REQ_RESP = 238,
        MC_SEARCH_FRIENDS_RESP = 239,
        MC_BACK_TO_HOME_PAGE_RESP = 240,
        MC_USER_PROJECTS_LIST_RESP = 241,
        MC_LEAVE_PROJECT_RESP = 242,
        MC_ACCEPT_PROJECT_INVITE_RESP = 243,
        MC_DECLINE_PROJECT_INVITE_RESP = 244,
        MC_VIEW_PROJECT_INFO_RESP = 245,
        MC_EDIT_PROJECT_INFO_RESP = 246,
        MC_UPDATE_PROJECT_LIST_RESP = 247,
        MC_GET_PROJECT_INFO_RESP = 248,
        MC_MODIFY_PROJECT_INFO_RESP = 249,
        MC_GET_CODE_STYLES_RESP = 250,
        MC_SETTINGS_RESP = 251,
        MC_GET_MAIL_IMAGES_RESP = 252,
        MC_GET_MESSAGES_RESP = 253,
        MC_MOVE_TO_MESSAGES_RESP = 254,
        MC_GET_MSG_COUNT_RESP = 255,
        MC_SEND_MSG_RESP = 256,
        MC_MARK_AS_READ_RESP = 257,
        MC_ADD_TASK_RESP = 258,
        MC_MARK_TASK_AS_COMPLETED_RESP = 259,
        MC_MARK_TASK_AS_NOT_COMPLETED_RESP = 260,
        MC_DELETE_TASK_RESP = 261,
        MC_GET_CURRENT_PROJECT_ISSUES_RESP = 262,
        MC_GET_COMPLETED_PROJECT_ISSUES_RESP = 263,
        MC_GET_PROJECT_PATICIPANTS_RESP = 264,
        MC_MOVE_TO_ISSUE_DATA_WINDOW_RESP = 265,
        MC_GET_ISSUE_RESP = 266,
        MC_BACK_TO_PROJECT_PAGE_RESP = 267,
        MC_MOVE_TO_TO_DO_LIST_RESP = 268,
        MC_MOVE_TO_PROJECT_PAGE_RESP = 269,
        MC_BACK_TO_TO_DO_LIST_PAGE_RESP = 270,
        MC_MODIFY_ISSUE_RESP = 271,
        MC_MARK_ALL_AS_READ_RESP = 272,

        MC_DISCONNECT = 300, //user
        MC_LOGIN_REQUEST = 301,
        MC_LOGOUT_REQUEST = 306,
        MC_SIGNUP_REQUEST = 303,
        MC_FORGOT_PASSW_REQUEST = 304,
        MC_HEARTBEAT_REQUEST = 307,

        MC_APPROVE_REQ_RESP = 302,
        MC_APPROVE_JOIN_RESP = 305,

        MC_LOGIN_RESP = 401,
        MC_SIGNUP_RESP = 403,
        MC_FORGOT_PASSW_RESP = 404,
        MC_LOGOUT_RESP = 406,

        MC_RECEIVE_FILES_REQUEST = 500,
        MC_VERSION_REQUEST = 501,
        MC_VERSION_RESP = 502,

    };

    public class Theme
    {
        public Brush Background { get; set; }
        public Brush Foreground { get; set; }
        public Brush ButtonBackground { get; set; }
        public Brush ButtonForeground { get; set; }
        public SolidColorBrush ButtonBorder { get; }
        public Brush TextColor { get; set; } 
        public string theame {  get; set; }

        public Theme(
            SolidColorBrush background,
            SolidColorBrush foreground,
            SolidColorBrush buttonBackground,
            SolidColorBrush buttonForeground,
            SolidColorBrush buttonBorder,
            SolidColorBrush textColor,
            string theame)
        {
            Background = background;
            Foreground = foreground;
            ButtonBackground = buttonBackground;
            ButtonForeground = buttonForeground;
            ButtonBorder = buttonBorder;
            TextColor = textColor;
            this.theame = theame;
        }
    }

    public class AppSettings
    {
        public string Theme { get; set; }
        public string Pass { get; set; }
        public string UserName { get; set; }
        public string Version { get; set; }
    }

    public class Communicator
    {
        public string CodeStylesDir = @"C:\githubDemo\data\codeStyles";
        public string MailImagesDir = @"C:\githubDemo\data\MailImages";
        public string settingsFilePath = "C:\\githubDemo\\settings\\appsettings.json";
        public Theme AppTheme;

        private Socket m_socket;
        public int UserId { get; set; }
        public string UserName { get; set; }

        public event EventHandler ThemeChanged;

        public AppSettings settings { get; set; }
        public Communicator(string ip, int port)
        {
            m_socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            if (m_socket == null)
            {
                throw new ExternalException("Socket failed to initialize");
            }

            m_socket.Connect(ip, port);

            LoadSettings();
        }

        private void LoadSettings()
        {
            
            settings = null;

            if (File.Exists(settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(settingsFilePath);
                    settings = JsonConvert.DeserializeObject<AppSettings>(json);
                }
                catch (Exception ex)
                {
                    // Handle the error (e.g., log it)
                    MessageBox.Show($"Error loading settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            SendData("501");
            string response = ReceiveData();
            string version = response.Substring(3);

            if (settings == null || string.IsNullOrEmpty(settings.Theme))
            {
                settings = new AppSettings { Theme = "Dark" , Pass = "", UserName = "", Version = version};
                SaveSettings(settingsFilePath, settings);
            }

            if (settings.Version != version)
            {
                // Notify the user that an update is needed
                MessageBox.Show("A new version is available. The application will now update.", "Update Available", MessageBoxButton.OK, MessageBoxImage.Information);

                settings.Version = version;
                // Save any necessary data before closing
                SaveSettings(settingsFilePath, settings);

                // Close the current application
                Application.Current.Shutdown();

                // Check if the installer file exists
                string installerPath = @"C:\githubDemo\installer\Installer.exe";
                if (File.Exists(installerPath))
                {
                    try
                    {
                        // Start the installer
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = installerPath,
                            WorkingDirectory = "C:\\githubDemo\\installer", // Set the working directory
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    }
                    catch (Exception ex)
                    {
                        // Handle any errors that occur when starting the process
                        MessageBox.Show($"Error starting installer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // Notify the user if the installer file is not found
                    MessageBox.Show("Installer file not found. Please check the file path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                Environment.Exit(0);
            }

            // Apply the settings
            Properties.Settings.Default.Theme = settings.Theme;
            Properties.Settings.Default.Version = settings.Version;
            Properties.Settings.Default.Username = settings.UserName;
            Properties.Settings.Default.HashedPassword = settings.Pass;
            Properties.Settings.Default.Save();
            ModifyTheme(Properties.Settings.Default.Theme);

        }

        public void SaveSettings(string settingsFilePath, AppSettings settings)
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(settingsFilePath);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string json = JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                // Handle the error (e.g., log it)
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        ~Communicator()
        {
            m_socket.Close();
        }

        public void ModifyTheme(string theme)
        {
            switch (theme.ToLower())
            {
                case "light":
                    AppTheme = new Theme(
                        Brushes.White,
                        Brushes.Black,
                        Brushes.White,
                        Brushes.Black,
                        Brushes.Black,
                        Brushes.Black, "Light");
                    break;
                case "dark":
                    AppTheme = new Theme(
                        Brushes.Black,
                        Brushes.White,
                        Brushes.Black,
                        Brushes.White,
                        Brushes.White,
                        Brushes.White, "Dark");
                    break;
                case "blue":
                    AppTheme = new Theme(
                        Brushes.LightBlue,
                        Brushes.DarkBlue,
                        Brushes.LightBlue,
                        Brushes.DarkBlue,
                        Brushes.DarkBlue,
                        Brushes.DarkBlue, "Blue");
                    break;
                case "green":
                    AppTheme = new Theme(
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.LightGreen,
                        Brushes.DarkGreen,
                        Brushes.DarkGreen,
                        Brushes.DarkGreen, "Green");
                    break;
                case "red":
                    AppTheme = new Theme(
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.LightCoral,
                        Brushes.DarkRed,
                        Brushes.DarkRed,
                        Brushes.DarkRed, "Red");
                    break;
                case "cyberpunk":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(10, 0, 50)),
                        new SolidColorBrush(Color.FromRgb(0, 255, 255)),
                        new SolidColorBrush(Color.FromRgb(255, 20, 147)),
                        Brushes.White,
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(0, 255, 255)), "CyberPunk");
                    break;
                case "matrix":
                    AppTheme = new Theme(
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                        new SolidColorBrush(Color.FromRgb(0, 255, 0)), "Matrix");
                    break;
                case "solarized light":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(253, 246, 227)),
                        new SolidColorBrush(Color.FromRgb(101, 123, 131)),
                        new SolidColorBrush(Color.FromRgb(238, 232, 213)),
                        new SolidColorBrush(Color.FromRgb(88, 110, 117)),
                        new SolidColorBrush(Color.FromRgb(88, 110, 117)),
                        new SolidColorBrush(Color.FromRgb(101, 123, 131)), "Solarized Light");
                    break;
                case "solarized dark":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(0, 43, 54)),
                        new SolidColorBrush(Color.FromRgb(131, 148, 153)),
                        new SolidColorBrush(Color.FromRgb(7, 54, 33)),
                        new SolidColorBrush(Color.FromRgb(147, 161, 161)),
                        new SolidColorBrush(Color.FromRgb(147, 161, 161)),
                        new SolidColorBrush(Color.FromRgb(131, 148, 153)), "Solarized Dark");
                    break;
                case "vintage":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(244, 227, 227)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(208, 182, 182)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)),
                        new SolidColorBrush(Color.FromRgb(108, 79, 79)), "Vintage");
                    break;
                case "neon":
                    AppTheme = new Theme(
                        Brushes.Black,
                        new SolidColorBrush(Color.FromRgb(57, 255, 20)),
                        new SolidColorBrush(Color.FromRgb(255, 0, 127)),
                        Brushes.White,
                        Brushes.White,
                        new SolidColorBrush(Color.FromRgb(57, 255, 20)), "Neon");
                    break;
                case "pastel":
                    AppTheme = new Theme(
                        new SolidColorBrush(Color.FromRgb(251, 232, 235)),
                        new SolidColorBrush(Color.FromRgb(195, 162, 176)),
                        new SolidColorBrush(Color.FromRgb(245, 227, 230)),
                        new SolidColorBrush(Color.FromRgb(176, 58, 106)),
                        new SolidColorBrush(Color.FromRgb(176, 58, 106)),
                        new SolidColorBrush(Color.FromRgb(195, 162, 176)), "Pastel");
                    break;
                default:
                    AppTheme = new Theme(
                        Brushes.White,
                        Brushes.Black,
                        Brushes.White,
                        Brushes.Black,
                        Brushes.Black,
                        Brushes.Black,
                        "Light");
                    break;
            }
            SaveSettings(theme);
            ThemeChanged?.Invoke(this, EventArgs.Empty);

            settings.Theme = theme;

            SaveSettings(settingsFilePath, settings);
        }

        public void ApplyTheme(Window window)
        {
            if (AppTheme != null)
            {
                var resourceDictionary = new ResourceDictionary();

                window.Background = AppTheme.Background;
                window.Foreground = AppTheme.Foreground;

                if (window.Content is Panel panel)
                {
                    ApplyThemeToControls(panel);
                }
            }
        }

        private void ApplyThemeToControls(Panel panel)
        {
            if (panel == null)
                return;

            foreach (var control in panel.Children)
            {
                ApplyThemeToControl(control as DependencyObject); // Cast to DependencyObject
            }
        }

        private void ApplyThemeToControl(DependencyObject control)
        {
            if (control == null)
                return;

            if (control is Button button)
            {
                // Apply the background and foreground colors
                button.Background = AppTheme.ButtonBackground;
                button.Foreground = AppTheme.ButtonForeground;

                // Apply the border color for buttons
                button.BorderBrush = AppTheme.ButtonBorder;
            }
            else if (control is TextBlock textBlock)
            {
                textBlock.Foreground = AppTheme.TextColor; // Apply text color

            }
            else if (control is TextBox textBox)
            {
                textBox.Foreground = AppTheme.TextColor; // Apply text color
                textBox.BorderBrush = AppTheme.TextColor; 
            }
            else if (control is Border border)
            {
                border.BorderBrush = AppTheme.TextColor; // Apply text color
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.Foreground = AppTheme.TextColor; // Apply text color
                if (checkBox.Content is string contentString)
                {
                    checkBox.Content = new TextBlock
                    {
                        Text = contentString,
                        Foreground = AppTheme.TextColor
                    };
                }
                else if (checkBox.Content is TextBlock contentTextBlock)
                {
                    contentTextBlock.Foreground = AppTheme.TextColor;
                }
            }
            else if (control is ListBox listBox)
            {
                listBox.Foreground = AppTheme.TextColor; // Apply text color
                foreach (var item in listBox.Items)
                {
                    // Find the ListBoxItem and apply theme to it
                    ListBoxItem listBoxItem = listBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    if (listBoxItem != null)
                    {
                        ApplyThemeToControl(listBoxItem);
                    }
                }
            }
            else if (control is ListView listView)
            {
                listView.Foreground = AppTheme.TextColor;
                foreach (var item in listView.Items)
                {
                    ListViewItem listViewItem = listView.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
                    if (listViewItem != null)
                    {
                        ApplyThemeToControl(listViewItem);
                    }
                }
            }
            else if (control is Panel childPanel)
            {
                foreach (var child in childPanel.Children)
                {
                    ApplyThemeToControl(child as DependencyObject); // Cast to DependencyObject
                }
            }
            else if (control is ListBoxItem listBoxItem)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(listBoxItem); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(listBoxItem, i);
                    ApplyThemeToControl(child);
                }
            }
            else if (control is ListViewItem listViewItem)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(listViewItem); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(listViewItem, i);
                    ApplyThemeToControl(child);
                }
            }
        }

        private void ApplyThemeToControl(ListBoxItem listBoxItem)
        {
            if (listBoxItem == null)
                return;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(listBoxItem); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(listBoxItem, i);
                ApplyThemeToControl(child);
            }
        }

        private void SaveSettings(string theme)
        {
            // Save the selected theme to the application settings
            Properties.Settings.Default.Theme = theme;
            Properties.Settings.Default.Save();
        }

        public void SaveCredentials(string username, string hashedPassword)
        {
            Properties.Settings.Default.Username = username;
            Properties.Settings.Default.HashedPassword = hashedPassword;
            Properties.Settings.Default.Save();
        }

        public void ClearCredentials()
        {
            Properties.Settings.Default.Username = string.Empty;
            Properties.Settings.Default.HashedPassword = string.Empty;
            Properties.Settings.Default.Save();

            settings.UserName = "";
            settings.Pass = "";

            SaveSettings(settingsFilePath, settings);
        }

        public string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hashedBytes.Length; i++)
                {
                    builder.Append(hashedBytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public void SendData(string message)
        {
            //LogAction(message);
           byte[] data = Encoding.UTF8.GetBytes(message);
           m_socket.Send(data); 
        }

        public void SendImage(byte[] message)
        {
            //LogAction(message);
            m_socket.Send(message);
        }

        public string ReceiveData()
        {
            byte[] buffer = new byte[512];  // Adjust the buffer size as needed
            int bytesRead = m_socket.Receive(buffer);
            string rep = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            //LogAction(rep);
            return rep;
        }

        public string ReceiveFileData()
        {
            byte[] buffer = new byte[30000];  // Adjust the buffer size as needed
            int bytesRead = m_socket.Receive(buffer);
            string rep = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            //LogAction(rep);
            return rep;
        }

        public string ReceiveImage(int imageSize)
        {
            byte[] buffer = new byte[imageSize];
            int received = 0;

            while (received < imageSize)
            {
                int bytes = m_socket.Receive(buffer, received, imageSize - received, SocketFlags.None);
                if (bytes == 0) throw new Exception("Connection closed prematurely.");
                received += bytes;
            }

            return Convert.ToBase64String(buffer);
        }

        /*
        public string ReceiveData(CancellationToken cancellationToken)
        {
            // Adjust the timeout value as needed
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            // Use CancellationTokenSource to apply timeout
            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    byte[] buffer = new byte[1024];  // Adjust the buffer size as needed
                    int bytesRead = m_socket.Receive(buffer);
                    string rep = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    LogAction(rep);
                    return rep;
                }
                catch (OperationCanceledException)
                {
                    // Handle the case where the operation is canceled due to timeout
                    throw new TimeoutException("Receive operation timed out.");
                }
                catch (Exception ex)
                {
                    // Handle other exceptions
                    throw new Exception($"Error receiving data: {ex.Message}");
                }
            }
        }

        public string ReceiveDataWithTimeout(TimeSpan timeout)
        {
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                {
                    var token = cts.Token;

                    Task<string> receiveTask = Task.Run(() =>
                    {
                        // Replace the following line with your actual ReceiveData logic
                        // Here, I'm assuming ReceiveData returns a string
                        return ReceiveData();
                    }, token);

                    // Wait for the task to complete within the specified timeout
                    if (receiveTask.Wait(timeout))
                    {
                        // Task completed within the timeout
                        return receiveTask.Result;
                    }
                    else
                    {
                        // Timeout occurred
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions as needed
                Console.WriteLine($"Error in ReceiveDataWithTimeout: {ex.Message}");
                return null;
            }
        }

        */
        public void LogAction(string action)
        {
            try
            {
                string logFilePath = "UserLog_" + UserName + ".txt";

                // Append the action to the log file
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {action}\n");
                //File.AppendAllText(logFilePath, $"{action}\n"); // - just the msg without date
            }
            catch (Exception ex)
            {
                // Handle the exception or log it to another source
                MessageBox.Show($"Error logging action: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public bool ContainsSqlInjection(string input)
        {
            // Define a list of special characters commonly used in SQL injection
            string[] sqlSpecialCharacters = { "'", ";", "--", "/*", "*/", "xp_", "exec", "sp_"};

            // Check if the input contains any of the special characters
            foreach (var specialCharacter in sqlSpecialCharacters)
            {
                if (input.Contains(specialCharacter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
